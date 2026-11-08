using RoboB3.Application.Abstractions;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.MarketData;

/// <summary>Horário do gráfico: segundos do relógio de Brasília (o gráfico mostra a hora como está).</summary>
public sealed record ChartCandle(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public sealed record ChartPoint(long Time, decimal Value);

public sealed record ChartMarker(long Time, string Side, decimal Price, string Reason, decimal? Result);

/// <summary>Leitura do último candle fechado, com os mesmos indicadores que as estratégias usam.</summary>
public sealed record ChartReading(
    decimal Last,
    decimal ChangePercent,
    decimal? Adx14,
    decimal? Atr14,
    int? AtrPercentile,
    decimal? Rsi14,
    decimal? VolumeRatio,
    decimal? Sma20,
    decimal? Sma50,
    decimal? Vwap,
    string Trend,
    string TrendDetail);

public sealed record ChartData(
    string Symbol,
    string Contract,
    Timeframe Timeframe,
    string Source,
    IReadOnlyList<ChartCandle> Candles,
    IReadOnlyList<ChartPoint> Sma20,
    IReadOnlyList<ChartPoint> Sma50,
    IReadOnlyList<ChartPoint> Vwap,
    IReadOnlyList<ChartMarker> Markers,
    ChartReading? Reading);

public sealed class ChartService(
    IMarketDataFeed feed,
    IAssetRepository assets,
    ICandleRepository candles,
    IPaperRepository paper,
    IExecutionSettingsRepository execution,
    TimeProvider clock)
{
    public async Task<ChartData> GetAsync(string symbol, Timeframe timeframe, int count, CancellationToken cancellationToken)
    {
        count = Math.Clamp(count, 50, 1000);
        var asset = await assets.FindAsync(symbol, cancellationToken) ?? throw new DomainException($"Ativo {symbol} não cadastrado.");

        IReadOnlyList<Candle> source;
        string contract = asset.Symbol, origin;
        var status = await feed.GetStatusAsync(cancellationToken);
        if (status.Connected)
        {
            contract = await feed.ResolveSymbolAsync(asset, cancellationToken);
            source = await feed.GetBarsAsync(asset, contract, timeframe, count, cancellationToken);
            origin = "MetaTrader 5";
        }
        else if (timeframe == Timeframe.D1)
        {
            var now = clock.GetUtcNow();
            source = (await candles.ListAsync(asset.Symbol, Timeframe.D1, now.AddDays(-count * 1.6), now, cancellationToken)).TakeLast(count).ToList();
            origin = "histórico diário gravado";
        }
        else
        {
            throw new DomainException("Barras intradiárias só com o MetaTrader 5 conectado. Escolha o diário (D1) para ver o histórico gravado.");
        }

        var bars = Bar.FromCandles(source);
        var times = bars.Select(ChartTime).ToList();
        var chartCandles = bars.Select((b, i) => new ChartCandle(times[i], b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
        var sma20 = new List<ChartPoint>();
        var sma50 = new List<ChartPoint>();
        var vwap = new List<ChartPoint>();
        var atrs = new List<decimal>();
        for (var i = 0; i < bars.Count; i++)
        {
            var h = new History(bars, i);
            if (h.Has(20))
            {
                sma20.Add(new ChartPoint(times[i], Math.Round(Indicators.Sma(h, 20), 4)));
            }

            if (h.Has(50))
            {
                sma50.Add(new ChartPoint(times[i], Math.Round(Indicators.Sma(h, 50), 4)));
            }

            if (timeframe != Timeframe.D1 && Indicators.SessionVwap(h) is { } v)
            {
                vwap.Add(new ChartPoint(times[i], Math.Round(v, 4)));
            }

            if (h.Has(15))
            {
                atrs.Add(Indicators.Atr(h, 14));
            }
        }

        var mode = (await execution.GetAsync(cancellationToken)).Mode;
        var first = bars.Count > 0 ? (bars[0].Time ?? new DateTimeOffset(bars[0].Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)) : DateTimeOffset.MaxValue;
        var markers = (await paper.ListRecentFillsAsync(mode, 200, cancellationToken))
            .Where(f => (f.Symbol == contract || f.Symbol == asset.Symbol) && f.Time >= first)
            .OrderBy(f => f.Time)
            .Select(f => new ChartMarker(ChartTime(f.Time), f.Side == OrderSide.Buy ? "buy" : "sell", f.Price, f.Reason, f.RealizedProfit))
            .ToList();

        return new ChartData(asset.Symbol, contract, timeframe, origin, chartCandles, sma20, sma50, vwap, markers, Read(bars, atrs, sma20, sma50, vwap));
    }

    private static ChartReading? Read(IReadOnlyList<Bar> bars, List<decimal> atrs, List<ChartPoint> sma20, List<ChartPoint> sma50, List<ChartPoint> vwap)
    {
        if (bars.Count < 2)
        {
            return null;
        }

        var h = new History(bars, bars.Count - 1);
        var last = h.Current.Close;

        // Variação do dia: contra o último fechamento do pregão anterior (no diário, o candle anterior).
        var previous = h[1].Close;
        for (var i = 1; i < h.Count; i++)
        {
            if (h[i].Date < h.Current.Date)
            {
                previous = h[i].Close;
                break;
            }
        }
        decimal? adx = h.Has(29) ? Math.Round(Indicators.Adx(h, 14), 1) : null;
        decimal? atr = h.Has(15) ? Math.Round(Indicators.Atr(h, 14), 2) : null;
        decimal? rsi = h.Has(15) ? Math.Round(Indicators.Rsi(h, 14), 1) : null;
        int? atrPercentile = atr is { } a && atrs.Count >= 20
            ? (int)Math.Round(100m * atrs.TakeLast(100).Count(x => x <= a) / Math.Min(100, atrs.Count))
            : null;
        var recentVolume = bars.TakeLast(21).Take(20).Select(b => (decimal)b.Volume).DefaultIfEmpty(0).Average();
        decimal? volumeRatio = recentVolume > 0 ? Math.Round(h.Current.Volume / recentVolume, 2) : null;
        decimal? s20 = sma20.Count > 0 ? sma20[^1].Value : null;
        decimal? s50 = sma50.Count > 0 ? sma50[^1].Value : null;

        string trend, detail;
        if (s20 is null || s50 is null)
        {
            (trend, detail) = ("indefinida", "Histórico curto para as médias de 20 e 50.");
        }
        else if (last > s20 && s20 > s50)
        {
            (trend, detail) = ("alta", "Preço acima da média de 20, que está acima da de 50.");
        }
        else if (last < s20 && s20 < s50)
        {
            (trend, detail) = ("baixa", "Preço abaixo da média de 20, que está abaixo da de 50.");
        }
        else
        {
            (trend, detail) = ("lateral", "Médias e preço sem alinhamento.");
        }

        if (adx is { } force && trend is "alta" or "baixa")
        {
            detail += force >= 25 ? $" ADX {force:0.#}: tendência com força." : $" ADX {force:0.#}: tendência fraca.";
        }

        return new ChartReading(last, previous == 0 ? 0 : Math.Round((last / previous - 1m) * 100m, 2), adx, atr, atrPercentile, rsi, volumeRatio,
            s20, s50, vwap.Count > 0 ? vwap[^1].Value : null, trend, detail);
    }

    private static long ChartTime(Bar bar) => bar.Time is { } t
        ? ChartTime(t)
        : new DateTimeOffset(bar.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

    private static long ChartTime(DateTimeOffset time)
    {
        var local = B3Calendar.ToLocal(time);
        return new DateTimeOffset(local.DateTime, TimeSpan.Zero).ToUnixTimeSeconds();
    }
}

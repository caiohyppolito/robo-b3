using System.Net.Http.Json;
using System.Text.Json;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.MarketData.Mt5;

/// <summary>
/// Cliente do conector local do MetaTrader 5 (mt5-gateway, em Python).
/// O conector só tem rotas de leitura: por construção, nenhuma ordem pode sair daqui.
/// </summary>
internal sealed class Mt5GatewayFeed(HttpClient http) : IMarketDataFeed
{
    internal const string TokenHeader = "X-Gateway-Token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FeedStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = await http.GetFromJsonAsync<HealthDto>("health", Json, cancellationToken)
                ?? throw new InvalidOperationException("Resposta vazia do conector.");
            return new FeedStatus(health.Connected, health.Mode, health.Server, health.Login, health.IsDemo, health.Company, health.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new FeedStatus(false, "indisponível", null, null, null, null,
                $"Conector do MT5 não respondeu em {http.BaseAddress} ({ex.Message}). Ele está rodando?");
        }
    }

    public async Task<string> ResolveSymbolAsync(Asset asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!asset.IsFuture)
        {
            return asset.Symbol;
        }

        var resolved = await http.GetFromJsonAsync<SymbolDto>($"symbols/{Uri.EscapeDataString(asset.Symbol)}/resolve", Json, cancellationToken);
        return resolved?.Symbol ?? throw new InvalidOperationException($"Contrato vigente de {asset.Symbol} não encontrado.");
    }

    public async Task<IReadOnlyList<Candle>> GetBarsAsync(Asset asset, string feedSymbol, Timeframe timeframe, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var rates = await http.GetFromJsonAsync<RatesDto>(
            $"rates/{Uri.EscapeDataString(feedSymbol)}?timeframe={timeframe}&count={count}", Json, cancellationToken);
        return ToCandles(asset.Symbol, timeframe, rates);
    }

    public async Task<IReadOnlyList<Candle>> GetHistoryAsync(Asset asset, Timeframe timeframe, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var rates = await http.GetFromJsonAsync<RatesDto>(
            $"history/{Uri.EscapeDataString(asset.Symbol)}?timeframe={timeframe}&count={count}", Json, cancellationToken);
        return ToCandles(asset.Symbol, timeframe, rates);
    }

    public async Task<Quote> GetQuoteAsync(string feedSymbol, CancellationToken cancellationToken)
    {
        var tick = await http.GetFromJsonAsync<TickDto>($"tick/{Uri.EscapeDataString(feedSymbol)}", Json, cancellationToken)
            ?? throw new InvalidOperationException($"Sem cotação para {feedSymbol}.");
        return new Quote(tick.Symbol, tick.Bid, tick.Ask, tick.Last > 0 ? tick.Last : (tick.Bid + tick.Ask) / 2, tick.Time);
    }

    /// <summary>
    /// Barras do MT5 viram candles do ativo do cadastro (ex.: WINZ26 → WIN).
    /// Candles diários usam a mesma chave do Yahoo: meia-noite UTC da data do pregão.
    /// Barras inválidas (preço zero, máxima menor que mínima) são descartadas.
    /// </summary>
    internal static IReadOnlyList<Candle> ToCandles(string assetSymbol, Timeframe timeframe, RatesDto? rates)
    {
        if (rates?.Bars is null)
        {
            return [];
        }

        var candles = new List<Candle>(rates.Bars.Count);
        foreach (var b in rates.Bars)
        {
            if (b.Open <= 0 || b.High <= 0 || b.Low <= 0 || b.Close <= 0 || b.High < b.Low)
            {
                continue;
            }

            var openTime = timeframe == Timeframe.D1
                ? new DateTimeOffset(B3Calendar.TradingDate(b.Time).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : b.Time;
            candles.Add(new Candle(assetSymbol, timeframe, openTime, b.Open, Math.Max(b.High, Math.Max(b.Open, b.Close)),
                Math.Min(b.Low, Math.Min(b.Open, b.Close)), b.Close, b.Volume));
        }

        return candles;
    }

    internal sealed record HealthDto(bool Connected, string Mode, string? Server, long? Login, bool? IsDemo, string? Company, string? Message);

    internal sealed record SymbolDto(string Symbol);

    internal sealed record BarDto(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    internal sealed record RatesDto(string Symbol, IReadOnlyList<BarDto> Bars);

    internal sealed record TickDto(string Symbol, decimal Bid, decimal Ask, decimal Last, DateTimeOffset Time);
}

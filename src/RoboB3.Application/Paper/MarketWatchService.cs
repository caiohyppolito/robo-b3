using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.Paper;

public sealed record WatchItem(
    string Symbol,
    string Name,
    string Contract,
    AssetClass AssetClass,
    decimal? Last,
    decimal? ChangePercent,
    DateTimeOffset? QuoteTime,
    bool IsFresh,
    IReadOnlyList<decimal> Spark,
    string? Error);

public sealed record MarketWatch(bool Connected, string? Message, IReadOnlyList<WatchItem> Items);

/// <summary>Cotações para o painel: mini índice, mini dólar, Ibovespa e os ativos das alocações.</summary>
public sealed class MarketWatchService(
    IMarketDataFeed feed,
    IAssetRepository assets,
    IStrategyAssignmentRepository assignments,
    IOptions<PaperTradingOptions> options,
    TimeProvider clock)
{
    private static readonly string[] Always = ["WIN", "WDO", "IBOV"];

    public async Task<MarketWatch> GetAsync(CancellationToken cancellationToken)
    {
        var status = await feed.GetStatusAsync(cancellationToken);
        if (!status.Connected)
        {
            return new MarketWatch(false, status.Message, []);
        }

        var all = (await assets.ListAsync(onlyActive: false, cancellationToken)).ToDictionary(a => a.Symbol);
        var symbols = Always
            .Concat((await assignments.ListAsync(cancellationToken)).Select(a => a.Symbol))
            .Distinct()
            .Where(all.ContainsKey);

        var items = new List<WatchItem>();
        foreach (var symbol in symbols)
        {
            items.Add(await ReadAsync(all[symbol], cancellationToken));
        }

        return new MarketWatch(true, null, items);
    }

    private async Task<WatchItem> ReadAsync(Asset asset, CancellationToken cancellationToken)
    {
        var contract = asset.Symbol;
        try
        {
            contract = await feed.ResolveSymbolAsync(asset, cancellationToken);
            var quote = await feed.GetQuoteAsync(contract, cancellationToken);
            var daily = await feed.GetBarsAsync(asset, contract, Timeframe.D1, 2, cancellationToken);
            var intraday = await feed.GetBarsAsync(asset, contract, Timeframe.M5, 120, cancellationToken);

            // Mini gráfico: barras de 5 minutos do último pregão com negócios.
            var lastDay = intraday.Count == 0 ? (DateOnly?)null : B3Calendar.TradingDate(intraday.Max(c => c.OpenTime));
            var spark = intraday.Where(c => B3Calendar.TradingDate(c.OpenTime) == lastDay).OrderBy(c => c.OpenTime).Select(c => c.Close).ToList();

            var today = B3Calendar.TradingDate(quote.Time);
            var previous = daily.OrderBy(c => c.OpenTime).LastOrDefault(c => DateOnly.FromDateTime(c.OpenTime.UtcDateTime) < today);
            decimal? change = previous is { Close: > 0 } && quote.Last > 0 ? Math.Round(((quote.Last / previous.Close) - 1m) * 100m, 2) : null;

            var fresh = quote.IsFresh(clock.GetUtcNow(), TimeSpan.FromSeconds(options.Value.MaxQuoteAgeSeconds));
            return new WatchItem(asset.Symbol, asset.Name, contract, asset.AssetClass, quote.Last, change, quote.Time, fresh, spark, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new WatchItem(asset.Symbol, asset.Name, contract, asset.AssetClass, null, null, null, false, [], ex.Message);
        }
    }
}

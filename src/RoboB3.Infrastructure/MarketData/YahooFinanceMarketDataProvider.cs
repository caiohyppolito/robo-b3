using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.MarketData;

/// <summary>
/// Dados diários gratuitos via Yahoo Finance (ações, ETFs e Ibovespa).
/// Futuros (WIN/WDO) e dados intradiários virão do MT5 da corretora.
/// </summary>
internal sealed class YahooFinanceMarketDataProvider(HttpClient http) : IMarketDataProvider
{
    public async Task<IReadOnlyList<Candle>> GetDailyCandlesAsync(
        Asset asset,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.DailyDataSymbol is null)
        {
            return [];
        }

        var period1 = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var url = $"v8/finance/chart/{Uri.EscapeDataString(asset.DailyDataSymbol)}?period1={period1}&period2={period2}&interval=1d";

        using var response = await http.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return YahooChartParser.ParseDaily(asset.Symbol, json);
    }
}

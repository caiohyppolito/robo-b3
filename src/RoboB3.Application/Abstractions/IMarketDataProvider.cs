using RoboB3.Domain.MarketData;

namespace RoboB3.Application.Abstractions;

/// <summary>Fonte externa de dados de mercado (Yahoo, MT5...).</summary>
public interface IMarketDataProvider
{
    Task<IReadOnlyList<Candle>> GetDailyCandlesAsync(
        Asset asset,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

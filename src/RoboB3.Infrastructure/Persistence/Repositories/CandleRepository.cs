using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class CandleRepository(RoboB3DbContext db) : ICandleRepository
{
    public async Task<DateTimeOffset?> GetLastOpenTimeAsync(
        string symbol,
        Timeframe timeframe,
        CancellationToken cancellationToken) =>
        await db.Candles
            .Where(c => c.Symbol == symbol && c.Timeframe == timeframe)
            .MaxAsync(c => (DateTimeOffset?)c.OpenTime, cancellationToken);

    public async Task<IReadOnlyList<Candle>> ListAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        await db.Candles
            .AsNoTracking()
            .Where(c => c.Symbol == symbol && c.Timeframe == timeframe && c.OpenTime >= from && c.OpenTime <= to)
            .OrderBy(c => c.OpenTime)
            .ToListAsync(cancellationToken);

    public async Task<int> UpsertAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (candles.Count == 0)
        {
            return 0;
        }

        var saved = 0;
        foreach (var group in candles.GroupBy(c => (c.Symbol, c.Timeframe)))
        {
            var from = group.Min(c => c.OpenTime);
            var to = group.Max(c => c.OpenTime);
            var existing = await db.Candles
                .Where(c => c.Symbol == group.Key.Symbol
                    && c.Timeframe == group.Key.Timeframe
                    && c.OpenTime >= from
                    && c.OpenTime <= to)
                .ToDictionaryAsync(c => c.OpenTime, cancellationToken);

            foreach (var candle in group.DistinctBy(c => c.OpenTime))
            {
                if (existing.TryGetValue(candle.OpenTime, out var current))
                {
                    current.UpdateFrom(candle);
                }
                else
                {
                    db.Candles.Add(candle);
                }

                saved++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return saved;
    }
}

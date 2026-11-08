using Microsoft.EntityFrameworkCore;
using RoboB3.Application.MarketData;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class CdiRepository(RoboB3DbContext db) : ICdiRepository
{
    public async Task<DateOnly?> GetLatestDateAsync(CancellationToken cancellationToken) =>
        await db.CdiRates.AnyAsync(cancellationToken) ? await db.CdiRates.MaxAsync(r => r.Date, cancellationToken) : null;

    public Task<CdiRate?> GetLatestAsync(CancellationToken cancellationToken) =>
        db.CdiRates.AsNoTracking().OrderByDescending(r => r.Date).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CdiRate>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.CdiRates.AsNoTracking().Where(r => r.Date >= from && r.Date <= to).OrderBy(r => r.Date).ToListAsync(cancellationToken);

    public async Task<int> AddMissingAsync(IReadOnlyList<CdiRate> rates, CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return 0;
        }

        var from = rates.Min(r => r.Date);
        var existing = (await db.CdiRates.Where(r => r.Date >= from).Select(r => r.Date).ToListAsync(cancellationToken)).ToHashSet();
        var added = rates.Where(r => existing.Add(r.Date)).ToList();
        db.CdiRates.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added.Count;
    }
}

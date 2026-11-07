using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Learning;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class MetaModelRepository(RoboB3DbContext db) : IMetaModelRepository
{
    public void Add(MetaModel model) => db.MetaModels.Add(model);

    public Task<MetaModel?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.MetaModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<MetaModel?> GetActiveAsync(string strategyKey, CancellationToken cancellationToken) =>
        db.MetaModels.FirstOrDefaultAsync(m => m.StrategyKey == strategyKey && m.Status == MetaModelStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<MetaModel>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await db.MetaModels
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync(cancellationToken);
}

internal sealed class TradeJournalRepository(RoboB3DbContext db) : ITradeJournalRepository
{
    public void Add(TradeJournalEntry entry) => db.TradeJournal.Add(entry);

    public async Task<IReadOnlyList<TradeJournalEntry>> ListByStrategyAsync(string strategyKey, CancellationToken cancellationToken) =>
        await db.TradeJournal
            .AsNoTracking()
            .Where(e => e.StrategyKey == strategyKey)
            .OrderBy(e => e.EntryDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TradeJournalEntry>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await db.TradeJournal
            .AsNoTracking()
            .OrderByDescending(e => e.ExitDate)
            .Take(Math.Clamp(count, 1, 500))
            .ToListAsync(cancellationToken);
}

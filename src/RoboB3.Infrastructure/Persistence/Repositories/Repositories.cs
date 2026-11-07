using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class AssetRepository(RoboB3DbContext db) : IAssetRepository
{
    public async Task<IReadOnlyList<Asset>> ListAsync(bool onlyActive, CancellationToken cancellationToken) =>
        await db.Assets
            .Where(a => !onlyActive || a.IsActive)
            .OrderBy(a => a.Symbol)
            .ToListAsync(cancellationToken);

    public Task<Asset?> FindAsync(string symbol, CancellationToken cancellationToken) =>
        db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol.ToUpperInvariant(), cancellationToken);
}

internal sealed class RiskSettingsRepository(RoboB3DbContext db) : IRiskSettingsRepository
{
    public async Task<RiskSettings> GetAsync(CancellationToken cancellationToken) =>
        await db.RiskSettings.SingleAsync(s => s.Id == RiskSettings.SingletonId, cancellationToken);
}

internal sealed class TradingStateRepository(RoboB3DbContext db) : ITradingStateRepository
{
    public async Task<TradingState> GetAsync(CancellationToken cancellationToken) =>
        await db.TradingStates.SingleAsync(s => s.Id == TradingState.SingletonId, cancellationToken);
}

internal sealed class AuditLog(RoboB3DbContext db) : IAuditLog
{
    public void Record(AuditEntry entry) => db.AuditEntries.Add(entry);

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await db.AuditEntries
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(count, 1, 500))
            .ToListAsync(cancellationToken);
}

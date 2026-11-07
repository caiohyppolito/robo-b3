using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Paper;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class PaperRepository(RoboB3DbContext db) : IPaperRepository
{
    public Task<PaperAccount?> GetAccountAsync(ExecutionMode mode, CancellationToken cancellationToken) =>
        db.PaperAccounts.FirstOrDefaultAsync(a => a.Id == (int)mode, cancellationToken);

    public void AddAccount(PaperAccount account) => db.PaperAccounts.Add(account);

    public Task<List<PaperPosition>> ListPositionsAsync(ExecutionMode mode, CancellationToken cancellationToken) =>
        db.PaperPositions.Where(p => p.Mode == mode).OrderBy(p => p.EntryTime).ToListAsync(cancellationToken);

    public void AddPosition(PaperPosition position) => db.PaperPositions.Add(position);

    public void RemovePosition(PaperPosition position) => db.PaperPositions.Remove(position);

    public void AddFill(PaperFill fill) => db.PaperFills.Add(fill);

    public async Task<IReadOnlyList<PaperFill>> ListRecentFillsAsync(ExecutionMode mode, int count, CancellationToken cancellationToken) =>
        await db.PaperFills.AsNoTracking().Where(f => f.Mode == mode).OrderByDescending(f => f.Time).Take(Math.Clamp(count, 1, 500)).ToListAsync(cancellationToken);

    public async Task ResetAsync(ExecutionMode mode, CancellationToken cancellationToken)
    {
        db.PaperFills.RemoveRange(await db.PaperFills.Where(f => f.Mode == mode).ToListAsync(cancellationToken));
        db.PaperPositions.RemoveRange(await db.PaperPositions.Where(p => p.Mode == mode).ToListAsync(cancellationToken));
        db.PaperAccounts.RemoveRange(await db.PaperAccounts.Where(a => a.Id == (int)mode).ToListAsync(cancellationToken));
    }
}

internal sealed class StrategyAssignmentRepository(RoboB3DbContext db) : IStrategyAssignmentRepository
{
    public async Task<IReadOnlyList<StrategyAssignment>> ListAsync(CancellationToken cancellationToken) =>
        await db.StrategyAssignments.OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);

    public Task<StrategyAssignment?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.StrategyAssignments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(StrategyAssignment assignment) => db.StrategyAssignments.Add(assignment);

    public void Remove(StrategyAssignment assignment) => db.StrategyAssignments.Remove(assignment);
}

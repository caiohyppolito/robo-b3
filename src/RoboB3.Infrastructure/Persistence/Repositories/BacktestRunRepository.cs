using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Backtesting;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class BacktestRunRepository(RoboB3DbContext db) : IBacktestRunRepository
{
    public void Add(BacktestRun run) => db.BacktestRuns.Add(run);

    public Task<BacktestRun?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.BacktestRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BacktestRun>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await db.BacktestRuns
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync(cancellationToken);
}

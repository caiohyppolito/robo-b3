using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class OrderIntentRepository(RoboB3DbContext db) : IOrderIntentRepository
{
    public void Add(OrderIntent intent) => db.OrderIntents.Add(intent);

    public async Task<IReadOnlyList<OrderIntent>> ListOpenAsync(ExecutionMode mode, CancellationToken cancellationToken) =>
        await db.OrderIntents.Where(i => i.Mode == mode && i.Status == OrderIntentStatus.Pending).OrderBy(i => i.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrderIntent>> ListRecentAsync(ExecutionMode mode, int count, CancellationToken cancellationToken) =>
        await db.OrderIntents.AsNoTracking().Where(i => i.Mode == mode).OrderByDescending(i => i.CreatedAt).Take(Math.Clamp(count, 1, 500)).ToListAsync(cancellationToken);
}

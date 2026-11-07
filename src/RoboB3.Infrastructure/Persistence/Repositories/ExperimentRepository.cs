using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Backtesting;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class ExperimentRepository(RoboB3DbContext db) : IExperimentRepository
{
    public void Add(ResearchExperiment experiment) => db.ResearchExperiments.Add(experiment);

    public async Task<IReadOnlyList<ResearchExperiment>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        await db.ResearchExperiments.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(count, 1, 200)).ToListAsync(cancellationToken);
}

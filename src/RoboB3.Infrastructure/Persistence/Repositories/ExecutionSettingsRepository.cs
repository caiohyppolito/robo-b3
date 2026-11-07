using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;

namespace RoboB3.Infrastructure.Persistence.Repositories;

internal sealed class ExecutionSettingsRepository(RoboB3DbContext db, TimeProvider clock) : IExecutionSettingsRepository
{
    /// <summary>Se o registro ainda não existe (workers antes da API), nasce no modo simulado, o mais seguro.</summary>
    public async Task<ExecutionSettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.ExecutionSettings.FirstOrDefaultAsync(s => s.Id == ExecutionSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new ExecutionSettings(clock.GetUtcNow());
            db.ExecutionSettings.Add(settings);
        }

        return settings;
    }
}

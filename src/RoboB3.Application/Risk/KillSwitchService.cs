using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Risk;

/// <summary>Trava e libera o robô, sempre registrando auditoria.</summary>
public sealed class KillSwitchService(
    ITradingStateRepository states,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public Task<TradingState> GetStateAsync(CancellationToken cancellationToken) => states.GetAsync(cancellationToken);

    public async Task<TradingState> HaltAsync(string reason, string actor, CancellationToken cancellationToken)
    {
        var state = await states.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        state.Halt(reason, actor, now);
        audit.Record(new AuditEntry("kill-switch.halt", actor, reason, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<TradingState> ResumeAsync(string actor, CancellationToken cancellationToken)
    {
        var state = await states.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        state.Resume(actor, now);
        audit.Record(new AuditEntry("kill-switch.resume", actor, null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return state;
    }
}

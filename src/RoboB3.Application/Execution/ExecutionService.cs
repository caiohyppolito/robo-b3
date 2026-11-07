using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Execution;

namespace RoboB3.Application.Execution;

public sealed record ExecutionStatus(ExecutionMode Mode, string ChangedBy, DateTimeOffset ChangedAt, bool AllowMt5, bool AllowRealAccount, FeedStatus Feed);

/// <summary>Consulta e troca do modo de execução, sempre auditada.</summary>
public sealed class ExecutionService(
    IExecutionSettingsRepository settings,
    ITradingStateRepository tradingState,
    IPaperRepository ledger,
    IMarketDataFeed feed,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IOptions<ExecutionOptions> options,
    TimeProvider clock)
{
    public async Task<ExecutionStatus> GetAsync(CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var status = await feed.GetStatusAsync(cancellationToken);
        return new ExecutionStatus(current.Mode, current.ChangedBy, current.ChangedAt, options.Value.AllowMt5, options.Value.AllowRealAccount, status);
    }

    public async Task<ExecutionStatus> ChangeModeAsync(ExecutionMode target, string? confirmation, string actor, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var status = await feed.GetStatusAsync(cancellationToken);
        var context = new ModeChangeContext(
            (await tradingState.GetAsync(cancellationToken)).IsHalted,
            (await ledger.ListPositionsAsync(current.Mode, cancellationToken)).Count,
            options.Value.AllowMt5,
            options.Value.AllowRealAccount,
            status.Connected,
            status.IsDemo,
            confirmation);

        ExecutionPolicy.EnsureCanChange(current.Mode, target, context);

        var now = clock.GetUtcNow();
        var previous = current.Mode;
        current.ChangeTo(target, actor, now);
        audit.Record(new AuditEntry("execucao.modo-alterado", actor,
            $"{previous.Describe()} -> {target.Describe()} (conta {status.Login}, {status.Server})", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ExecutionStatus(current.Mode, current.ChangedBy, current.ChangedAt, options.Value.AllowMt5, options.Value.AllowRealAccount, status);
    }
}

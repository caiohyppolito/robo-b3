using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;

namespace RoboB3.Application.Paper;

public sealed record AssignmentRequest(
    string Symbol,
    string StrategyKey,
    IReadOnlyDictionary<string, decimal>? Parameters,
    Timeframe Timeframe,
    bool UseMetaModel,
    bool FlattenAtDayEnd);

/// <summary>Cadastro das estratégias que operam na conta simulada. Toda mudança fica na auditoria.</summary>
public sealed class StrategyAssignmentService(
    IStrategyAssignmentRepository assignments,
    IAssetRepository assets,
    IPaperRepository paper,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public Task<IReadOnlyList<StrategyAssignment>> ListAsync(CancellationToken cancellationToken) => assignments.ListAsync(cancellationToken);

    public async Task<StrategyAssignment> CreateAsync(AssignmentRequest request, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asset = await ValidateAsync(request, null, cancellationToken);
        var assignment = new StrategyAssignment(asset.Symbol, request.StrategyKey, request.Parameters ?? new Dictionary<string, decimal>(),
            request.Timeframe, request.UseMetaModel, request.FlattenAtDayEnd, clock.GetUtcNow());
        assignments.Add(assignment);
        Record("paper.alocacao-criada", actor, assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<StrategyAssignment> UpdateAsync(Guid id, AssignmentRequest request, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var assignment = await GetAsync(id, cancellationToken);
        if (!string.Equals(assignment.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Não é possível trocar o ativo de uma alocação; crie outra.");
        }

        await ValidateAsync(request, id, cancellationToken);
        assignment.Update(request.StrategyKey, request.Parameters ?? new Dictionary<string, decimal>(), request.Timeframe, request.UseMetaModel, request.FlattenAtDayEnd);
        Record("paper.alocacao-alterada", actor, assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<StrategyAssignment> SetEnabledAsync(Guid id, bool enabled, string actor, CancellationToken cancellationToken)
    {
        var assignment = await GetAsync(id, cancellationToken);
        if (enabled)
        {
            assignment.Enable();
        }
        else
        {
            assignment.Disable();
        }

        Record(enabled ? "paper.alocacao-ligada" : "paper.alocacao-desligada", actor, assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var assignment = await GetAsync(id, cancellationToken);
        if ((await paper.ListPositionsAsync(cancellationToken)).Any(p => p.AssignmentId == id))
        {
            throw new DomainException("Encerre a posição desta alocação antes de removê-la.");
        }

        assignments.Remove(assignment);
        Record("paper.alocacao-removida", actor, assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<StrategyAssignment> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await assignments.FindAsync(id, cancellationToken) ?? throw new DomainException("Alocação não encontrada.");

    private async Task<Asset> ValidateAsync(AssignmentRequest request, Guid? currentId, CancellationToken cancellationToken)
    {
        var asset = await assets.FindAsync(request.Symbol, cancellationToken)
            ?? throw new DomainException($"Ativo '{request.Symbol}' não cadastrado.");
        if (asset.AssetClass == AssetClass.Index)
        {
            throw new DomainException("Índices não são negociáveis; use BOVA11 ou o mini índice (WIN).");
        }

        if (request.FlattenAtDayEnd && request.Timeframe == Timeframe.D1)
        {
            throw new DomainException("Day trade precisa de tempo gráfico intradiário (M1, M5, M15 ou H1).");
        }

        var duplicate = (await assignments.ListAsync(cancellationToken)).Any(a => a.Id != currentId
            && a.Symbol == asset.Symbol && a.StrategyKey == request.StrategyKey && a.Timeframe == request.Timeframe);
        if (duplicate)
        {
            throw new DomainException("Já existe uma alocação igual (mesmo ativo, estratégia e tempo gráfico).");
        }

        return asset;
    }

    private void Record(string action, string actor, StrategyAssignment a) =>
        audit.Record(new AuditEntry(action, actor, $"{a.Symbol} {a.StrategyKey} {a.Timeframe} IA={a.UseMetaModel} daytrade={a.FlattenAtDayEnd}", clock.GetUtcNow()));
}

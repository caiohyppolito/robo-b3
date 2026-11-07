using RoboB3.Domain.Learning;

namespace RoboB3.Application.Abstractions;

/// <summary>Treina e carrega modelos de classificação (implementado com ML.NET).</summary>
public interface IMetaModelEngine
{
    byte[] Train(IReadOnlyList<LabeledExample> examples);

    IProbabilityModel Load(byte[] modelBytes);
}

public interface IMetaModelRepository
{
    void Add(MetaModel model);

    Task<MetaModel?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<MetaModel?> GetActiveAsync(string strategyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetaModel>> ListRecentAsync(int count, CancellationToken cancellationToken);
}

public interface ITradeJournalRepository
{
    void Add(TradeJournalEntry entry);

    Task<IReadOnlyList<TradeJournalEntry>> ListByStrategyAsync(string strategyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeJournalEntry>> ListRecentAsync(int count, CancellationToken cancellationToken);
}

/// <summary>Analista em linguagem natural (Claude). Opcional: só funciona com chave configurada.</summary>
public interface IModelAnalyst
{
    bool IsConfigured { get; }

    Task<string> AnalyzeAsync(ModelAnalysisInput input, CancellationToken cancellationToken);
}

public sealed record ModelAnalysisInput(
    MetaModel Model,
    string StrategyName,
    string StrategyDescription,
    IReadOnlyList<LabeledExample> WorstValidationTrades);

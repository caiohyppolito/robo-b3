using RoboB3.Domain.Execution;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;

namespace RoboB3.Application.Abstractions;

/// <summary>Estado da conexão com o MetaTrader 5 (via conector local).</summary>
public sealed record FeedStatus(
    bool Connected,
    string Mode,
    string? Server,
    long? Login,
    bool? IsDemo,
    string? Company,
    string? Message);

/// <summary>Dados de mercado em tempo real e intradiários. Só leitura: não existe envio de ordens aqui.</summary>
public interface IMarketDataFeed
{
    Task<FeedStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Símbolo negociável na corretora: o próprio código para ações; o contrato vigente para futuros (ex.: WIN → WINZ26).</summary>
    Task<string> ResolveSymbolAsync(Asset asset, CancellationToken cancellationToken);

    /// <summary>Últimas barras do símbolo, incluindo a barra ainda em formação (a última).</summary>
    Task<IReadOnlyList<Candle>> GetBarsAsync(Asset asset, string feedSymbol, Timeframe timeframe, int count, CancellationToken cancellationToken);

    /// <summary>Histórico longo para backtest; em futuros, a série contínua quando a corretora oferece.</summary>
    Task<IReadOnlyList<Candle>> GetHistoryAsync(Asset asset, Timeframe timeframe, int count, CancellationToken cancellationToken);

    Task<Quote> GetQuoteAsync(string feedSymbol, CancellationToken cancellationToken);
}

/// <summary>Livro do robô (conta, posições e execuções), separado por modo de execução.</summary>
public interface IPaperRepository
{
    Task<PaperAccount?> GetAccountAsync(ExecutionMode mode, CancellationToken cancellationToken);

    void AddAccount(PaperAccount account);

    Task<List<PaperPosition>> ListPositionsAsync(ExecutionMode mode, CancellationToken cancellationToken);

    void AddPosition(PaperPosition position);

    void RemovePosition(PaperPosition position);

    void AddFill(PaperFill fill);

    Task<IReadOnlyList<PaperFill>> ListRecentFillsAsync(ExecutionMode mode, int count, CancellationToken cancellationToken);

    void AddDecision(TradingDecision decision);

    Task<IReadOnlyList<TradingDecision>> ListRecentDecisionsAsync(ExecutionMode mode, int count, CancellationToken cancellationToken);

    /// <summary>Variáveis registradas nas decisões recentes da estratégia, só de alocações em barras diárias (todos os modos).</summary>
    /// <summary>Entradas vetadas pela IA em alocações de barras diárias (todos os modos), mais recentes primeiro.</summary>
    Task<IReadOnlyList<TradingDecision>> ListRecentVetoesAsync(int count, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<float>>> ListRecentDailyFeaturesAsync(string strategyKey, int count, CancellationToken cancellationToken);

    /// <summary>Apaga conta, posições e execuções do modo (o diário da IA e o registro de decisões são mantidos).</summary>
    Task ResetAsync(ExecutionMode mode, CancellationToken cancellationToken);
}

public interface IStrategyAssignmentRepository
{
    Task<IReadOnlyList<StrategyAssignment>> ListAsync(CancellationToken cancellationToken);

    Task<StrategyAssignment?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(StrategyAssignment assignment);

    void Remove(StrategyAssignment assignment);
}

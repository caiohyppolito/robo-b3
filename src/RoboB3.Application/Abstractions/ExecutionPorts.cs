using RoboB3.Domain.Execution;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Abstractions;

/// <summary>Ordem a mercado. Para encerrar, informa o ticket da posição na corretora.</summary>
public sealed record OrderInstruction(
    string Symbol,
    OrderSide Side,
    int Quantity,
    decimal ReferencePrice,
    decimal? StopLoss,
    long? PositionTicket,
    string ClientOrderId,
    string Reason);

public sealed record OrderResult(bool Filled, decimal Price, int Quantity, long? BrokerTicket, string Message);

public sealed record BrokerPosition(long Ticket, string Symbol, OrderSide Side, int Volume, decimal Price, decimal? StopLoss, bool IsRobot);

/// <summary>Executa ordens no modo vigente: simulação interna ou MetaTrader 5.</summary>
public interface IOrderExecutor
{
    ExecutionMode Mode { get; }

    Task<OrderResult> SendAsync(OrderInstruction instruction, CancellationToken cancellationToken);

    Task<bool> UpdateStopAsync(string symbol, long ticket, decimal stop, CancellationToken cancellationToken);

    /// <summary>Posições na corretora, para conciliação; nulo na simulação.</summary>
    Task<IReadOnlyList<BrokerPosition>?> GetPositionsAsync(CancellationToken cancellationToken);

    /// <summary>Preço de saída de uma posição encerrada pela corretora (ex.: stop).</summary>
    Task<decimal?> GetClosingPriceAsync(long ticket, CancellationToken cancellationToken);

    /// <summary>Patrimônio da conta na corretora, para abrir o livro do modo; nulo na simulação.</summary>
    Task<decimal?> GetEquityAsync(CancellationToken cancellationToken);
}

public interface IOrderExecutorFactory
{
    IOrderExecutor For(ExecutionMode mode);
}

public interface IExecutionSettingsRepository
{
    Task<ExecutionSettings> GetAsync(CancellationToken cancellationToken);
}

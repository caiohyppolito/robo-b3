using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Execution;

/// <summary>Execução simulada: preenche na hora, ao preço de referência piorado pelo slippage. Nada sai daqui.</summary>
public sealed class SimulatedOrderExecutor(IOptions<BacktestOptions> options) : IOrderExecutor
{
    public ExecutionMode Mode => ExecutionMode.Paper;

    public Task<OrderResult> SendAsync(OrderInstruction instruction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var price = options.Value.Costs().Fill(instruction.ReferencePrice, instruction.Side == OrderSide.Buy);
        return Task.FromResult(new OrderResult(true, price, instruction.Quantity, null, "Executada (simulação)."));
    }

    public Task<bool> UpdateStopAsync(string symbol, long ticket, decimal stop, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<BrokerPosition>?> GetPositionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BrokerPosition>?>(null);

    public Task<decimal?> GetClosingPriceAsync(long ticket, CancellationToken cancellationToken) => Task.FromResult<decimal?>(null);

    public Task<decimal?> GetEquityAsync(CancellationToken cancellationToken) => Task.FromResult<decimal?>(null);
}

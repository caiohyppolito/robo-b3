using RoboB3.Application.Abstractions;
using RoboB3.Application.Execution;
using RoboB3.Domain.Execution;

namespace RoboB3.Infrastructure.Execution;

internal sealed class OrderExecutorFactory(SimulatedOrderExecutor simulated, IHttpClientFactory httpClients) : IOrderExecutorFactory
{
    public const string HttpClientName = "mt5-orders";

    public IOrderExecutor For(ExecutionMode mode) => mode switch
    {
        ExecutionMode.Paper => simulated,
        ExecutionMode.Mt5Demo or ExecutionMode.Mt5Real => new Mt5OrderExecutor(httpClients.CreateClient(HttpClientName), mode),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo de execução desconhecido."),
    };
}

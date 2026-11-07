using RoboB3.Domain.Execution;

namespace RoboB3.Application.Abstractions;

/// <summary>Garante um único ciclo de operação por vez, entre processos (worker e API).</summary>
public interface ITradingLock
{
    /// <returns>A trava (liberada no Dispose), ou nulo se outro ciclo a detém após a espera.</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken);
}

public interface IOrderIntentRepository
{
    void Add(OrderIntent intent);

    Task<IReadOnlyList<OrderIntent>> ListOpenAsync(ExecutionMode mode, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderIntent>> ListRecentAsync(ExecutionMode mode, int count, CancellationToken cancellationToken);
}

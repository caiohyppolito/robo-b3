using RoboB3.Domain.Risk;

namespace RoboB3.Application.Abstractions;

/// <summary>Fornece a carteira atual (simulada ou vinda da corretora).</summary>
public interface IPortfolioProvider
{
    Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

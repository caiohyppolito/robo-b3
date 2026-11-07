using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Tests;

internal sealed class InMemoryAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry) => Entries.Add(entry);

    public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Entries.TakeLast(count).ToList());
}

internal sealed class FakeTradingStateRepository(TradingState state) : ITradingStateRepository
{
    public Task<TradingState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(state);
}

internal sealed class FakeRiskSettingsRepository(RiskSettings settings) : IRiskSettingsRepository
{
    public Task<RiskSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
}

internal sealed class FakePortfolioProvider(PortfolioSnapshot snapshot) : IPortfolioProvider
{
    public Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
}

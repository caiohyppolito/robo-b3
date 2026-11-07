using RoboB3.Domain.Audit;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Abstractions;

public interface IAssetRepository
{
    Task<IReadOnlyList<Asset>> ListAsync(bool onlyActive, CancellationToken cancellationToken);

    Task<Asset?> FindAsync(string symbol, CancellationToken cancellationToken);
}

public interface ICandleRepository
{
    Task<DateTimeOffset?> GetLastOpenTimeAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken);

    Task<IReadOnlyList<Candle>> ListAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>Insere candles novos e atualiza os existentes com a mesma chave. Retorna quantos foram gravados.</summary>
    Task<int> UpsertAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken);
}

public interface IRiskSettingsRepository
{
    Task<RiskSettings> GetAsync(CancellationToken cancellationToken);
}

public interface ITradingStateRepository
{
    Task<TradingState> GetAsync(CancellationToken cancellationToken);
}

public interface IAuditLog
{
    void Record(AuditEntry entry);

    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int count, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

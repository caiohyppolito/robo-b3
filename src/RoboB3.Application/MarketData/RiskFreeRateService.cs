using Microsoft.Extensions.Logging;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.MarketData;

/// <summary>Fonte do CDI diário (Banco Central, série 12 do SGS).</summary>
public interface ICdiProvider
{
    Task<IReadOnlyList<CdiRate>> GetDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface ICdiRepository
{
    Task<DateOnly?> GetLatestDateAsync(CancellationToken cancellationToken);

    Task<CdiRate?> GetLatestAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CdiRate>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Grava os dias que ainda não existem e devolve quantos entraram.</summary>
    Task<int> AddMissingAsync(IReadOnlyList<CdiRate> rates, CancellationToken cancellationToken);
}

/// <summary>Mantém a série do CDI atualizada e monta a curva usada no backtest e na IA.</summary>
public sealed partial class RiskFreeRateService(ICdiProvider provider, ICdiRepository repository, TimeProvider clock, ILogger<RiskFreeRateService> logger)
{
    public static readonly DateOnly SeriesStart = new(2000, 1, 3);

    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(B3Calendar.ToLocal(clock.GetUtcNow()).DateTime);
        var latest = await repository.GetLatestDateAsync(cancellationToken);
        var from = latest?.AddDays(1) ?? SeriesStart;
        if (from > today)
        {
            return 0;
        }

        var rates = await provider.GetDailyAsync(from, today, cancellationToken);
        var saved = await repository.AddMissingAsync(rates, cancellationToken);
        LogSynced(saved, from, today);
        return saved;
    }

    public Task<CdiRate?> GetLatestAsync(CancellationToken cancellationToken) => repository.GetLatestAsync(cancellationToken);

    /// <summary>Curva do período, com a taxa fixa como reserva; nula se a série ainda não foi baixada.</summary>
    public async Task<RiskFreeCurve?> GetCurveAsync(DateOnly from, DateOnly to, decimal fallbackAnnualPercent, CancellationToken cancellationToken)
    {
        // Pega alguns dias antes para cobrir um início em feriado.
        var rates = await repository.ListAsync(from.AddDays(-10), to, cancellationToken);
        return rates.Count == 0
            ? null
            : new RiskFreeCurve(rates.Select(r => new KeyValuePair<DateOnly, decimal>(r.Date, r.DailyPercent)), fallbackAnnualPercent);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CDI: {Saved} dias gravados ({From} a {To})")]
    private partial void LogSynced(int saved, DateOnly from, DateOnly to);
}

using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;

namespace RoboB3.Application.Backtesting;

/// <summary>Roda backtests com dados do banco e os limites de risco vigentes.</summary>
public sealed class BacktestService(
    IAssetRepository assets,
    ICandleRepository candles,
    IRiskSettingsRepository riskSettings,
    IBacktestRunRepository runs,
    IUnitOfWork unitOfWork,
    IOptions<BacktestOptions> options,
    TimeProvider clock)
{
    /// <summary>Histórico extra antes do início, para aquecer indicadores longos (ex.: média de 200).</summary>
    public const int WarmupCalendarDays = 450;

    public async Task<BacktestRun> RunAsync(BacktestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = StrategyCatalog.Get(request.StrategyKey);
        var parameters = definition.Normalize(request.Parameters);
        var (from, to) = ResolvePeriod(request.From, request.To);

        var (asset, bars) = await LoadBarsAsync(request.Symbol, from, to, cancellationToken);
        var startIndex = FirstIndexOnOrAfter(bars, from);
        var settings = await BuildSettingsAsync(request.Settings, cancellationToken);

        var result = BacktestEngine.Run(bars, definition.Create(parameters), parameters, InstrumentSpec.For(asset), settings, startIndex);

        var run = new BacktestRun(result, settings, clock.GetUtcNow());
        runs.Add(run);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return run;
    }

    internal async Task<(Asset Asset, IReadOnlyList<Bar> Bars)> LoadBarsAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var asset = await assets.FindAsync(symbol, cancellationToken)
            ?? throw new DomainException($"Ativo '{symbol}' não encontrado.");

        var start = new DateTimeOffset(from.AddDays(-WarmupCalendarDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var data = await candles.ListAsync(asset.Symbol, Timeframe.D1, start, end, cancellationToken);
        if (data.Count == 0)
        {
            throw new DomainException($"Sem candles diários de {asset.Symbol}. Aguarde a coleta de dados.");
        }

        return (asset, Bar.FromCandles(data));
    }

    internal async Task<BacktestSettings> BuildSettingsAsync(SimulationSettings simulation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var current = (await riskSettings.GetAsync(cancellationToken)).Limits;

        // O backtest herda os limites vigentes; só a exposição por ativo pode ser ajustada,
        // porque num teste de um ativo só faz sentido usar mais do que a fatia de uma carteira.
        var limits = new RiskLimits(
            current.MaxDailyLossPercent,
            current.MaxDrawdownPercent,
            simulation.MaxPositionPercent,
            current.MaxOpenPositions,
            current.MaxFuturesContracts);

        var defaults = options.Value;
        return new BacktestSettings(
            simulation.InitialCapital,
            simulation.RiskPerTradePercent,
            limits,
            defaults.Costs(),
            simulation.RiskFreeRatePercent ?? defaults.RiskFreeRatePercent,
            simulation.ApplyTaxes);
    }

    internal (DateOnly From, DateOnly To) ResolvePeriod(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var end = to ?? today;
        var start = from ?? end.AddYears(-3);
        if (start >= end)
        {
            throw new DomainException("A data inicial deve ser anterior à final.");
        }

        return (start, end);
    }

    internal static int FirstIndexOnOrAfter(IReadOnlyList<Bar> bars, DateOnly date)
    {
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i].Date >= date)
            {
                return i;
            }
        }

        throw new DomainException($"Não há pregões a partir de {date:dd/MM/yyyy}.");
    }
}

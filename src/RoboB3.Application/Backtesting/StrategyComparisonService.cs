using System.Collections.Concurrent;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Strategies;

namespace RoboB3.Application.Backtesting;

/// <summary>
/// Compara as estratégias de forma honesta: escolhe os parâmetros só com a parte inicial do histórico
/// (dentro da amostra) e mede o resultado na parte final, que a otimização nunca viu (fora da amostra).
/// Assim evitamos premiar o ajuste excessivo ao passado.
/// </summary>
public sealed class StrategyComparisonService(
    IAssetRepository assets,
    BacktestService backtests,
    IExperimentRepository experiments,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Mínimo de operações dentro da amostra para um conjunto de parâmetros ser considerado.</summary>
    public const int MinInSampleTrades = 3;

    public async Task<ComparisonResult> CompareAsync(ComparisonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.InSamplePercent is < 30 or > 90)
        {
            throw new DomainException("A parte dentro da amostra deve ficar entre 30% e 90%.");
        }

        var (from, to) = backtests.ResolvePeriod(request.From, request.To);
        var settings = await backtests.BuildSettingsAsync(request.Settings, cancellationToken);
        var symbols = request.Symbols is { Count: > 0 }
            ? request.Symbols
            : (await assets.ListAsync(onlyActive: true, cancellationToken))
                .Where(a => a.DailyDataSymbol is not null && a.AssetClass is AssetClass.Stock or AssetClass.Etf)
                .Select(a => a.Symbol)
                .ToList();

        var series = new List<(InstrumentSpec Instrument, IReadOnlyList<Bar> Bars, int Start, int Split)>();
        var skipped = new List<string>();
        foreach (var symbol in symbols)
        {
            try
            {
                var (asset, bars) = await backtests.LoadBarsAsync(symbol, from, to, cancellationToken);
                var start = BacktestService.FirstIndexOnOrAfter(bars, from);
                var split = start + (int)((bars.Count - start) * request.InSamplePercent / 100m);
                series.Add((InstrumentSpec.For(asset), bars, start, split));
            }
            catch (DomainException ex)
            {
                skipped.Add($"{symbol}: {ex.Message}");
            }
        }

        if (series.Count == 0)
        {
            throw new DomainException("Nenhum ativo com dados suficientes para comparar.");
        }

        var rows = new ConcurrentBag<ComparisonRow>();
        var errors = new ConcurrentBag<string>();
        var work = series.SelectMany(s => StrategyCatalog.All.Select(d => (Series: s, Definition: d))).ToList();

        // Cálculo puro e independente por ativo/estratégia: paraleliza com segurança.
        Parallel.ForEach(work, new ParallelOptions { CancellationToken = cancellationToken }, item =>
        {
            try
            {
                rows.Add(Evaluate(item.Series.Instrument, item.Series.Bars, item.Series.Start, item.Series.Split, item.Definition, settings));
            }
            catch (DomainException ex)
            {
                errors.Add($"{item.Series.Instrument.Symbol}/{item.Definition.Key}: {ex.Message}");
            }
        });

        skipped.AddRange(errors.Order());
        var ordered = rows.OrderBy(r => r.Symbol).ThenBy(r => r.StrategyKey).ToList();
        var reference = series[0];
        var ranking = Rank(ordered);

        // Cada combinação da grade em cada ativo, mais a avaliação fora da amostra: tudo conta como tentativa.
        var trials = series.Count * StrategyCatalog.All.Sum(d => d.Grid().Count() + 1);
        var experiment = new ResearchExperiment(
            "comparacao-estrategias",
            $"{series.Count} ativos, {reference.Bars[reference.Start].Date:dd/MM/yyyy} a {reference.Bars[^1].Date:dd/MM/yyyy}, {request.InSamplePercent}% dentro da amostra",
            trials,
            System.Text.Json.JsonSerializer.Serialize(ranking),
            CodeVersion.Current,
            clock.GetUtcNow());
        experiments.Add(experiment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ComparisonResult(
            reference.Bars[reference.Start].Date,
            reference.Bars[reference.Split].Date,
            reference.Bars[^1].Date,
            ranking,
            ordered,
            skipped,
            trials,
            experiment.Id);
    }

    private static ComparisonRow Evaluate(
        InstrumentSpec instrument,
        IReadOnlyList<Bar> bars,
        int start,
        int split,
        StrategyDefinition definition,
        BacktestSettings settings)
    {
        var inSampleBars = bars.Take(split + 1).ToList();
        var minTrades = definition.Key == StrategyKeys.BuyAndHold ? 1 : MinInSampleTrades;

        BacktestResult? bestInSample = null;
        foreach (var parameters in definition.Grid())
        {
            var result = BacktestEngine.Run(inSampleBars, definition.Create(parameters), parameters, instrument, settings, start);
            if (result.Metrics.Trades >= minTrades
                && (bestInSample is null || result.Metrics.Sharpe > bestInSample.Metrics.Sharpe))
            {
                bestInSample = result;
            }
        }

        var chosen = bestInSample?.Parameters ?? definition.DefaultParameters();
        var outOfSample = BacktestEngine.Run(bars, definition.Create(chosen), chosen, instrument, settings, split);
        var m = outOfSample.Metrics;

        return new ComparisonRow(
            instrument.Symbol,
            definition.Key,
            chosen.Values,
            bestInSample?.Metrics.Sharpe ?? 0m,
            bestInSample?.Metrics.TotalReturnPercent ?? 0m,
            m.Sharpe,
            m.TotalReturnPercent,
            m.MaxDrawdownPercent,
            m.Trades,
            m.BenchmarkReturnPercent,
            m.RiskFreeReturnPercent);
    }

    private static List<StrategyRanking> Rank(IReadOnlyList<ComparisonRow> rows) =>
        rows.GroupBy(r => r.StrategyKey)
            .Select(g => new StrategyRanking(
                g.Key,
                StrategyCatalog.Get(g.Key).Name,
                g.Count(),
                Math.Round(g.Average(r => r.OutOfSampleSharpe), 2),
                Math.Round(g.Average(r => r.OutOfSampleReturnPercent), 2),
                g.Max(r => r.OutOfSampleMaxDrawdownPercent),
                Math.Round(100m * g.Count(r => r.OutOfSampleReturnPercent > r.OutOfSampleBenchmarkReturnPercent) / g.Count(), 0),
                Math.Round(100m * g.Count(r => r.OutOfSampleReturnPercent > r.OutOfSampleRiskFreeReturnPercent) / g.Count(), 0)))
            .OrderByDescending(r => r.AverageOutOfSampleSharpe)
            .ToList();
}

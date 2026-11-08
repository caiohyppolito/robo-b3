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
/// Com várias janelas (walk-forward), a parte final é dividida em períodos sucessivos e cada um é avaliado
/// com parâmetros escolhidos usando tudo o que veio antes dele: são várias avaliações independentes em vez
/// de uma só, que poderia ter caído num período atípico. Funciona em barras diárias e intradiárias (day trade).
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

        if (request.Folds is < 1 or > 12)
        {
            throw new DomainException("Use de 1 a 12 janelas fora da amostra.");
        }

        var intraday = request.Timeframe != Timeframe.D1;
        if (request.FlattenAtDayEnd && !intraday)
        {
            throw new DomainException("Day trade precisa de barras intradiárias (M1, M5, M15 ou H1).");
        }

        var (from, to) = backtests.ResolvePeriod(request.From, request.To);
        var settings = await backtests.BuildSettingsAsync(request.Settings, cancellationToken, request.FlattenAtDayEnd, (from, to));
        var symbols = request.Symbols is { Count: > 0 }
            ? request.Symbols
            : intraday
                ? ["WIN", "WDO"]
                : (await assets.ListAsync(onlyActive: true, cancellationToken))
                    .Where(a => a.DailyDataSymbol is not null && a.AssetClass is AssetClass.Stock or AssetClass.Etf)
                    .Select(a => a.Symbol)
                    .ToList();

        var series = new List<(InstrumentSpec Instrument, IReadOnlyList<Bar> Bars, int Start, int Split, int[] Folds)>();
        var skipped = new List<string>();
        foreach (var symbol in symbols)
        {
            try
            {
                var (asset, bars) = await backtests.LoadBarsAsync(symbol, from, to, cancellationToken, request.Timeframe);
                var start = BacktestService.FirstIndexOnOrAfter(bars, from);
                var split = DayStart(bars, start + (int)((bars.Count - start) * request.InSamplePercent / 100m));
                var folds = FoldStarts(bars, split, request.Folds);
                if (split >= bars.Count - 1)
                {
                    throw new DomainException("histórico curto demais para separar dentro e fora da amostra.");
                }

                series.Add((InstrumentSpec.For(asset), bars, start, split, folds));
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
                rows.Add(Evaluate(item.Series.Instrument, item.Series.Bars, item.Series.Start, item.Series.Folds, item.Definition, settings));
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
        var trials = series.Count * StrategyCatalog.All.Sum(d => d.Grid().Count() + 1) * request.Folds;
        var experiment = new ResearchExperiment(
            "comparacao-estrategias",
            $"{series.Count} ativos, {request.Timeframe}{(request.FlattenAtDayEnd ? " day trade" : "")}, {reference.Bars[reference.Start].Date:dd/MM/yyyy} a {reference.Bars[^1].Date:dd/MM/yyyy}, {request.InSamplePercent}% dentro da amostra, {request.Folds} janela(s)",
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

    /// <summary>Índice da primeira barra do pregão que contém (ou segue) o índice dado: os cortes nunca partem um pregão.</summary>
    internal static int DayStart(IReadOnlyList<Bar> bars, int index)
    {
        index = Math.Clamp(index, 0, bars.Count - 1);
        while (index > 0 && index < bars.Count && bars[index].Date == bars[index - 1].Date)
        {
            index++;
        }

        return Math.Min(index, bars.Count - 1);
    }

    /// <summary>Início de cada janela fora da amostra (a última termina no fim da série).</summary>
    internal static int[] FoldStarts(IReadOnlyList<Bar> bars, int split, int folds)
    {
        var length = bars.Count - split;
        return Enumerable.Range(0, folds)
            .Select(f => f == 0 ? split : DayStart(bars, split + (length * f / folds)))
            .Distinct()
            .Where(i => i < bars.Count - 1)
            .ToArray();
    }

    private static ComparisonRow Evaluate(
        InstrumentSpec instrument,
        IReadOnlyList<Bar> bars,
        int start,
        int[] foldStarts,
        StrategyDefinition definition,
        BacktestSettings settings)
    {
        var minTrades = definition.Key == StrategyKeys.BuyAndHold ? 1 : MinInSampleTrades;
        var folds = new List<FoldResult>();
        var oosTrades = new List<BacktestTrade>();
        BacktestResult? lastInSample = null;
        StrategyParameters? lastChosen = null;
        decimal worstDrawdown = 0;

        for (var f = 0; f < foldStarts.Length; f++)
        {
            var foldStart = foldStarts[f];
            var foldEnd = f + 1 < foldStarts.Length ? foldStarts[f + 1] - 1 : bars.Count - 1;

            // Dentro da amostra: tudo antes da janela (âncora no início do período).
            var inSampleBars = bars.Take(foldStart).ToList();
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
            var window = bars.Take(foldEnd + 1).ToList();
            var outOfSample = BacktestEngine.Run(window, definition.Create(chosen), chosen, instrument, settings, foldStart);
            var m = outOfSample.Metrics;
            folds.Add(new FoldResult(bars[foldStart].Date, bars[foldEnd].Date, chosen.Values, m.Sharpe, m.TotalReturnPercent,
                m.BenchmarkReturnPercent, m.RiskFreeReturnPercent, m.Trades));
            oosTrades.AddRange(outOfSample.Trades);
            worstDrawdown = Math.Max(worstDrawdown, m.MaxDrawdownPercent);
            lastInSample = bestInSample;
            lastChosen = chosen;
        }

        static decimal Compound(IEnumerable<decimal> percents) =>
            Math.Round((percents.Aggregate(1m, (acc, p) => acc * (1m + (p / 100m))) - 1m) * 100m, 2);

        var nets = oosTrades.Select(t => (double)t.NetProfit).ToList();
        var mean = nets.Count > 0 ? nets.Average() : 0d;
        var sd = nets.Count > 1 ? Math.Sqrt(nets.Sum(x => (x - mean) * (x - mean)) / (nets.Count - 1)) : 0d;
        var margin = nets.Count > 1 ? 1.96 * sd / Math.Sqrt(nets.Count) : 0d;

        return new ComparisonRow(
            instrument.Symbol,
            definition.Key,
            (lastChosen ?? definition.DefaultParameters()).Values,
            lastInSample?.Metrics.Sharpe ?? 0m,
            lastInSample?.Metrics.TotalReturnPercent ?? 0m,
            Math.Round(folds.Average(x => x.Sharpe), 2),
            Compound(folds.Select(x => x.ReturnPercent)),
            worstDrawdown,
            folds.Sum(x => x.Trades),
            Compound(folds.Select(x => x.BenchmarkReturnPercent)),
            Compound(folds.Select(x => x.RiskFreeReturnPercent)),
            folds.Count,
            folds.Count(x => x.ReturnPercent > x.RiskFreeReturnPercent),
            folds.Count(x => x.ReturnPercent > x.BenchmarkReturnPercent),
            Math.Round((decimal)mean, 2),
            Math.Round((decimal)margin, 2),
            folds);
    }

    /// <summary>
    /// Ranking por Sharpe médio fora da amostra, só com os ativos em que a estratégia operou; estratégia que não
    /// operou em nenhum ativo vai para o fim (Sharpe zero de quem ficou parado não é mérito).
    /// </summary>
    private static List<StrategyRanking> Rank(IReadOnlyList<ComparisonRow> rows) =>
        rows.GroupBy(r => r.StrategyKey)
            .Select(g =>
            {
                var traded = g.Where(r => r.OutOfSampleTrades > 0).ToList();
                var basis = traded.Count > 0 ? traded : g.ToList();
                return new StrategyRanking(
                    g.Key,
                    StrategyCatalog.Get(g.Key).Name,
                    g.Count(),
                    Math.Round(basis.Average(r => r.OutOfSampleSharpe), 2),
                    Math.Round(basis.Average(r => r.OutOfSampleReturnPercent), 2),
                    g.Max(r => r.OutOfSampleMaxDrawdownPercent),
                    Math.Round(100m * basis.Count(r => r.OutOfSampleReturnPercent > r.OutOfSampleBenchmarkReturnPercent) / basis.Count, 0),
                    Math.Round(100m * basis.Count(r => r.OutOfSampleReturnPercent > r.OutOfSampleRiskFreeReturnPercent) / basis.Count, 0),
                    traded.Count,
                    Math.Round(100m * basis.Sum(r => r.FoldsBeatingRiskFree) / Math.Max(1, basis.Sum(r => r.Folds)), 0));
            })
            .OrderByDescending(r => r.AssetsWithTrades > 0)
            .ThenByDescending(r => r.AverageOutOfSampleSharpe)
            .ToList();
}

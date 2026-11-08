using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;

namespace RoboB3.Application.Learning;

/// <summary>
/// Ciclo de aprendizado de uma estratégia:
/// 1. gera operações históricas com o contexto de cada entrada (backtest de toda a grade de parâmetros) e soma o diário real;
/// 2. separa no tempo: treina com o passado e valida com o período seguinte, que o modelo nunca viu;
/// 3. mede a qualidade (AUC) e o efeito econômico (Sharpe com e sem filtro, e contra o modelo atual);
/// 4. promove o modelo só se houver evidência de melhora; aposenta o atual se ele deixou de ajudar.
/// </summary>
public sealed partial class LearningService(
    IAssetRepository assets,
    BacktestService backtests,
    IMetaModelRepository models,
    ITradeJournalRepository journal,
    IMetaModelEngine engine,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IOptions<BacktestOptions> backtestOptions,
    IOptions<LearningOptions> learningOptions,
    TimeProvider clock,
    ILogger<LearningService> logger)
{
    public const string Actor = "ia";
    private static readonly DateOnly HistoryStart = new(2000, 1, 1);

    public async Task<MetaModel> TrainAsync(string strategyKey, CancellationToken cancellationToken)
    {
        var definition = StrategyCatalog.Get(strategyKey);
        if (definition.Key == StrategyKeys.BuyAndHold)
        {
            throw new DomainException("Comprar e segurar não gera sinais para filtrar.");
        }

        var options = learningOptions.Value;
        var series = await LoadSeriesAsync(cancellationToken);
        var examples = await BuildExamplesAsync(definition, series, cancellationToken);
        var now = clock.GetUtcNow();

        if (examples.Count == 0)
        {
            return await SaveRejectedAsync(definition.Key, now, "Nenhuma operação com contexto suficiente (são necessários 201 pregões de histórico).", cancellationToken);
        }

        var splitDate = SplitDate(examples, options.ValidationPercent);

        // Purga: no treino só entram operações já encerradas antes da validação começar.
        var training = examples.Where(e => e.ExitDate < splitDate).ToList();
        var validation = examples.Where(e => e.EntryDate >= splitDate).ToList();
        var trainingFrom = examples.Min(e => e.EntryDate);
        var validationTo = examples.Max(e => e.ExitDate);

        if (training.Count < options.MinTrainingExamples)
        {
            return await SaveRejectedAsync(definition.Key, now,
                $"Dados insuficientes: {training.Count} operações para treino (mínimo {options.MinTrainingExamples}).", cancellationToken,
                trainingFrom, splitDate, validationTo, training.Count, validation.Count);
        }

        if (training.All(e => e.IsWin) || training.All(e => !e.IsWin))
        {
            return await SaveRejectedAsync(definition.Key, now,
                "Todas as operações de treino têm o mesmo resultado: não há o que aprender.", cancellationToken,
                trainingFrom, splitDate, validationTo, training.Count, validation.Count);
        }

        var bytes = engine.Train(training);
        var candidate = engine.Load(bytes);
        var probabilities = validation.Select(e => candidate.Predict(e.Features)).ToList();
        var report = ValidationReport.Calculate(validation, probabilities, options.Threshold);
        var importance = FeatureImportance(candidate, validation, report.Auc);
        var calibration = CalibrationReport.Calculate(validation.Select(e => e.IsWin).ToList(), probabilities);
        var baseline = FeatureBaseline.FromVectors(training.Select(e => e.Features).ToList());

        var champion = await models.GetActiveAsync(definition.Key, cancellationToken);
        var economics = await EconomicTestAsync(definition, series, splitDate, candidate, champion, cancellationToken);

        var policy = new PromotionPolicy(options.MinValidationExamples, options.MinAuc, options.MinSharpeImprovement, options.MinKeptPercent);
        var decision = policy.Decide(report, economics);

        if (decision.RetireChampion && champion is not null)
        {
            // Grava a aposentadoria antes: só pode haver um modelo ativo por estratégia (índice único).
            champion.Retire(decision.PromoteCandidate ? "substituído por um modelo melhor." : "deixou de superar operar sem filtro.", now);
            audit.Record(new AuditEntry("ia.modelo-aposentado", Actor, $"{definition.Key} {champion.Id}", now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var model = new MetaModel(definition.Key, now, trainingFrom, splitDate, validationTo, training.Count,
            options.Threshold, report, economics, importance, bytes);
        model.SetDiagnostics(calibration, baseline);
        models.Add(model);

        if (decision.PromoteCandidate)
        {
            model.Activate(decision.Reason, now);
            audit.Record(new AuditEntry("ia.modelo-promovido", Actor, $"{definition.Key} {model.Id}: {decision.Reason}", now));
        }
        else
        {
            model.Reject(decision.Reason);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogTrained(definition.Key, training.Count, validation.Count, report.Auc, decision.Reason);
        return model;
    }

    /// <summary>
    /// Pede ao analista (Claude) uma explicação em português do que o modelo aprendeu e de onde a estratégia mais erra.
    /// </summary>
    public async Task<MetaModel> AnalyzeAsync(Guid modelId, IModelAnalyst analyst, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analyst);
        if (!analyst.IsConfigured)
        {
            throw new DomainException("Análise indisponível: configure Anthropic:ApiKey com dotnet user-secrets (veja o README).");
        }

        var model = await models.FindAsync(modelId, cancellationToken)
            ?? throw new DomainException("Modelo não encontrado.");
        var definition = StrategyCatalog.Get(model.StrategyKey);

        var series = await LoadSeriesAsync(cancellationToken);
        var worst = (await BuildExamplesAsync(definition, series, cancellationToken))
            .Where(e => e.EntryDate >= model.SplitDate && e.EntryDate <= model.ValidationTo)
            .OrderBy(e => e.ReturnPercent)
            .Take(8)
            .ToList();

        var text = await analyst.AnalyzeAsync(new ModelAnalysisInput(model, definition.Name, definition.Description, worst), cancellationToken);
        model.SetAnalysis(text);
        audit.Record(new AuditEntry("ia.analise", Actor, $"{model.StrategyKey} {model.Id}", clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return model;
    }

    /// <summary>
    /// Compara as variáveis que o robô está vendo agora (registro de decisões, barras diárias) com as do treino do modelo.
    /// Só informa e registra na auditoria quando a mudança é forte; nunca troca nem desliga o modelo sozinho.
    /// </summary>
    public async Task<DriftReport> CheckDriftAsync(Guid modelId, IPaperRepository decisions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var model = await models.FindAsync(modelId, cancellationToken) ?? throw new DomainException("Modelo não encontrado.");
        if (model.Baseline is null)
        {
            return new DriftReport(0, DriftLevel.InsufficientData, 0, []);
        }

        var recent = (await decisions.ListRecentDailyFeaturesAsync(model.StrategyKey, 200, cancellationToken))
            .Select(f => new FeatureVector(f))
            .ToList();
        var report = DriftReport.Compute(model.Baseline, recent);
        if (report.Level == DriftLevel.Strong && model.Status == MetaModelStatus.Active)
        {
            var top = string.Join(", ", report.Features.Take(3).Select(f => $"{f.Name} {f.Psi:F2}"));
            audit.Record(new AuditEntry("ia.drift", Actor, $"{model.StrategyKey} {model.Id}: mudança forte de regime ({top}). Considere retreinar.", clock.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return report;
    }

    /// <summary>
    /// Volta para um modelo aposentado (rollback), por decisão humana. O modelo ativo da estratégia, se houver, é aposentado.
    /// </summary>
    public async Task<MetaModel> ReactivateAsync(Guid modelId, string actor, CancellationToken cancellationToken)
    {
        var model = await models.FindAsync(modelId, cancellationToken) ?? throw new DomainException("Modelo não encontrado.");
        if (model.Status != MetaModelStatus.Retired || model.ActivatedAt is null)
        {
            throw new DomainException("Só um modelo já promovido e depois aposentado pode ser reativado.");
        }

        var now = clock.GetUtcNow();
        var current = await models.GetActiveAsync(model.StrategyKey, cancellationToken);
        if (current is not null)
        {
            current.Retire($"substituído pela reativação de {model.Id} por {actor}.", now);
            audit.Record(new AuditEntry("ia.modelo-aposentado", actor, $"{current.StrategyKey} {current.Id}", now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        model.Reactivate(actor, now);
        audit.Record(new AuditEntry("ia.modelo-reativado", actor, $"{model.StrategyKey} {model.Id}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return model;
    }

    /// <summary>Filtro do modelo ativo da estratégia, ou nulo se não houver.</summary>
    public async Task<(MetaModel Model, ISignalFilter Filter)?> GetActiveFilterAsync(string strategyKey, CancellationToken cancellationToken)
    {
        var active = await models.GetActiveAsync(strategyKey, cancellationToken);
        return active?.ModelBytes is { } bytes
            ? (active, new ThresholdSignalFilter(engine.Load(bytes), active.Threshold))
            : null;
    }

    /// <summary>Registra no diário uma operação executada pelo robô (simulada ou real).</summary>
    public async Task RecordTradeAsync(TradeJournalEntry entry, CancellationToken cancellationToken)
    {
        journal.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Exemplos rotulados: operações de backtest de toda a grade de parâmetros + diário real com peso maior.</summary>
    internal async Task<List<LabeledExample>> BuildExamplesAsync(
        StrategyDefinition definition,
        IReadOnlyList<AssetSeries> series,
        CancellationToken cancellationToken)
    {
        // Para rotular sinais, os limites globais não devem interromper a geração de dados.
        var labelingSettings = new BacktestSettings(
            100_000m, 1m, new RiskLimits(100m, 100m, 100m, 5, 2), backtestOptions.Value.Costs(),
            backtestOptions.Value.RiskFreeRatePercent, applyTaxes: false);

        var examples = new Dictionary<(string, DateOnly, DateOnly, float), LabeledExample>();
        foreach (var s in series)
        {
            foreach (var parameters in definition.Grid())
            {
                BacktestResult result;
                try
                {
                    result = BacktestEngine.Run(s.Bars, definition.Create(parameters), parameters, s.Instrument, labelingSettings, captureFeatures: true);
                }
                catch (DomainException)
                {
                    continue;
                }

                foreach (var trade in result.Trades.Where(t => t.EntryFeatures is { Count: FeatureVector.Count }))
                {
                    var key = (s.Instrument.Symbol, trade.EntryDate, trade.ExitDate, trade.EntryFeatures![^1]);
                    examples.TryAdd(key, new LabeledExample(
                        s.Instrument.Symbol, trade.EntryDate, trade.ExitDate, new FeatureVector(trade.EntryFeatures),
                        trade.NetProfit > 0, trade.ReturnPercent));
                }
            }
        }

        var weight = learningOptions.Value.JournalWeight;
        var real = (await journal.ListByStrategyAsync(definition.Key, cancellationToken))
            .Select(j => j.ToExample(weight))
            .OfType<LabeledExample>();

        return examples.Values.Concat(real).OrderBy(e => e.EntryDate).ToList();
    }

    internal async Task<IReadOnlyList<AssetSeries>> LoadSeriesAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var tradable = (await assets.ListAsync(onlyActive: true, cancellationToken))
            .Where(a => a.DailyDataSymbol is not null && a.AssetClass is AssetClass.Stock or AssetClass.Etf);

        var series = new List<AssetSeries>();
        foreach (var asset in tradable)
        {
            try
            {
                var (_, bars) = await backtests.LoadBarsAsync(asset.Symbol, HistoryStart, today, cancellationToken);
                series.Add(new AssetSeries(InstrumentSpec.For(asset), bars));
            }
            catch (DomainException)
            {
                // Ativo ainda sem dados: fica de fora.
            }
        }

        return series;
    }

    private async Task<EconomicTest> EconomicTestAsync(
        StrategyDefinition definition,
        IReadOnlyList<AssetSeries> series,
        DateOnly splitDate,
        IProbabilityModel candidate,
        MetaModel? champion,
        CancellationToken cancellationToken)
    {
        var settings = await backtests.BuildSettingsAsync(new SimulationSettings(), cancellationToken, period: (HistoryStart, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
        var threshold = learningOptions.Value.Threshold;
        var parameters = definition.DefaultParameters();
        var candidateFilter = new ThresholdSignalFilter(candidate, threshold);
        ISignalFilter? championFilter = champion?.ModelBytes is { } bytes
            ? new ThresholdSignalFilter(engine.Load(bytes), champion.Threshold)
            : null;

        var unfiltered = new List<decimal>();
        var withCandidate = new List<decimal>();
        var withChampion = new List<decimal>();
        foreach (var s in series)
        {
            var start = s.Bars.ToList().FindIndex(b => b.Date >= splitDate);
            if (start < 0)
            {
                continue;
            }

            try
            {
                decimal Sharpe(ISignalFilter? filter) =>
                    BacktestEngine.Run(s.Bars, definition.Create(parameters), parameters, s.Instrument, settings, start, filter).Metrics.Sharpe;

                unfiltered.Add(Sharpe(null));
                withCandidate.Add(Sharpe(candidateFilter));
                if (championFilter is not null)
                {
                    withChampion.Add(Sharpe(championFilter));
                }
            }
            catch (DomainException)
            {
                // Período de validação curto demais neste ativo.
            }
        }

        static decimal Average(List<decimal> values) => values.Count == 0 ? 0 : Math.Round(values.Average(), 2);
        return new EconomicTest(Average(unfiltered), Average(withCandidate), championFilter is null ? null : Average(withChampion));
    }

    /// <summary>Queda da AUC ao embaralhar cada variável na validação (importância por permutação).</summary>
    private static Dictionary<string, double> FeatureImportance(IProbabilityModel model, IReadOnlyList<LabeledExample> validation, double baseAuc)
    {
        var labels = validation.Select(e => e.IsWin).ToList();
        var importance = new Dictionary<string, double>();
        for (var feature = 0; feature < FeatureVector.Count; feature++)
        {
            var random = new Random(feature + 1);
            var shuffled = validation.Select(e => e.Features.Values[feature]).OrderBy(_ => random.Next()).ToList();
            var scores = validation.Select((e, i) => model.Predict(e.Features.WithValue(feature, shuffled[i]))).ToList();
            importance[FeatureVector.Names[feature]] = Math.Round(baseAuc - ValidationReport.ComputeAuc(scores, labels), 4);
        }

        return importance;
    }

    private static DateOnly SplitDate(IReadOnlyList<LabeledExample> ordered, decimal validationPercent)
    {
        var index = (int)Math.Floor(ordered.Count * (1m - (validationPercent / 100m)));
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)].EntryDate;
    }

    private async Task<MetaModel> SaveRejectedAsync(
        string strategyKey,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? split = null,
        DateOnly? to = null,
        int training = 0,
        int validation = 0)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var empty = new ValidationReport(validation, 0.5, 0, 0, 0, 0, 0);
        var model = new MetaModel(strategyKey, now, from ?? today, split ?? today, to ?? today, training,
            learningOptions.Value.Threshold, empty, new EconomicTest(0, 0, null), new Dictionary<string, double>(), null);
        model.Reject(reason);
        models.Add(model);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogTrained(strategyKey, training, validation, 0.5, reason);
        return model;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "IA {Strategy}: treino {Training}, validação {Validation}, AUC {Auc:F3}. {Decision}")]
    private partial void LogTrained(string strategy, int training, int validation, double auc, string decision);
}

public sealed record AssetSeries(InstrumentSpec Instrument, IReadOnlyList<Bar> Bars);

using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

public class LearningTests
{
    private static readonly DateOnly Day = new(2026, 1, 5);
    private static readonly StrategyParameters NoParameters = new(new Dictionary<string, decimal>());

    private static List<Bar> Wave(int count) => TestBars.FromCloses(Enumerable.Range(0, count)
        .Select(i => 50m + (8m * (decimal)Math.Sin(i / 9d)) + (i * 0.02m))
        .ToArray());

    private sealed class FixedFilter(double probability, double threshold = 0.5) : ISignalFilter
    {
        public int Calls { get; private set; }

        public SignalAssessment Assess(FeatureVector features)
        {
            Calls++;
            return new SignalAssessment(probability, probability >= threshold);
        }
    }

    [Fact]
    public void Contexto_exige_201_pregoes()
    {
        var bars = Wave(250);

        Assert.Null(FeatureExtractor.TryExtract(new History(bars, 199), PositionDirection.Long));
        var features = FeatureExtractor.TryExtract(new History(bars, 200), PositionDirection.Long);

        Assert.NotNull(features);
        Assert.Equal(FeatureVector.Count, features.Values.Count);
        Assert.Equal(1f, features.Values[^1]);
        Assert.All(features.Values, v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void Contexto_nao_depende_de_barras_futuras()
    {
        var bars = Wave(260);
        var changedFuture = bars.ToList();
        for (var i = 221; i < changedFuture.Count; i++)
        {
            changedFuture[i] = changedFuture[i] with { Close = changedFuture[i].Close * 3m, High = changedFuture[i].High * 3m };
        }

        var original = FeatureExtractor.TryExtract(new History(bars, 220), PositionDirection.Short)!;
        var withOtherFuture = FeatureExtractor.TryExtract(new History(changedFuture, 220), PositionDirection.Short)!;

        Assert.Equal(original.Values, withOtherFuture.Values);
        Assert.Equal(-1f, original.Values[^1]);
    }

    [Theory]
    [InlineData(new[] { 0.9, 0.8, 0.2, 0.1 }, new[] { true, true, false, false }, 1.0)]
    [InlineData(new[] { 0.1, 0.2, 0.8, 0.9 }, new[] { true, true, false, false }, 0.0)]
    [InlineData(new[] { 0.5, 0.5, 0.5, 0.5 }, new[] { true, false, true, false }, 0.5)]
    public void Auc_mede_a_separacao_entre_acertos_e_erros(double[] scores, bool[] labels, double expected)
    {
        Assert.Equal(expected, ValidationReport.ComputeAuc(scores, labels), precision: 6);
    }

    [Fact]
    public void Relatorio_de_validacao_compara_com_e_sem_filtro()
    {
        var features = new FeatureVector(new float[FeatureVector.Count]);
        var examples = new[]
        {
            new LabeledExample("A", Day, Day, features, true, 4m),
            new LabeledExample("A", Day, Day, features, true, 2m),
            new LabeledExample("A", Day, Day, features, false, -3m),
            new LabeledExample("A", Day, Day, features, false, -1m),
        };

        var report = ValidationReport.Calculate(examples, [0.9, 0.4, 0.6, 0.1], threshold: 0.5);

        Assert.Equal(50m, report.BaseWinRatePercent);
        Assert.Equal(50m, report.FilteredWinRatePercent);
        Assert.Equal(0.5m, report.BaseAverageReturnPercent);
        Assert.Equal(0.5m, report.FilteredAverageReturnPercent);
        Assert.Equal(50m, report.KeptPercent);
        Assert.Equal(0.75, report.Auc);
    }

    [Fact]
    public void Politica_promove_so_com_evidencia_fora_da_amostra()
    {
        var policy = new PromotionPolicy();
        var good = new ValidationReport(40, 0.62, 45, 55, 0.2m, 0.8m, 60);

        Assert.True(policy.Decide(good, new EconomicTest(0.40m, 0.60m, null)).PromoteCandidate);
        Assert.False(policy.Decide(good, new EconomicTest(0.40m, 0.42m, null)).PromoteCandidate);
        Assert.False(policy.Decide(good with { Examples = 10 }, new EconomicTest(0.40m, 0.90m, null)).PromoteCandidate);
        Assert.False(policy.Decide(good with { Auc = 0.51 }, new EconomicTest(0.40m, 0.90m, null)).PromoteCandidate);
        Assert.False(policy.Decide(good with { KeptPercent = 10 }, new EconomicTest(0.40m, 0.90m, null)).PromoteCandidate);
    }

    [Fact]
    public void Politica_substitui_ou_aposenta_o_modelo_atual()
    {
        var policy = new PromotionPolicy();
        var good = new ValidationReport(40, 0.62, 45, 55, 0.2m, 0.8m, 60);

        var better = policy.Decide(good, new EconomicTest(0.40m, 0.70m, ChampionSharpe: 0.55m));
        Assert.True(better.PromoteCandidate);
        Assert.True(better.RetireChampion);

        var notBetter = policy.Decide(good, new EconomicTest(0.40m, 0.50m, ChampionSharpe: 0.55m));
        Assert.False(notBetter.PromoteCandidate);
        Assert.False(notBetter.RetireChampion);

        var championStale = policy.Decide(good with { Auc = 0.5 }, new EconomicTest(0.40m, 0.30m, ChampionSharpe: 0.35m));
        Assert.False(championStale.PromoteCandidate);
        Assert.True(championStale.RetireChampion);
    }

    [Fact]
    public void Filtro_que_veta_tudo_impede_entradas_e_conta_os_sinais()
    {
        var bars = Wave(400);
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);
        var parameters = definition.DefaultParameters();
        var settings = BacktestEngineTests.Settings();

        var unfiltered = BacktestEngine.Run(bars, definition.Create(parameters), parameters, BacktestEngineTests.Stock, settings);
        var veto = new FixedFilter(0.1);
        var filtered = BacktestEngine.Run(bars, definition.Create(parameters), parameters, BacktestEngineTests.Stock, settings, filter: veto);

        var tradesWithContext = unfiltered.Trades.Count(t => t.EntryDate >= bars[201].Date);
        Assert.True(tradesWithContext > 0);
        Assert.True(filtered.Metrics.SignalsFiltered > 0);
        Assert.True(filtered.Trades.Count < unfiltered.Trades.Count);
        Assert.All(filtered.Trades, t => Assert.True(t.EntryDate <= bars[202].Date));
    }

    [Fact]
    public void Filtro_que_aprova_registra_contexto_e_probabilidade()
    {
        var bars = Wave(400);
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);
        var parameters = definition.DefaultParameters();

        var result = BacktestEngine.Run(bars, definition.Create(parameters), parameters, BacktestEngineTests.Stock,
            BacktestEngineTests.Settings(), filter: new FixedFilter(0.8));

        var withContext = result.Trades.Where(t => t.EntryFeatures is not null).ToList();
        Assert.NotEmpty(withContext);
        Assert.All(withContext, t => Assert.Equal(0.8, t.EntryProbability));
        Assert.Equal(0, result.Metrics.SignalsFiltered);
    }

    [Fact]
    public void Filtro_nunca_impede_a_saida()
    {
        var bars = Wave(260);
        var veto = new FixedFilter(0.0);

        // Comprado até a barra 230, depois zerado: a saída precisa acontecer mesmo com o filtro vetando tudo.
        var strategy = new ScriptedStrategy((h, _) => h.Count <= 230 ? new StrategySignal(PositionDirection.Long) : StrategySignal.Flat);

        var result = BacktestEngine.Run(bars, strategy, NoParameters, BacktestEngineTests.Stock, BacktestEngineTests.Settings(), filter: veto);

        var trade = Assert.Single(result.Trades);
        Assert.Equal("sinal", trade.ExitReason);
        Assert.Equal(bars[231].Date, trade.ExitDate);
    }

    [Fact]
    public void Modelo_sem_conteudo_nao_pode_ser_ativado_e_so_ativo_e_aposentado()
    {
        var model = new MetaModel("sma-cross", DateTimeOffset.UnixEpoch, Day, Day, Day, 0, 0.5,
            new ValidationReport(0, 0.5, 0, 0, 0, 0, 0), new EconomicTest(0, 0, null), new Dictionary<string, double>(), null);

        Assert.Throws<DomainException>(() => model.Activate("teste", DateTimeOffset.UnixEpoch));
        Assert.Throws<DomainException>(() => model.Retire("teste", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Diario_vira_exemplo_de_treino_so_com_contexto_completo()
    {
        var features = new FeatureVector(Enumerable.Repeat(0.1f, FeatureVector.Count).ToArray());
        var complete = new TradeJournalEntry(TradeSource.Paper, "sma-cross", "petr4", PositionDirection.Long, Day, Day.AddDays(3), 120m, 1.5m, features, 0.7, DateTimeOffset.UnixEpoch);
        var withoutContext = new TradeJournalEntry(TradeSource.Live, "sma-cross", "VALE3", PositionDirection.Long, Day, Day, -50m, -0.5m, null, null, DateTimeOffset.UnixEpoch);

        var example = complete.ToExample(weight: 2f);

        Assert.NotNull(example);
        Assert.True(example.IsWin);
        Assert.Equal(2f, example.Weight);
        Assert.Equal("PETR4", example.Symbol);
        Assert.Null(withoutContext.ToExample(2f));
        Assert.Throws<DomainException>(() => new TradeJournalEntry(TradeSource.Paper, "x", "Y", PositionDirection.Long, Day, Day.AddDays(-1), 0, 0, null, null, DateTimeOffset.UnixEpoch));
    }
}

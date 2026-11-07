using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Learning;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class LearningIntegrationTests
{
    private static readonly DateOnly Day = new(2025, 1, 2);

    internal static LearningService Service(RoboB3DbContext db, IRiskSettingsRepository? risk = null) => new(
        new AssetRepository(db),
        BacktestIntegrationTests.Service(db, risk),
        new MetaModelRepository(db),
        new TradeJournalRepository(db),
        new MlNetMetaModelEngine(),
        new AuditLog(db),
        db,
        Options.Create(new BacktestOptions()),
        Options.Create(new LearningOptions()),
        BacktestIntegrationTests.Clock,
        NullLogger<LearningService>.Instance);

    private static RoboB3DbContext NewContext() =>
        new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Motor_aprende_um_padrao_plantado_e_sobrevive_a_serializacao()
    {
        var random = new Random(7);
        LabeledExample Make(int i)
        {
            var values = Enumerable.Range(0, FeatureVector.Count).Select(_ => (float)random.NextDouble()).ToArray();
            var isWin = values[5] + (0.2 * random.NextDouble()) > 0.6;
            return new LabeledExample("X", Day.AddDays(i), Day.AddDays(i), new FeatureVector(values), isWin, isWin ? 1m : -1m);
        }

        var training = Enumerable.Range(0, 600).Select(Make).ToList();
        var holdout = Enumerable.Range(600, 200).Select(Make).ToList();
        var engine = new MlNetMetaModelEngine();

        var model = engine.Load(engine.Train(training));
        var auc = ValidationReport.ComputeAuc(holdout.Select(e => model.Predict(e.Features)).ToList(), holdout.Select(e => e.IsWin).ToList());

        Assert.True(auc > 0.85, $"AUC {auc:F3}");
    }

    [Fact]
    public async Task Sem_dados_o_treino_e_rejeitado_com_motivo()
    {
        await using var db = NewContext();
        await new DatabaseInitializer(db, BacktestIntegrationTests.Clock, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync(false, CancellationToken.None);

        var model = await Service(db).TrainAsync(StrategyKeys.RsiMeanReversion, CancellationToken.None);

        Assert.Equal(MetaModelStatus.Rejected, model.Status);
        Assert.Contains("Nenhuma operação", model.Decision, StringComparison.Ordinal);
        Assert.Null(model.ModelBytes);
    }

    [Fact]
    public async Task Comprar_e_segurar_nao_tem_sinais_para_filtrar()
    {
        await using var db = NewContext();

        await Assert.ThrowsAsync<DomainException>(() => Service(db).TrainAsync(StrategyKeys.BuyAndHold, CancellationToken.None));
    }

    [Fact]
    public async Task Ciclo_completo_treina_valida_e_decide_mantendo_no_maximo_um_modelo_ativo()
    {
        await using var db = NewContext();
        await BacktestIntegrationTests.SeedAsync(db);
        var service = Service(db);

        var first = await service.TrainAsync(StrategyKeys.RsiMeanReversion, CancellationToken.None);
        var second = await service.TrainAsync(StrategyKeys.RsiMeanReversion, CancellationToken.None);

        foreach (var model in new[] { first, second })
        {
            Assert.NotEqual("Em avaliação.", model.Decision);
            Assert.True(model.SplitDate > model.TrainingFrom);
            Assert.True(model.ValidationTo >= model.SplitDate);
            if (model.ModelBytes is not null)
            {
                Assert.True(model.TrainingExamples >= new LearningOptions().MinTrainingExamples);
                Assert.Equal(FeatureVector.Count, model.FeatureImportance.Count);
            }
        }

        var active = await db.MetaModels.CountAsync(m => m.StrategyKey == StrategyKeys.RsiMeanReversion && m.Status == MetaModelStatus.Active);
        Assert.InRange(active, 0, 1);
        if (first.Status == MetaModelStatus.Active || second.Status == MetaModelStatus.Active)
        {
            Assert.Contains(await db.AuditEntries.ToListAsync(), a => a.Action == "ia.modelo-promovido");
        }
    }

    [Fact]
    public async Task Analise_sem_chave_explica_como_configurar()
    {
        await using var db = NewContext();
        var analyst = new ClaudeModelAnalyst(Options.Create(new AnthropicOptions()));

        Assert.False(analyst.IsConfigured);
        var ex = await Assert.ThrowsAsync<DomainException>(() => Service(db).AnalyzeAsync(Guid.NewGuid(), analyst, CancellationToken.None));
        Assert.Contains("Anthropic:ApiKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_do_analista_traz_numeros_importancias_e_piores_operacoes()
    {
        var model = new MetaModel("rsi-mean-reversion", DateTimeOffset.UnixEpoch, Day, Day.AddDays(300), Day.AddDays(400), 120, 0.5,
            new ValidationReport(40, 0.61, 48m, 57m, 0.3m, 0.9m, 55m), new EconomicTest(0.4m, 0.7m, null),
            new Dictionary<string, double> { ["rsi_14"] = 0.05, ["volume_relativo"] = 0.01 }, [1, 2, 3]);
        var worst = new LabeledExample("PETR4", Day.AddDays(310), Day.AddDays(315),
            new FeatureVector(Enumerable.Repeat(0.25f, FeatureVector.Count).ToArray()), false, -6.5m);

        var prompt = ClaudeModelAnalyst.BuildPrompt(new ModelAnalysisInput(model, "Reversão à média (RSI)", "Compra quedas.", [worst]));

        Assert.Contains("AUC: 0.610", prompt, StringComparison.Ordinal);
        Assert.Contains("48% sem filtro, 57% com filtro", prompt, StringComparison.Ordinal);
        Assert.Contains("- rsi_14: 0.0500", prompt, StringComparison.Ordinal);
        Assert.Contains("PETR4 2025-11-08 a 2025-11-13: -6.5%", prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("rsi_14", StringComparison.Ordinal) < prompt.IndexOf("volume_relativo", StringComparison.Ordinal));
    }
}

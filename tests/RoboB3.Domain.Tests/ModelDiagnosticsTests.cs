using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Strategies;

namespace RoboB3.Domain.Tests;

public class ModelDiagnosticsTests
{
    private static readonly DateOnly Day = new(2026, 1, 5);

    [Fact]
    public void Calibracao_perfeita_tem_brier_zero_e_supera_a_taxa_base()
    {
        var outcomes = new[] { true, false, true, false };
        var report = CalibrationReport.Calculate(outcomes, [1, 0, 1, 0]);

        Assert.Equal(0, report.Brier);
        Assert.Equal(0.25, report.BaseRateBrier);
        Assert.True(report.BeatsBaseRate);
        Assert.Equal(0, report.ExpectedCalibrationError);
    }

    [Fact]
    public void Modelo_confiante_demais_aparece_no_ece_e_nas_faixas()
    {
        // Diz 90% sempre, mas acerta metade.
        var outcomes = Enumerable.Range(0, 20).Select(i => i % 2 == 0).ToList();
        var report = CalibrationReport.Calculate(outcomes, Enumerable.Repeat(0.9, 20).ToList());

        Assert.False(report.BeatsBaseRate);
        Assert.Equal(0.4, report.ExpectedCalibrationError, 3);
        var top = report.Bins[^1];
        Assert.Equal(20, top.Count);
        Assert.Equal(0.5, top.ObservedWinRate);
        Assert.Equal(5, report.Bins.Count);
    }

    private static List<FeatureVector> Vectors(int count, Func<int, float> value) =>
        Enumerable.Range(0, count).Select(i => new FeatureVector(Enumerable.Repeat(value(i), FeatureVector.Count).ToArray())).ToList();

    [Fact]
    public void Mesma_distribuicao_e_estavel_e_distribuicao_deslocada_e_forte()
    {
        var training = Vectors(500, i => (i % 100) / 100f);
        var baseline = FeatureBaseline.FromVectors(training);

        var same = DriftReport.Compute(baseline, Vectors(100, i => (i % 100) / 100f));
        var shifted = DriftReport.Compute(baseline, Vectors(100, i => 0.95f + (i % 5 / 100f)));

        Assert.Equal(DriftLevel.Stable, same.Level);
        Assert.Equal(DriftLevel.Strong, shifted.Level);
        Assert.Equal(FeatureVector.Count, shifted.Features.Count);
        Assert.True(shifted.MaxPsi > DriftReport.StrongThreshold);
    }

    [Fact]
    public void Poucas_decisoes_recentes_nao_permitem_concluir_sobre_drift()
    {
        var baseline = FeatureBaseline.FromVectors(Vectors(200, i => i / 200f));

        var report = DriftReport.Compute(baseline, Vectors(5, _ => 0.99f));

        Assert.Equal(DriftLevel.InsufficientData, report.Level);
    }

    private static MetaModel Model() => new("sma-cross", DateTimeOffset.UnixEpoch, Day, Day, Day, 10, 0.5,
        new ValidationReport(20, 0.6, 50, 55, 0, 0, 60), new EconomicTest(0.2m, 0.4m, null), new Dictionary<string, double>(), [1, 2, 3]);

    [Fact]
    public void So_modelo_ja_promovido_e_aposentado_pode_ser_reativado()
    {
        var rejected = Model();
        rejected.Reject("AUC baixo");
        var retired = Model();
        retired.Activate("promovido", DateTimeOffset.UnixEpoch);
        retired.Retire("substituído", DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Throws<DomainException>(() => rejected.Reactivate("caio", DateTimeOffset.UnixEpoch));
        retired.Reactivate("caio", DateTimeOffset.UnixEpoch.AddDays(2));

        Assert.Equal(MetaModelStatus.Active, retired.Status);
        Assert.Null(retired.RetiredAt);
        Assert.Contains("Reativado por caio", retired.Decision, StringComparison.Ordinal);
    }

    [Fact]
    public void Filtro_da_ia_so_pode_ser_ligado_em_barras_diarias()
    {
        Assert.Throws<DomainException>(() =>
            new StrategyAssignment("WIN", StrategyKeys.SmaCross, new Dictionary<string, decimal>(), Timeframe.M5, true, false, DateTimeOffset.UnixEpoch));

        var daily = new StrategyAssignment("PETR4", StrategyKeys.SmaCross, new Dictionary<string, decimal>(), Timeframe.D1, true, false, DateTimeOffset.UnixEpoch);
        Assert.True(daily.UseMetaModel);
    }
}

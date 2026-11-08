using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class WalkForwardTests
{
    private static async Task<(RoboB3DbContext Db, StrategyComparisonService Comparison)> CreateAsync()
    {
        var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await BacktestIntegrationTests.SeedAsync(db);
        return (db, new StrategyComparisonService(new AssetRepository(db), BacktestIntegrationTests.Service(db), new ExperimentRepository(db), db, BacktestIntegrationTests.Clock));
    }

    [Fact]
    public async Task Tres_janelas_sucessivas_sem_sobreposicao_e_contagem_de_tentativas()
    {
        var (db, comparison) = await CreateAsync();
        await using var _ = db;

        var result = await comparison.CompareAsync(
            new ComparisonRequest(["PETR4", "VALE3"], new DateOnly(2024, 1, 2), new DateOnly(2026, 11, 6), new SimulationSettings(), 50m, Folds: 3),
            CancellationToken.None);

        var row = result.Rows.First(r => r.Symbol == "PETR4" && r.StrategyKey == StrategyKeys.SmaCross);
        Assert.Equal(3, row.Folds);
        var folds = row.FoldResults!;
        Assert.Equal(3, folds.Count);
        Assert.True(folds[0].To < folds[1].From && folds[1].To < folds[2].From);
        Assert.Equal(result.OutOfSampleFrom, folds[0].From);
        Assert.InRange(row.FoldsBeatingRiskFree, 0, 3);
        Assert.Equal(2 * StrategyCatalog.All.Sum(d => d.Grid().Count() + 1) * 3, result.TrialsEvaluated);

        // Retorno total é o composto das janelas.
        var compounded = (folds.Aggregate(1m, (acc, f) => acc * (1 + (f.ReturnPercent / 100m))) - 1) * 100m;
        Assert.Equal(Math.Round(compounded, 2), row.OutOfSampleReturnPercent);
    }

    [Fact]
    public async Task Win_em_5_minutos_com_day_trade_corta_as_janelas_no_inicio_do_pregao()
    {
        var (db, comparison) = await CreateAsync();
        await using var _ = db;
        var price = 130_000m;
        for (var day = 0; day < 40; day++)
        {
            var date = new DateOnly(2026, 8, 3).AddDays(day);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var b = 0; b < 90; b++)
            {
                var open = price;
                price += (decimal)(60 * Math.Sin((day * 90 + b) / 7.0));
                var time = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(-3)).AddMinutes(5 * b);
                db.Candles.Add(new Candle("WIN", Timeframe.M5, time, open, Math.Max(open, price) + 15, Math.Min(open, price) - 15, price, 1000));
            }
        }

        await db.SaveChangesAsync();

        var result = await comparison.CompareAsync(
            new ComparisonRequest(null, new DateOnly(2026, 8, 3), new DateOnly(2026, 9, 11), new SimulationSettings(), 50m, Timeframe.M5, FlattenAtDayEnd: true, Folds: 2),
            CancellationToken.None);

        Assert.Contains(result.Skipped, s => s.StartsWith("WDO", StringComparison.Ordinal));
        var rows = result.Rows.Where(r => r.Symbol == "WIN").ToList();
        Assert.Equal(StrategyCatalog.All.Count, rows.Count);
        var emaVwap = rows.Single(r => r.StrategyKey == StrategyKeys.EmaVwap);
        Assert.True(emaVwap.OutOfSampleTrades > 0, "a estratégia intradiária precisa operar em barras de 5 minutos");
        var folds = emaVwap.FoldResults!;
        Assert.Equal(2, folds.Count);
        Assert.True(folds[0].To < folds[1].From, "a segunda janela começa num pregão novo");
        Assert.Contains("M5 day trade", (await db.ResearchExperiments.SingleAsync()).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Day_trade_em_barras_diarias_e_janelas_demais_sao_recusados()
    {
        var (db, comparison) = await CreateAsync();
        await using var _ = db;

        await Assert.ThrowsAsync<DomainException>(() => comparison.CompareAsync(
            new ComparisonRequest(["PETR4"], null, null, new SimulationSettings(), FlattenAtDayEnd: true), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => comparison.CompareAsync(
            new ComparisonRequest(["PETR4"], null, null, new SimulationSettings(), Folds: 20), CancellationToken.None));
    }
}

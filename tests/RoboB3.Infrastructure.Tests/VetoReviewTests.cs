using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Execution;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class VetoReviewTests
{
    [Fact]
    public async Task Veto_e_avaliado_pela_operacao_que_a_estrategia_teria_feito()
    {
        await using var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await BacktestIntegrationTests.SeedAsync(db);

        // A operação que o motor faz nesse histórico: é ela que o veto teria impedido.
        var bars = Bar.FromCandles(await db.Candles.Where(c => c.Symbol == "PETR4").ToListAsync());
        var definition = StrategyCatalog.Get(StrategyKeys.SmaCross);
        var parameters = definition.DefaultParameters();
        var trades = BacktestEngine.Run(bars, definition.Create(parameters), parameters, InstrumentSpec.For(await db.Assets.SingleAsync(a => a.Symbol == "PETR4")),
            new BacktestSettings(100_000m, 1m, new RiskLimits(100m, 100m, 100m, 5, 2), new CostModel(0, 0, 0, 0), 0m, applyTaxes: false)).Trades;
        var trade = trades.First(t => t.ExitReason != "fim do período");
        var signalBar = bars[bars.ToList().FindIndex(b => b.Date == trade.EntryDate) - 1];

        var assignment = new StrategyAssignment("PETR4", StrategyKeys.SmaCross, new Dictionary<string, decimal>(), Timeframe.D1, true, false, DateTimeOffset.UnixEpoch);
        db.StrategyAssignments.Add(assignment);
        var veto = new TradingDecision(DateTimeOffset.UtcNow, ExecutionMode.Paper, assignment.Id, "PETR4", "PETR4", StrategyKeys.SmaCross,
            new DateTimeOffset(signalBar.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), trade.Direction);
        veto.SetContext(parameters.Values, null, "teste");
        veto.SetProbability(0.3);
        veto.Conclude(DecisionOutcome.Vetoed, "A IA vetou.");
        db.TradingDecisions.Add(veto);
        await db.SaveChangesAsync();

        var review = await new VetoReviewService(new PaperRepository(db), LearningIntegrationTests.Service(db), Options.Create(new BacktestOptions()))
            .ReviewAsync(50, CancellationToken.None);

        Assert.Equal(1, review.Vetoes);
        Assert.Equal(1, review.Evaluated);
        var item = Assert.Single(review.Items);
        Assert.Equal(trade.EntryDate, item.EntryDate);
        Assert.Equal(trade.ExitDate, item.ExitDate);
        Assert.Equal(trade.ReturnPercent > 0 ? "teria ganhado" : "teria perdido", item.Status);
    }

    [Fact]
    public async Task Sem_vetos_nao_ha_o_que_avaliar()
    {
        await using var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var review = await new VetoReviewService(new PaperRepository(db), LearningIntegrationTests.Service(db), Options.Create(new BacktestOptions()))
            .ReviewAsync(50, CancellationToken.None);

        Assert.Equal(0, review.Vetoes);
        Assert.Null(review.HitRatePercent);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class BacktestIntegrationTests
{
    internal static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 6, 22, 0, 0, TimeSpan.Zero));

    private static async Task<RoboB3DbContext> SeededContextAsync()
    {
        var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await SeedAsync(db);
        return db;
    }

    /// <summary>Cria o seed padrão e três anos de pregões sintéticos com tendência e ciclos (PETR4 e VALE3).</summary>
    internal static async Task SeedAsync(RoboB3DbContext db)
    {
        await new DatabaseInitializer(db, Clock, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(false, CancellationToken.None);

        var start = new DateTime(2023, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (symbol, phase) in new[] { ("PETR4", 0d), ("VALE3", 1.3d) })
        {
            var price = 30m;
            for (var day = 0; day < 1100; day++)
            {
                var date = start.AddDays(day);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var open = price;
                price = Math.Max(5m, price * (1m + (decimal)(0.0006 + (0.012 * Math.Sin((day / 23d) + phase)))));
                db.Candles.Add(new Candle(symbol, Timeframe.D1, new DateTimeOffset(date), open,
                    Math.Max(open, price) * 1.005m, Math.Min(open, price) * 0.995m, price, 1_000_000));
            }
        }

        await db.SaveChangesAsync();
    }

    internal static BacktestService Service(RoboB3DbContext db, IRiskSettingsRepository? risk = null) => new(
        new AssetRepository(db),
        new CandleRepository(db),
        risk ?? new FixedRiskSettings(),
        new BacktestRunRepository(db),
        db,
        Options.Create(new BacktestOptions()),
        Clock);

    [Fact]
    public async Task Backtest_sem_dados_explica_o_motivo()
    {
        await using var db = await SeededContextAsync();
        var request = new BacktestRequest("ITUB4", StrategyKeys.BuyAndHold, null, null, null, new SimulationSettings());

        var ex = await Assert.ThrowsAsync<DomainException>(() => Service(db).RunAsync(request, CancellationToken.None));

        Assert.Contains("Sem candles", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comparacao_avalia_todas_as_estrategias_fora_da_amostra()
    {
        await using var db = await SeededContextAsync();
        var comparison = new StrategyComparisonService(new AssetRepository(db), Service(db));
        var request = new ComparisonRequest(["PETR4", "VALE3", "ITUB4"], new DateOnly(2024, 6, 1), null, new SimulationSettings());

        var result = await comparison.CompareAsync(request, CancellationToken.None);

        Assert.Equal(StrategyCatalog.All.Count * 2, result.Rows.Count);
        Assert.Equal(StrategyCatalog.All.Count, result.Ranking.Count);
        Assert.Contains(result.Skipped, s => s.StartsWith("ITUB4", StringComparison.Ordinal));
        Assert.True(result.OutOfSampleFrom > result.InSampleFrom);
        Assert.True(result.Ranking.Zip(result.Ranking.Skip(1)).All(p => p.First.AverageOutOfSampleSharpe >= p.Second.AverageOutOfSampleSharpe));
    }
}

/// <summary>O provedor InMemory do EF não lê complex types; no SQL Server o repositório real é usado.</summary>
internal sealed class FixedRiskSettings : IRiskSettingsRepository
{
    public Task<RiskSettings> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RiskSettings(RiskLimits.Default, DateTimeOffset.UnixEpoch));
}

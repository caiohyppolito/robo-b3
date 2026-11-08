using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Domain.MarketData;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class PersistenceTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    private static RoboB3DbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedAsync(RoboB3DbContext db) =>
        await new DatabaseInitializer(db, new FakeTimeProvider(Day), NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync(applyMigrations: false, CancellationToken.None);

    [Fact]
    public async Task Inicializacao_cria_ativos_limites_e_robo_travado_de_forma_idempotente()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = NewContext(name))
        {
            await SeedAsync(db);
            await SeedAsync(db);
        }

        await using var check = NewContext(name);
        Assert.Equal(DatabaseInitializer.DefaultAssets.Count + DatabaseInitializer.UniverseAssets.Count, await check.Assets.CountAsync());
        Assert.Equal(DatabaseInitializer.DefaultAssets.Count, await check.Assets.CountAsync(a => a.IsActive));
        Assert.Equal(1, await check.RiskSettings.CountAsync());
        Assert.True((await check.TradingStates.SingleAsync()).IsHalted);
    }

    [Fact]
    public async Task Upsert_insere_novos_e_atualiza_existentes()
    {
        var name = Guid.NewGuid().ToString();
        await using (var db = NewContext(name))
        {
            await SeedAsync(db);
            var repo = new CandleRepository(db);
            await repo.UpsertAsync([new Candle("PETR4", Timeframe.D1, Day, 10m, 11m, 9m, 10m, 100)], CancellationToken.None);
        }

        await using (var db = NewContext(name))
        {
            var repo = new CandleRepository(db);
            var saved = await repo.UpsertAsync(
            [
                new Candle("PETR4", Timeframe.D1, Day, 10m, 12m, 9m, 11.5m, 200),
                new Candle("PETR4", Timeframe.D1, Day.AddDays(1), 11.5m, 12m, 11m, 11.8m, 300),
            ], CancellationToken.None);
            Assert.Equal(2, saved);
        }

        await using var check = NewContext(name);
        var candles = await new CandleRepository(check)
            .ListAsync("PETR4", Timeframe.D1, Day, Day.AddDays(1), CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.Equal(11.5m, candles[0].Close);
        Assert.Equal(Day.AddDays(1), await new CandleRepository(check).GetLastOpenTimeAsync("PETR4", Timeframe.D1, CancellationToken.None));
    }
}

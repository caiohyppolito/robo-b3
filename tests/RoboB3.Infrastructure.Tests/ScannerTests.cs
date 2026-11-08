using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.MarketData;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class ScannerTests
{
    private static async Task<(RoboB3DbContext Db, ScannerService Scanner)> CreateAsync()
    {
        var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await BacktestIntegrationTests.SeedAsync(db);
        var comparison = new StrategyComparisonService(new AssetRepository(db), BacktestIntegrationTests.Service(db), new ExperimentRepository(db), db, BacktestIntegrationTests.Clock);
        return (db, new ScannerService(new AssetRepository(db), new CandleRepository(db), comparison, BacktestIntegrationTests.Clock));
    }

    [Fact]
    public async Task Varredura_escolhe_a_estrategia_dentro_da_amostra_e_julga_fora_dela()
    {
        var (db, scanner) = await CreateAsync();
        await using var _ = db;

        var result = await scanner.ScanAsync(new ScanRequest(null, null, MinDailyVolumeMillions: 1m), CancellationToken.None);

        var petr = Assert.Single(result.Items, i => i.Symbol == "PETR4");
        Assert.True(petr.Liquid);
        Assert.Contains(petr.Verdict, new[] { "candidato", "observar", "descartar" });
        Assert.NotNull(petr.Strategy);
        Assert.NotEqual("buy-and-hold", petr.Strategy);
        Assert.True(result.TrialsEvaluated > 0);

        // Ativos ligados sem histórico aparecem como "sem dados", no fim da lista.
        Assert.Contains(result.Items, i => i.Symbol == "ITUB4" && i.Verdict == "sem dados");
        Assert.Equal("sem dados", result.Items[^1].Verdict);
    }

    [Fact]
    public async Task Ativo_com_pouco_volume_fica_de_fora_como_iliquido()
    {
        var (db, scanner) = await CreateAsync();
        await using var _ = db;

        var result = await scanner.ScanAsync(new ScanRequest(null, null, MinDailyVolumeMillions: 100_000m), CancellationToken.None);

        Assert.All(result.Items.Where(i => i.Symbol is "PETR4" or "VALE3"), i => Assert.Equal("ilíquido", i.Verdict));
        Assert.Equal(0, result.TrialsEvaluated);
    }

    [Fact]
    public async Task Universo_amplo_nasce_desligado_e_liga_de_uma_vez()
    {
        var (db, scanner) = await CreateAsync();
        await using var _ = db;
        Assert.False(await db.Assets.AnyAsync(a => a.Symbol == "PRIO3" && a.IsActive));

        var changed = await scanner.SetUniverseAsync(true, db, CancellationToken.None);

        Assert.Equal(AssetUniverse.Liquid.Count, changed);
        Assert.True(await db.Assets.AnyAsync(a => a.Symbol == "PRIO3" && a.IsActive));
        Assert.Equal(AssetUniverse.Liquid.Count, await scanner.SetUniverseAsync(false, db, CancellationToken.None));
    }
}

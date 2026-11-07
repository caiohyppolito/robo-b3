using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

/// <summary>
/// Roda só quando a variável ROBOB3_TEST_SQLSERVER aponta para um SQL Server (o CI sobe um container).
/// Ex.: Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "ROBOB3_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            Skip = $"Defina {EnvironmentVariable} para rodar os testes contra o SQL Server.";
        }
    }
}

/// <summary>Banco temporário com todas as migrações aplicadas; apagado ao final.</summary>
public sealed class SqlServerDatabase : IAsyncLifetime
{
    private string? _connectionString;

    public RoboB3DbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RoboB3DbContext>().UseSqlServer(_connectionString!).Options);

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        _connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"RoboB3_Test_{Guid.NewGuid():N}" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

public class SqlServerTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [SqlServerFact]
    public async Task Migracoes_criam_o_banco_e_o_seed_le_limites_e_estado()
    {
        await using var db = database.CreateContext();
        await BacktestIntegrationTests.SeedAsync(db);
        db.ChangeTracker.Clear();

        var limits = (await new RiskSettingsRepository(db).GetAsync(CancellationToken.None)).Limits;
        var state = await new TradingStateRepository(db).GetAsync(CancellationToken.None);

        Assert.Equal(Domain.Risk.RiskLimits.Default, limits);
        Assert.True(state.IsHalted);
    }

    [SqlServerFact]
    public async Task Backtest_e_gravado_e_lido_de_volta_com_series_completas()
    {
        await using var db = database.CreateContext();
        await BacktestIntegrationTests.SeedAsync(db);

        var service = BacktestIntegrationTests.Service(db, new RiskSettingsRepository(db));
        var request = new BacktestRequest("petr4", StrategyKeys.SmaCross, null, new DateOnly(2024, 6, 1), null, new SimulationSettings());

        var run = await service.RunAsync(request, CancellationToken.None);
        db.ChangeTracker.Clear();
        var loaded = await new BacktestRunRepository(db).FindAsync(run.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("PETR4", loaded.Symbol);
        Assert.True(loaded.From >= new DateOnly(2024, 6, 1));
        Assert.Equal(run.Equity.Count, loaded.Equity.Count);
        Assert.Equal(run.Trades, loaded.Trades);
        Assert.Equal(run.Metrics, loaded.Metrics);
        Assert.Equal(100m, loaded.Parameters["slow"]);
    }

    [SqlServerFact]
    public async Task Ciclo_de_aprendizado_respeita_o_indice_de_um_modelo_ativo_por_estrategia()
    {
        await using var db = database.CreateContext();
        await BacktestIntegrationTests.SeedAsync(db);

        var service = LearningIntegrationTests.Service(db, new RiskSettingsRepository(db));
        await service.TrainAsync(StrategyKeys.RsiMeanReversion, CancellationToken.None);
        await service.TrainAsync(StrategyKeys.RsiMeanReversion, CancellationToken.None);
        db.ChangeTracker.Clear();

        var models = await db.MetaModels.Where(m => m.StrategyKey == StrategyKeys.RsiMeanReversion).ToListAsync();
        Assert.Equal(2, models.Count);
        Assert.InRange(models.Count(m => m.Status == Domain.Learning.MetaModelStatus.Active), 0, 1);
        Assert.All(models.Where(m => m.ModelBytes is not null), m => Assert.Equal(Domain.Learning.FeatureVector.Count, m.FeatureImportance.Count));
    }

    [SqlServerFact]
    public async Task Indice_impede_dois_modelos_ativos_na_mesma_estrategia()
    {
        await using var db = database.CreateContext();
        static Domain.Learning.MetaModel Active()
        {
            var day = new DateOnly(2025, 1, 2);
            var model = new Domain.Learning.MetaModel("donchian-breakout", DateTimeOffset.UnixEpoch, day, day, day, 50, 0.5,
                new Domain.Learning.ValidationReport(20, 0.6, 50, 55, 0, 0, 50), new Domain.Learning.EconomicTest(0, 0.2m, null),
                new Dictionary<string, double>(), [1]);
            model.Activate("teste", DateTimeOffset.UnixEpoch);
            return model;
        }

        db.MetaModels.Add(Active());
        await db.SaveChangesAsync();
        db.MetaModels.Add(Active());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Trava_do_ciclo_vale_entre_conexoes_e_e_liberada()
    {
        await using var a = database.CreateContext();
        await using var b = database.CreateContext();
        var lockA = new Execution.SqlServerTradingLock(a);
        var lockB = new Execution.SqlServerTradingLock(b);

        await using (var held = await lockA.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None))
        {
            Assert.NotNull(held);
            Assert.Null(await lockB.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None));
        }

        await using var again = await lockB.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(again);
    }
}

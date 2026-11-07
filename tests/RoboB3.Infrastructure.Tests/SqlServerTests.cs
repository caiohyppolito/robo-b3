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
        if (!await db.Candles.AnyAsync())
        {
            await BacktestIntegrationTests.SeedAsync(db);
        }

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
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Persistence;

/// <summary>Aplica migrações e cria os dados iniciais (ativos, limites padrão, robô travado).</summary>
public sealed partial class DatabaseInitializer(
    RoboB3DbContext db,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public static IReadOnlyList<Asset> DefaultAssets { get; } =
    [
        new("IBOV", "Índice Bovespa", AssetClass.Index, "^BVSP", 1),
        new("BOVA11", "iShares Ibovespa", AssetClass.Etf, "BOVA11.SA", 1),
        new("SMAL11", "iShares Small Cap", AssetClass.Etf, "SMAL11.SA", 1),
        new("PETR4", "Petrobras PN", AssetClass.Stock, "PETR4.SA", 100),
        new("VALE3", "Vale ON", AssetClass.Stock, "VALE3.SA", 100),
        new("ITUB4", "Itaú Unibanco PN", AssetClass.Stock, "ITUB4.SA", 100),
        new("BBDC4", "Bradesco PN", AssetClass.Stock, "BBDC4.SA", 100),
        new("BBAS3", "Banco do Brasil ON", AssetClass.Stock, "BBAS3.SA", 100),
        new("B3SA3", "B3 ON", AssetClass.Stock, "B3SA3.SA", 100),
        new("ABEV3", "Ambev ON", AssetClass.Stock, "ABEV3.SA", 100),
        new("WEGE3", "WEG ON", AssetClass.Stock, "WEGE3.SA", 100),
        new("WIN", "Mini Índice (contrato vigente)", AssetClass.Future, null, 1),
        new("WDO", "Mini Dólar (contrato vigente)", AssetClass.Future, null, 1),
    ];

    public async Task InitializeAsync(bool applyMigrations, CancellationToken cancellationToken)
    {
        if (applyMigrations && db.Database.IsRelational())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await SeedAsync(cancellationToken);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var existingSymbols = await db.Assets.Select(a => a.Symbol).ToListAsync(cancellationToken);
        var missing = DefaultAssets.Where(a => !existingSymbols.Contains(a.Symbol)).ToList();
        foreach (var asset in missing)
        {
            db.Assets.Add(new Asset(asset.Symbol, asset.Name, asset.AssetClass, asset.DailyDataSymbol, asset.LotSize));
        }

        if (!await db.RiskSettings.AnyAsync(cancellationToken))
        {
            db.RiskSettings.Add(new RiskSettings(RiskLimits.Default, now));
        }

        if (!await db.TradingStates.AnyAsync(cancellationToken))
        {
            db.TradingStates.Add(TradingState.CreateHalted(now));
        }

        if (!await db.ExecutionSettings.AnyAsync(cancellationToken))
        {
            db.ExecutionSettings.Add(new Domain.Execution.ExecutionSettings(now));
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(missing.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Banco inicializado; {Count} ativos adicionados")]
    private partial void LogSeeded(int count);
}

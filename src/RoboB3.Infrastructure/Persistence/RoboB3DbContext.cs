using Microsoft.EntityFrameworkCore;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Learning;
using RoboB3.Domain.Paper;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Persistence;

public sealed class RoboB3DbContext(DbContextOptions<RoboB3DbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<Candle> Candles => Set<Candle>();

    public DbSet<RiskSettings> RiskSettings => Set<RiskSettings>();

    public DbSet<TradingState> TradingStates => Set<TradingState>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();

    public DbSet<MetaModel> MetaModels => Set<MetaModel>();

    public DbSet<TradeJournalEntry> TradeJournal => Set<TradeJournalEntry>();

    public DbSet<StrategyAssignment> StrategyAssignments => Set<StrategyAssignment>();

    public DbSet<PaperAccount> PaperAccounts => Set<PaperAccount>();

    public DbSet<PaperPosition> PaperPositions => Set<PaperPosition>();

    public DbSet<PaperFill> PaperFills => Set<PaperFill>();

    public DbSet<Domain.Execution.ExecutionSettings> ExecutionSettings => Set<Domain.Execution.ExecutionSettings>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("robo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RoboB3DbContext).Assembly);
    }
}

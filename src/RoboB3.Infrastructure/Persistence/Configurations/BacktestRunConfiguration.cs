using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoboB3.Domain.Backtesting;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal sealed class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<BacktestRun> builder)
    {
        builder.ToTable("BacktestRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Symbol).HasMaxLength(20);
        builder.Property(r => r.StrategyKey).HasMaxLength(50);
        builder.Property(r => r.RiskPerTradePercent).HasPrecision(5, 2);
        builder.HasIndex(r => r.CreatedAt);

        builder.ComplexProperty(r => r.Metrics, metrics =>
        {
            foreach (var property in typeof(PerformanceMetrics).GetProperties().Where(p => p.PropertyType == typeof(decimal)))
            {
                metrics.Property(property.Name).HasPrecision(18, 4);
            }
        });

        // Séries de tamanho variável e só lidas em bloco: guardadas como JSON.
        builder.Property(r => r.Parameters).HasConversion(JsonConverter<IReadOnlyDictionary<string, decimal>, Dictionary<string, decimal>>(), JsonComparer<IReadOnlyDictionary<string, decimal>>());
        builder.Property(r => r.Trades).HasConversion(JsonConverter<IReadOnlyList<BacktestTrade>, List<BacktestTrade>>(), JsonComparer<IReadOnlyList<BacktestTrade>>());
        builder.Property(r => r.Equity).HasConversion(JsonConverter<IReadOnlyList<EquityPoint>, List<EquityPoint>>(), JsonComparer<IReadOnlyList<EquityPoint>>());
        builder.Property(r => r.Events).HasConversion(JsonConverter<IReadOnlyList<BacktestEvent>, List<BacktestEvent>>(), JsonComparer<IReadOnlyList<BacktestEvent>>());
    }

    /// <summary>Compara pelo JSON serializado, para o EF detectar mudanças corretamente.</summary>
    private static ValueComparer<TModel> JsonComparer<TModel>() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<TModel>(JsonSerializer.Serialize(v, Json), Json)!);

    private static ValueConverter<TModel, string> JsonConverter<TModel, TConcrete>()
        where TConcrete : TModel =>
        new(
            v => JsonSerializer.Serialize(v, Json),
            v => (TModel)JsonSerializer.Deserialize<TConcrete>(v, Json)!);
}

internal sealed class ResearchExperimentConfiguration : IEntityTypeConfiguration<ResearchExperiment>
{
    public void Configure(EntityTypeBuilder<ResearchExperiment> builder)
    {
        builder.ToTable("ResearchExperiments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(50);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.CodeVersion).HasMaxLength(100);
        builder.HasIndex(e => e.CreatedAt);
    }
}

internal sealed class CdiRateConfiguration : IEntityTypeConfiguration<Domain.MarketData.CdiRate>
{
    public void Configure(EntityTypeBuilder<Domain.MarketData.CdiRate> builder)
    {
        builder.ToTable("CdiRates");
        builder.HasKey(r => r.Date);
        builder.Property(r => r.DailyPercent).HasPrecision(12, 8);
        builder.Ignore(r => r.AnnualizedPercent);
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RoboB3.Domain.Paper;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<IReadOnlyDictionary<string, decimal>> AsJson(this PropertyBuilder<IReadOnlyDictionary<string, decimal>> property) =>
        property.HasConversion(
            new ValueConverter<IReadOnlyDictionary<string, decimal>, string>(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Dictionary<string, decimal>>(v, Json)!),
            new ValueComparer<IReadOnlyDictionary<string, decimal>>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => new Dictionary<string, decimal>(v)));

    public static PropertyBuilder<IReadOnlyList<float>> AsJson(this PropertyBuilder<IReadOnlyList<float>> property) =>
        property.HasConversion(
            new ValueConverter<IReadOnlyList<float>, string>(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<float>>(v, Json)!),
            new ValueComparer<IReadOnlyList<float>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (h, f) => HashCode.Combine(h, f)),
                v => v.ToList()));
}

internal sealed class StrategyAssignmentConfiguration : IEntityTypeConfiguration<StrategyAssignment>
{
    public void Configure(EntityTypeBuilder<StrategyAssignment> builder)
    {
        builder.ToTable("StrategyAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Symbol).HasMaxLength(20);
        builder.Property(a => a.StrategyKey).HasMaxLength(50);
        builder.Property(a => a.Timeframe).HasConversion<string>().HasMaxLength(5);
        builder.Property(a => a.Parameters).AsJson();
        builder.HasOne<Domain.MarketData.Asset>().WithMany().HasForeignKey(a => a.Symbol).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaperAccountConfiguration : IEntityTypeConfiguration<PaperAccount>
{
    public void Configure(EntityTypeBuilder<PaperAccount> builder)
    {
        builder.ToTable("PaperAccount");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Ignore(a => a.Mode);
        foreach (var name in new[]
        {
            nameof(PaperAccount.InitialCapital), nameof(PaperAccount.Cash), nameof(PaperAccount.PeakEquity),
            nameof(PaperAccount.DayStartEquity), nameof(PaperAccount.CostsPaid), nameof(PaperAccount.RealizedProfit),
        })
        {
            builder.Property(name).HasPrecision(18, 4);
        }
    }
}

internal sealed class PaperPositionConfiguration : IEntityTypeConfiguration<PaperPosition>
{
    public void Configure(EntityTypeBuilder<PaperPosition> builder)
    {
        builder.ToTable("PaperPositions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Symbol).HasMaxLength(20);
        builder.Property(p => p.ContractSymbol).HasMaxLength(30);
        builder.Property(p => p.StrategyKey).HasMaxLength(50);
        builder.Property(p => p.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(10).HasDefaultValue(Domain.Execution.ExecutionMode.Paper);
        builder.HasIndex(p => p.Mode);
        foreach (var name in new[]
        {
            nameof(PaperPosition.EntryPrice), nameof(PaperPosition.Stop), nameof(PaperPosition.Multiplier),
            nameof(PaperPosition.EntryCost), nameof(PaperPosition.MarkPrice),
        })
        {
            builder.Property(name).HasPrecision(18, 6);
        }

        builder.Property(p => p.Features).AsJson();
        builder.Ignore(p => p.UnrealizedProfit);
        builder.Ignore(p => p.Notional);
        builder.Ignore(p => p.FeatureVector);
    }
}

internal sealed class PaperFillConfiguration : IEntityTypeConfiguration<PaperFill>
{
    public void Configure(EntityTypeBuilder<PaperFill> builder)
    {
        builder.ToTable("PaperFills");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Symbol).HasMaxLength(30);
        builder.Property(f => f.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(f => f.Reason).HasMaxLength(100);
        builder.Property(f => f.Mode).HasConversion<string>().HasMaxLength(10).HasDefaultValue(Domain.Execution.ExecutionMode.Paper);
        builder.Property(f => f.Price).HasPrecision(18, 6);
        builder.Property(f => f.Cost).HasPrecision(18, 4);
        builder.Property(f => f.RealizedProfit).HasPrecision(18, 4);
        builder.HasIndex(f => f.Time);
    }
}

internal sealed class ExecutionSettingsConfiguration : IEntityTypeConfiguration<Domain.Execution.ExecutionSettings>
{
    public void Configure(EntityTypeBuilder<Domain.Execution.ExecutionSettings> builder)
    {
        builder.ToTable("ExecutionSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.ChangedBy).HasMaxLength(100);
    }
}

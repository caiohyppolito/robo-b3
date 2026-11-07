using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RoboB3.Domain.Learning;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal sealed class MetaModelConfiguration : IEntityTypeConfiguration<MetaModel>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MetaModel> builder)
    {
        builder.ToTable("MetaModels");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.StrategyKey).HasMaxLength(50);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Decision).HasMaxLength(1000);
        foreach (var name in new[]
        {
            nameof(MetaModel.BaseWinRatePercent), nameof(MetaModel.FilteredWinRatePercent),
            nameof(MetaModel.BaseAverageReturnPercent), nameof(MetaModel.FilteredAverageReturnPercent),
            nameof(MetaModel.KeptPercent), nameof(MetaModel.UnfilteredSharpe), nameof(MetaModel.FilteredSharpe),
            nameof(MetaModel.ChampionSharpe),
        })
        {
            builder.Property(name).HasPrecision(10, 4);
        }

        builder.Property(m => m.FeatureImportance).HasConversion(
            new ValueConverter<IReadOnlyDictionary<string, double>, string>(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Dictionary<string, double>>(v, Json)!),
            new ValueComparer<IReadOnlyDictionary<string, double>>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => new Dictionary<string, double>(v)));

        // No máximo um modelo ativo por estratégia.
        builder.HasIndex(m => m.StrategyKey).IsUnique().HasFilter("[Status] = 'Active'");
        builder.HasIndex(m => m.CreatedAt);
    }
}

internal sealed class TradeJournalConfiguration : IEntityTypeConfiguration<TradeJournalEntry>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<TradeJournalEntry> builder)
    {
        builder.ToTable("TradeJournal");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.StrategyKey).HasMaxLength(50);
        builder.Property(e => e.Symbol).HasMaxLength(20);
        builder.Property(e => e.NetProfit).HasPrecision(18, 2);
        builder.Property(e => e.ReturnPercent).HasPrecision(10, 4);
        builder.Property(e => e.MaePercent).HasPrecision(10, 4);
        builder.Property(e => e.MfePercent).HasPrecision(10, 4);
        builder.Property(e => e.Features).HasConversion(
            new ValueConverter<IReadOnlyList<float>, string>(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<float>>(v, Json)!),
            new ValueComparer<IReadOnlyList<float>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (h, f) => HashCode.Combine(h, f)),
                v => v.ToList()));
        builder.Ignore(e => e.IsWin);
        builder.HasIndex(e => new { e.StrategyKey, e.EntryDate });
    }
}

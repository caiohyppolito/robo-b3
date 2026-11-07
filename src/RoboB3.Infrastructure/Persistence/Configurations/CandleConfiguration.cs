using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal sealed class CandleConfiguration : IEntityTypeConfiguration<Candle>
{
    public void Configure(EntityTypeBuilder<Candle> builder)
    {
        builder.ToTable("Candles");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Symbol).HasMaxLength(20);
        builder.Property(c => c.Timeframe).HasConversion<string>().HasMaxLength(5);
        builder.Property(c => c.Open).HasPrecision(18, 6);
        builder.Property(c => c.High).HasPrecision(18, 6);
        builder.Property(c => c.Low).HasPrecision(18, 6);
        builder.Property(c => c.Close).HasPrecision(18, 6);
        builder.Property(c => c.AdjustedClose).HasPrecision(18, 6);
        builder.HasIndex(c => new { c.Symbol, c.Timeframe, c.OpenTime }).IsUnique();
        builder.HasOne<Asset>().WithMany().HasForeignKey(c => c.Symbol).OnDelete(DeleteBehavior.Restrict);
    }
}

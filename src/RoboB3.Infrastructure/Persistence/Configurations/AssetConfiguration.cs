using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Symbol);
        builder.Property(a => a.Symbol).HasMaxLength(20);
        builder.Property(a => a.Name).HasMaxLength(120).IsRequired();
        builder.Property(a => a.AssetClass).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DailyDataSymbol).HasMaxLength(30);
        builder.Ignore(a => a.IsFuture);
    }
}

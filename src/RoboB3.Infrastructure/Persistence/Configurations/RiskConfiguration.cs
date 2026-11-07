using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Risk;

namespace RoboB3.Infrastructure.Persistence.Configurations;

internal sealed class RiskSettingsConfiguration : IEntityTypeConfiguration<RiskSettings>
{
    public void Configure(EntityTypeBuilder<RiskSettings> builder)
    {
        builder.ToTable("RiskSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.ComplexProperty(s => s.Limits, limits =>
        {
            limits.Property(l => l.MaxDailyLossPercent).HasPrecision(5, 2).HasColumnName("MaxDailyLossPercent");
            limits.Property(l => l.MaxDrawdownPercent).HasPrecision(5, 2).HasColumnName("MaxDrawdownPercent");
            limits.Property(l => l.MaxPositionPercentPerAsset).HasPrecision(5, 2).HasColumnName("MaxPositionPercentPerAsset");
            limits.Property(l => l.MaxOpenPositions).HasColumnName("MaxOpenPositions");
            limits.Property(l => l.MaxFuturesContracts).HasColumnName("MaxFuturesContracts");
        });
    }
}

internal sealed class TradingStateConfiguration : IEntityTypeConfiguration<TradingState>
{
    public void Configure(EntityTypeBuilder<TradingState> builder)
    {
        builder.ToTable("TradingState");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.HaltReason).HasMaxLength(500);
        builder.Property(s => s.ChangedBy).HasMaxLength(100);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100);
        builder.Property(a => a.Actor).HasMaxLength(100);
        builder.Property(a => a.Details).HasMaxLength(2000);
        builder.HasIndex(a => a.OccurredAt);
    }
}

using RoboB3.Domain.Common;

namespace RoboB3.Domain.Risk;

/// <summary>
/// Limites de risco configuráveis. Valores percentuais são relativos ao patrimônio (0–100).
/// </summary>
public sealed record RiskLimits
{
    public RiskLimits(
        decimal maxDailyLossPercent,
        decimal maxDrawdownPercent,
        decimal maxPositionPercentPerAsset,
        int maxOpenPositions,
        int maxFuturesContracts)
    {
        MaxDailyLossPercent = Guard.Percentage(maxDailyLossPercent, nameof(maxDailyLossPercent));
        MaxDrawdownPercent = Guard.Percentage(maxDrawdownPercent, nameof(maxDrawdownPercent));
        MaxPositionPercentPerAsset = Guard.Percentage(maxPositionPercentPerAsset, nameof(maxPositionPercentPerAsset));
        MaxOpenPositions = Guard.Positive(maxOpenPositions, nameof(maxOpenPositions));
        MaxFuturesContracts = Guard.Positive(maxFuturesContracts, nameof(maxFuturesContracts));

        if (MaxDailyLossPercent > MaxDrawdownPercent)
        {
            throw new DomainException("A perda diária máxima não pode ser maior que o drawdown máximo.");
        }
    }

    /// <summary>Perda máxima no dia, em % do patrimônio de abertura, antes de travar o robô.</summary>
    public decimal MaxDailyLossPercent { get; }

    /// <summary>Queda máxima a partir do pico de patrimônio antes de travar o robô.</summary>
    public decimal MaxDrawdownPercent { get; }

    /// <summary>Exposição máxima em um único ativo à vista, em % do patrimônio.</summary>
    public decimal MaxPositionPercentPerAsset { get; }

    public int MaxOpenPositions { get; }

    /// <summary>Número máximo de contratos futuros (WIN/WDO) em aberto somados.</summary>
    public int MaxFuturesContracts { get; }

    /// <summary>Padrão conservador para começar.</summary>
    public static RiskLimits Default { get; } = new(
        maxDailyLossPercent: 2m,
        maxDrawdownPercent: 15m,
        maxPositionPercentPerAsset: 20m,
        maxOpenPositions: 5,
        maxFuturesContracts: 2);
}

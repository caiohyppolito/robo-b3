using System.ComponentModel.DataAnnotations;

namespace RoboB3.Application.Paper;

/// <summary>Capital da conta simulada (seção "Trading").</summary>
public sealed class TradingCapitalOptions
{
    public const string SectionName = "Trading";

    [Range(1_000, 100_000_000)]
    public decimal InitialCapital { get; set; } = 100_000m;
}

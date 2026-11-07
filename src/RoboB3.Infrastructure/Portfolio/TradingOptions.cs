using System.ComponentModel.DataAnnotations;

namespace RoboB3.Infrastructure.Portfolio;

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    /// <summary>Capital inicial da carteira simulada, em reais.</summary>
    [Range(1_000, 100_000_000)]
    public decimal InitialCapital { get; set; } = 100_000m;
}

using System.ComponentModel.DataAnnotations;

namespace RoboB3.Infrastructure.MarketData.Mt5;

/// <summary>Conector local do MetaTrader 5 (seção "Mt5Gateway"). O token fica em user-secrets.</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Mt5Gateway";

    [Required]
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:8765/");

    public string? Token { get; set; }
}

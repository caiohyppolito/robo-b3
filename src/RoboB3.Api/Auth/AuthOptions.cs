using System.ComponentModel.DataAnnotations;

namespace RoboB3.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required]
    public string AdminUsername { get; set; } = string.Empty;

    /// <summary>Hash gerado com <c>dotnet run --project src/RoboB3.Api -- hash-password</c>.</summary>
    [Required]
    public string AdminPasswordHash { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "robo-b3";

    [Required]
    public string Audience { get; set; } = "robo-b3-web";

    /// <summary>Chave HMAC do JWT. Mínimo de 32 caracteres; nunca versionar.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 1440)]
    public int TokenLifetimeMinutes { get; set; } = 480;
}

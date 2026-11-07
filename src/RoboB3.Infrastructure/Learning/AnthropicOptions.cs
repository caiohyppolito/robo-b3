namespace RoboB3.Infrastructure.Learning;

/// <summary>Configuração do Claude (seção "Anthropic"). A chave fica só em user-secrets ou variável de ambiente.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";
}

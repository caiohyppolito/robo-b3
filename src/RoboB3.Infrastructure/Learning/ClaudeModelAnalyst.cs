using System.Globalization;
using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;

namespace RoboB3.Infrastructure.Learning;

/// <summary>
/// Pede ao Claude uma análise em português do que o modelo de IA aprendeu e de onde a estratégia mais erra.
/// Só lê números já calculados; não decide nada sobre operações.
/// </summary>
internal sealed class ClaudeModelAnalyst(IOptions<AnthropicOptions> options) : IModelAnalyst
{
    internal const string SystemPrompt = """
        Você é um analista quantitativo que explica, em português do Brasil e para um investidor pessoa física,
        o resultado de um modelo de machine learning que filtra sinais de uma estratégia de trading na B3.
        O modelo estima a chance de cada operação dar lucro e veta as de baixa chance; ele nunca cria operações.

        Escreva em Markdown, com no máximo cerca de 400 palavras, nestas seções:
        1. Veredito: o filtro ajuda ou não, com base nos números fora da amostra (AUC, taxa de acerto, Sharpe).
        2. O que o modelo aprendeu: interprete as variáveis mais importantes em linguagem simples.
        3. Onde a estratégia mais erra: padrões nas piores operações da validação.
        4. Próximos passos: no máximo três sugestões concretas e testáveis.

        Seja honesto: se a amostra é pequena ou a vantagem é marginal, diga. Não prometa lucro
        e não recomende operar com dinheiro real com base apenas nisto.
        """;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<string> AnalyzeAsync(ModelAnalysisInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var client = new AnthropicClient { ApiKey = options.Value.ApiKey };

        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = options.Value.Model,
            MaxTokens = 16000,
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            // Se o modelo recusar por política, o servidor redireciona para um modelo de reserva.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            System = SystemPrompt,
            Messages = [new BetaUserMessageParam { Content = BuildPrompt(input) }],
        }, cancellationToken);

        if (response.StopReason == "refusal")
        {
            throw new DomainException("O Claude recusou a análise deste modelo.");
        }

        var text = string.Join("\n", response.Content
            .Select(b => b.Value)
            .OfType<BetaTextBlock>()
            .Select(t => t.Text));

        return string.IsNullOrWhiteSpace(text)
            ? throw new DomainException("O Claude não retornou texto.")
            : text.Trim();
    }

    internal static string BuildPrompt(ModelAnalysisInput input)
    {
        var m = input.Model;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"Estratégia: {input.StrategyName} ({input.StrategyDescription})");
        sb.AppendLine(inv, $"Treino: {m.TrainingFrom:yyyy-MM-dd} a {m.SplitDate:yyyy-MM-dd}, {m.TrainingExamples} operações.");
        sb.AppendLine(inv, $"Validação (fora da amostra): {m.SplitDate:yyyy-MM-dd} a {m.ValidationTo:yyyy-MM-dd}, {m.ValidationExamples} operações.");
        sb.AppendLine(inv, $"AUC: {m.Auc:F3}. Limiar de probabilidade: {m.Threshold:F2}.");
        sb.AppendLine(inv, $"Taxa de acerto: {m.BaseWinRatePercent}% sem filtro, {m.FilteredWinRatePercent}% com filtro; o filtro mantém {m.KeptPercent}% dos sinais.");
        sb.AppendLine(inv, $"Retorno médio por operação: {m.BaseAverageReturnPercent}% sem filtro, {m.FilteredAverageReturnPercent}% com filtro.");
        var champion = m.ChampionSharpe is { } c ? string.Create(inv, $", {c} com o modelo anterior") : string.Empty;
        sb.AppendLine(inv, $"Sharpe médio fora da amostra: {m.UnfilteredSharpe} sem filtro, {m.FilteredSharpe} com filtro{champion}.");
        sb.AppendLine(inv, $"Decisão automática: {m.Decision}");
        sb.AppendLine();
        sb.AppendLine("Importância das variáveis (queda da AUC ao embaralhar; maior = mais usada):");
        foreach (var (name, value) in m.FeatureImportance.OrderByDescending(kv => kv.Value))
        {
            sb.AppendLine(inv, $"- {name}: {value:F4}");
        }

        sb.AppendLine();
        sb.AppendLine("Piores operações da validação (variáveis na entrada):");
        foreach (var e in input.WorstValidationTrades)
        {
            var features = string.Join(", ", FeatureVector.Names.Select((n, i) => string.Create(inv, $"{n}={e.Features.Values[i]:F3}")));
            sb.AppendLine(inv, $"- {e.Symbol} {e.EntryDate:yyyy-MM-dd} a {e.ExitDate:yyyy-MM-dd}: {e.ReturnPercent}% | {features}");
        }

        return sb.ToString();
    }
}

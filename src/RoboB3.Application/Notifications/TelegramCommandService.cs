using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Risk;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Execution;

namespace RoboB3.Application.Notifications;

/// <summary>
/// Confirmações pendentes (código de uso único por conversa e usuário). Fica em memória de propósito:
/// reiniciar o processo invalida qualquer código emitido.
/// </summary>
public sealed class TelegramConfirmations
{
    private readonly ConcurrentDictionary<(long Chat, long User), (string Code, string Command, DateTimeOffset Expires)> _pending = new();

    public string Issue(long chat, long user, string command, DateTimeOffset expires)
    {
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(CultureInfo.InvariantCulture);
        _pending[(chat, user)] = (code, command, expires);
        return code;
    }

    /// <summary>Consome o código: certo ou errado, ele deixa de valer (uma tentativa só).</summary>
    public string? Consume(long chat, long user, string code, DateTimeOffset now)
    {
        if (!_pending.TryRemove((chat, user), out var pending))
        {
            return null;
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(pending.Code),
            System.Text.Encoding.UTF8.GetBytes(code ?? string.Empty));
        return matches && now <= pending.Expires ? pending.Command : null;
    }
}

/// <summary>
/// Comandos recebidos pelo Telegram. Só consultas e travar o robô. Não destrava, não envia ordens,
/// não muda limites nem o modo de execução: isso só pelo painel, com senha.
/// O texto recebido é dado não confiável: só é comparado com a lista fixa de comandos.
/// </summary>
public sealed class TelegramCommandService(
    IOptions<TelegramOptions> options,
    TelegramConfirmations confirmations,
    KillSwitchService killSwitch,
    IExecutionSettingsRepository executionSettings,
    IPaperRepository paper,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string PauseCommand = "pausar";

    public const string Help =
        "Comandos: /status, /posicoes, /decisoes, /pausar (trava o robô, pede confirmação). " +
        "Destravar, mudar limites ou enviar ordens só pelo painel.";

    /// <summary>Trata a mensagem e devolve a resposta, ou null quando nada deve ser respondido.</summary>
    public async Task<string?> HandleAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        if (!settings.AllowedChatIds.Contains(update.ChatId) || !settings.AllowedUserIds.Contains(update.UserId))
        {
            // Não responde (não confirma que o bot existe), mas deixa rastro.
            await AuditAsync("telegram.nao-autorizado", update, "mensagem ignorada", cancellationToken);
            return null;
        }

        if (now - update.Date > TimeSpan.FromSeconds(settings.MaxMessageAgeSeconds) || update.Date - now > TimeSpan.FromMinutes(1))
        {
            await AuditAsync("telegram.mensagem-antiga", update, "ignorada por ser antiga", cancellationToken);
            return null;
        }

        var parts = update.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].StartsWith('/'))
        {
            return Help;
        }

        var command = parts[0][1..].Split('@')[0].ToLowerInvariant();
        await AuditAsync("telegram.comando", update, command, cancellationToken);

        return command switch
        {
            "status" or "start" => await StatusAsync(cancellationToken),
            "posicoes" => await PositionsAsync(cancellationToken),
            "decisoes" => await DecisionsAsync(cancellationToken),
            PauseCommand => RequestPause(update, now, settings),
            "confirmar" => await ConfirmAsync(update, parts.Length > 1 ? parts[1] : string.Empty, now, cancellationToken),
            "retomar" or "destravar" or "liberar" => "Destravar o robô só pelo painel, com senha. Pelo Telegram dá apenas para travar.",
            _ => Help,
        };
    }

    private string RequestPause(TelegramUpdate update, DateTimeOffset now, TelegramOptions settings)
    {
        var code = confirmations.Issue(update.ChatId, update.UserId, PauseCommand, now.AddSeconds(settings.ConfirmationSeconds));
        return $"Para travar o robô (novas entradas param; stops e saídas continuam), envie /confirmar {code} em até {settings.ConfirmationSeconds} segundos.";
    }

    private async Task<string> ConfirmAsync(TelegramUpdate update, string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var command = confirmations.Consume(update.ChatId, update.UserId, code, now);
        if (command != PauseCommand)
        {
            await AuditAsync("telegram.confirmacao-recusada", update, "código inválido, expirado ou já usado", cancellationToken);
            return "Código inválido, expirado ou já usado. Nada foi alterado. Envie /pausar de novo se quiser travar.";
        }

        await killSwitch.HaltAsync("Travado pelo Telegram", $"telegram:{update.UserId}", cancellationToken);
        return "Robô travado. Novas entradas estão bloqueadas; stops e saídas seguem funcionando. Para destravar, use o painel.";
    }

    private async Task<string> StatusAsync(CancellationToken cancellationToken)
    {
        var state = await killSwitch.GetStateAsync(cancellationToken);
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        var account = await paper.GetAccountAsync(mode, cancellationToken);
        var positions = await paper.ListPositionsAsync(mode, cancellationToken);
        var lockText = state.IsHalted ? $"TRAVADO ({state.HaltReason})" : "liberado";
        var cash = account is null ? "sem conta" : account.Cash.ToString("C2", Brl);
        return $"Modo: {mode.Describe()}. Robô {lockText}. Posições abertas: {positions.Count}. Caixa: {cash}.";
    }

    private async Task<string> PositionsAsync(CancellationToken cancellationToken)
    {
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        var positions = await paper.ListPositionsAsync(mode, cancellationToken);
        if (positions.Count == 0)
        {
            return "Nenhuma posição aberta.";
        }

        return string.Join('\n', positions.Select(p =>
            $"{p.ContractSymbol}: {(p.Direction == Domain.Strategies.PositionDirection.Long ? "comprado" : "vendido")} {p.Quantity} a {p.EntryPrice.ToString("N2", Brl)}, stop {p.Stop?.ToString("N2", Brl) ?? "—"}"));
    }

    private async Task<string> DecisionsAsync(CancellationToken cancellationToken)
    {
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        var decisions = await paper.ListRecentDecisionsAsync(mode, 5, cancellationToken);
        if (decisions.Count == 0)
        {
            return "Nenhuma decisão registrada ainda.";
        }

        return string.Join('\n', decisions.Select(d =>
            $"{Domain.MarketData.B3Calendar.ToLocal(d.Time):dd/MM HH:mm} {d.Symbol} {d.StrategyKey}: {d.Outcome} — {d.Detail}"));
    }

    private async Task AuditAsync(string action, TelegramUpdate update, string details, CancellationToken cancellationToken)
    {
        audit.Record(new AuditEntry(action, $"telegram:{update.UserId}", $"conversa {update.ChatId}: {details}", clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static readonly CultureInfo Brl = CultureInfo.GetCultureInfo("pt-BR");
}

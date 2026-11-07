namespace RoboB3.Application.Notifications;

/// <summary>
/// Configuração do Telegram. O token do bot é segredo: fica em user-secrets ou variável de ambiente
/// (Telegram__BotToken), nunca no appsettings versionado.
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; }

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Conversas que recebem avisos e podem mandar comandos.</summary>
    public long[] AllowedChatIds { get; set; } = [];

    /// <summary>Usuários do Telegram autorizados a mandar comandos (além da conversa estar liberada).</summary>
    public long[] AllowedUserIds { get; set; } = [];

    /// <summary>Prazo para confirmar um comando que altera a operação.</summary>
    public int ConfirmationSeconds { get; set; } = 120;

    /// <summary>Mensagens mais velhas que isso são ignoradas (protege contra reenvio e fila acumulada).</summary>
    public int MaxMessageAgeSeconds { get; set; } = 120;

    /// <summary>Horário (Brasília, HHmm) do resumo diário.</summary>
    public int DailySummaryHhmm { get; set; } = 1815;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(BotToken) && AllowedChatIds.Length > 0;
}

public sealed record TelegramUpdate(long UpdateId, long ChatId, long UserId, string Text, DateTimeOffset Date);

/// <summary>Porta para a API de bots do Telegram (só texto puro, sem formatação).</summary>
public interface ITelegramClient
{
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken);
}

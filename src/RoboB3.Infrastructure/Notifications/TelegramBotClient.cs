using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RoboB3.Application.Notifications;

namespace RoboB3.Infrastructure.Notifications;

/// <summary>Falha ao falar com o Telegram. A mensagem nunca contém a URL (que carrega o token).</summary>
public sealed class TelegramException(string message) : Exception(message);

/// <summary>
/// Cliente da API de bots do Telegram. O token vai no caminho da URL, por isso: o HttpClient é registrado sem
/// logs de requisição e qualquer erro é relançado sem a exceção original (que poderia citar a URL).
/// </summary>
internal sealed class TelegramBotClient(HttpClient http, IOptions<TelegramOptions> options) : ITelegramClient
{
    public const string HttpClientName = "telegram";

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var body = new { chat_id = chatId, text = text.Length > 4000 ? text[..4000] : text, disable_web_page_preview = true };
        using var response = await SafeAsync(() => http.PostAsJsonAsync(Path("sendMessage"), body, cancellationToken), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramException($"Telegram recusou o envio (HTTP {(int)response.StatusCode}).");
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"{Path("getUpdates")}?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";
        using var response = await SafeAsync(() => http.GetAsync(url, cancellationToken), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramException($"Telegram recusou a leitura de mensagens (HTTP {(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<UpdatesDto>(cancellationToken);
        return Parse(payload);
    }

    internal static IReadOnlyList<TelegramUpdate> Parse(UpdatesDto? payload)
    {
        if (payload is not { Ok: true, Result: { } result })
        {
            return [];
        }

        return result
            .Select(u => u.Message is { Chat: { } chat, From: { } from } m
                ? new TelegramUpdate(u.UpdateId, chat.Id, from.Id, m.Text ?? string.Empty, DateTimeOffset.FromUnixTimeSeconds(m.Date))
                : new TelegramUpdate(u.UpdateId, 0, 0, string.Empty, DateTimeOffset.MinValue))
            .ToList();
    }

    private string Path(string method)
    {
        var token = options.Value.BotToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TelegramException("Token do Telegram não configurado (Telegram__BotToken).");
        }

        return $"bot{Uri.EscapeDataString(token).Replace("%3A", ":", StringComparison.Ordinal)}/{method}";
    }

    private static async Task<HttpResponseMessage> SafeAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new TelegramException($"Falha de rede ao falar com o Telegram ({ex.GetType().Name}).");
        }
    }

    internal sealed record UpdatesDto(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] List<UpdateDto>? Result);

    internal sealed record UpdateDto(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] MessageDto? Message);

    internal sealed record MessageDto(
        [property: JsonPropertyName("date")] long Date,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("chat")] IdDto? Chat,
        [property: JsonPropertyName("from")] IdDto? From);

    internal sealed record IdDto([property: JsonPropertyName("id")] long Id);
}

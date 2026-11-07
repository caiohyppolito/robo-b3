using System.Globalization;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Notifications;

/// <summary>Até onde os avisos já foram enviados. Em memória: ao reiniciar, recomeça do presente (sem enxurrada).</summary>
public sealed class TelegramNotificationCursor
{
    public long? LastAuditId { get; set; }

    public long? LastFillId { get; set; }

    public DateOnly? LastSummaryDate { get; set; }
}

/// <summary>
/// Avisos pelo Telegram: entradas, saídas, recusas, travas, falhas de ordem, troca de modo, limites e modelos da IA,
/// e um resumo diário. Só lê o que o robô já registrou; não decide nada.
/// </summary>
public sealed class TelegramNotificationService(
    IOptions<TelegramOptions> options,
    ITelegramClient telegram,
    TelegramNotificationCursor cursor,
    IAuditLog audit,
    IPaperRepository paper,
    IExecutionSettingsRepository executionSettings,
    TimeProvider clock)
{
    private static readonly CultureInfo Brl = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Dictionary<string, string> AuditTitles = new()
    {
        ["kill-switch.halt"] = "🔒 Robô travado",
        ["kill-switch.resume"] = "🔓 Robô destravado",
        ["emergencia.zerar-tudo"] = "🚨 Zerar tudo acionado",
        ["execucao.modo-alterado"] = "⚙️ Modo de execução alterado",
        ["ordem.recusada"] = "⛔ Ordem recusada",
        ["ordem.nao-confirmada"] = "⚠️ Ordem sem confirmação da corretora",
        ["ordem.recuperada"] = "♻️ Ordem recuperada após reinício",
        ["risk-limits.update"] = "⚙️ Limites de risco alterados",
        ["ia.modelo-promovido"] = "🧠 Modelo da IA promovido",
        ["ia.modelo-aposentado"] = "🧠 Modelo da IA aposentado",
        ["operacao.posicao-encerrada"] = "✋ Posição encerrada manualmente",
    };

    /// <summary>Junta os eventos novos e envia. Devolve quantas mensagens foram mandadas.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return 0;
        }

        var messages = new List<string>();
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;

        var entries = await audit.ListRecentAsync(100, cancellationToken);
        if (cursor.LastAuditId is null)
        {
            cursor.LastAuditId = entries.Count > 0 ? entries.Max(e => e.Id) : 0;
        }
        else
        {
            foreach (var entry in entries.Where(e => e.Id > cursor.LastAuditId).OrderBy(e => e.Id))
            {
                if (AuditTitles.TryGetValue(entry.Action, out var title))
                {
                    messages.Add($"{title}\n{entry.Details} ({entry.Actor}, {B3Calendar.ToLocal(entry.OccurredAt):HH:mm})");
                }

                cursor.LastAuditId = entry.Id;
            }
        }

        var fills = await paper.ListRecentFillsAsync(mode, 100, cancellationToken);
        if (cursor.LastFillId is null)
        {
            cursor.LastFillId = fills.Count > 0 ? fills.Max(f => f.Id) : 0;
        }
        else
        {
            foreach (var fill in fills.Where(f => f.Id > cursor.LastFillId).OrderBy(f => f.Id))
            {
                var side = fill.Side == OrderSide.Buy ? "Compra" : "Venda";
                var result = fill.RealizedProfit is { } profit ? $" — resultado {profit.ToString("C2", Brl)}" : string.Empty;
                messages.Add($"{(fill.RealizedProfit is null ? "▶️ Entrada" : "⏹️ Saída")} [{mode.Describe()}]\n{side} {fill.Quantity} {fill.Symbol} a {fill.Price.ToString("N2", Brl)} ({fill.Reason}){result}");
                cursor.LastFillId = fill.Id;
            }
        }

        var now = clock.GetUtcNow();
        var local = B3Calendar.ToLocal(now);
        var today = DateOnly.FromDateTime(local.DateTime);
        var isWeekday = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        if (isWeekday && cursor.LastSummaryDate != today && (local.Hour * 100) + local.Minute >= settings.DailySummaryHhmm)
        {
            var todays = fills.Where(f => B3Calendar.TradingDate(f.Time) == today).ToList();
            var realized = todays.Sum(f => f.RealizedProfit ?? 0);
            var exits = todays.Count(f => f.RealizedProfit is not null);
            var open = (await paper.ListPositionsAsync(mode, cancellationToken)).Count;
            messages.Add($"📊 Resumo de {today:dd/MM} [{mode.Describe()}]\n{todays.Count - exits} entradas, {exits} saídas, resultado realizado {realized.ToString("C2", Brl)}. Posições abertas: {open}.");
            cursor.LastSummaryDate = today;
        }

        foreach (var message in messages)
        {
            foreach (var chat in settings.AllowedChatIds)
            {
                await telegram.SendMessageAsync(chat, message, cancellationToken);
            }
        }

        return messages.Count;
    }
}

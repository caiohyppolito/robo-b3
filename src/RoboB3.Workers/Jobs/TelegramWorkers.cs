using Microsoft.Extensions.Options;
using RoboB3.Application.Notifications;

namespace RoboB3.Workers.Jobs;

/// <summary>Envia os avisos do robô pelo Telegram (desligado se não houver token e conversas liberadas).</summary>
public sealed class TelegramNotificationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramNotificationWorker> logger) : PeriodicScopedWorker(scopeFactory, logger)
{
    protected override string JobName => "telegram-avisos";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken stoppingToken) =>
        services.GetRequiredService<TelegramNotificationService>().RunAsync(stoppingToken);
}

/// <summary>
/// Lê os comandos enviados ao bot (long polling, sem webhook: o PC não precisa ficar exposto na internet).
/// Ao iniciar, descarta mensagens acumuladas. Cada mensagem é confirmada ANTES de ser tratada: na dúvida,
/// um comando se perde, mas nunca é executado duas vezes.
/// </summary>
public sealed partial class TelegramCommandWorker(
    IServiceScopeFactory scopeFactory,
    ITelegramClient telegram,
    IOptions<TelegramOptions> options,
    ILogger<TelegramCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            LogDisabled();
            return;
        }

        long? offset = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (offset is null)
                {
                    var backlog = await telegram.GetUpdatesAsync(-1, 0, stoppingToken);
                    offset = backlog.Count > 0 ? backlog.Max(u => u.UpdateId) + 1 : 0;
                    LogStarted();
                    continue;
                }

                var updates = await telegram.GetUpdatesAsync(offset.Value, 25, stoppingToken);
                foreach (var update in updates.OrderBy(u => u.UpdateId))
                {
                    offset = update.UpdateId + 1;
                    if (update.ChatId == 0)
                    {
                        continue;
                    }

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var reply = await scope.ServiceProvider.GetRequiredService<TelegramCommandService>().HandleAsync(update, stoppingToken);
                    if (reply is not null)
                    {
                        await telegram.SendMessageAsync(update.ChatId, reply, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Só o tipo e a mensagem já sanitizada: nada de URL (que contém o token).
                LogFailed(ex.GetType().Name, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram desligado (sem Telegram:Enabled, token ou conversas liberadas)")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram: lendo comandos")]
    private partial void LogStarted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram: falha ({Type}: {Message}); nova tentativa em 30 s")]
    private partial void LogFailed(string type, string message);
}

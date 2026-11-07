using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Notifications;
using RoboB3.Application.Risk;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Trading;
using RoboB3.Infrastructure.Notifications;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

internal sealed class RecordingTelegram : ITelegramClient
{
    public List<(long Chat, string Text)> Sent { get; } = [];

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        Sent.Add((chatId, text));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
}

public class TelegramTests
{
    private const long Chat = 1001;
    private const long User = 42;

    // Segunda-feira, 14h em Brasília.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero));
    private readonly RoboB3DbContext _db = new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly TradingState _state;
    private readonly TelegramConfirmations _confirmations = new();
    private readonly TelegramOptions _options = new() { Enabled = true, BotToken = "123:segredo", AllowedChatIds = [Chat], AllowedUserIds = [User] };

    public TelegramTests()
    {
        _state = TradingState.CreateHalted(_clock.GetUtcNow());
        _state.Resume("teste", _clock.GetUtcNow());
    }

    private sealed class FixedState(TradingState state) : ITradingStateRepository
    {
        public Task<TradingState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(state);
    }

    private TelegramCommandService Commands() => new(
        Options.Create(_options),
        _confirmations,
        new KillSwitchService(new FixedState(_state), new AuditLog(_db), _db, _clock),
        new FakeExecutionSettings(new Domain.Execution.ExecutionSettings(_clock.GetUtcNow())),
        new PaperRepository(_db),
        new AuditLog(_db),
        _db,
        _clock);

    private TelegramUpdate Message(string text, long chat = Chat, long user = User) =>
        new(1, chat, user, text, _clock.GetUtcNow());

    private static string CodeFrom(string reply) => Regex.Match(reply, @"/confirmar (\d{6})").Groups[1].Value;

    [Fact]
    public async Task Conversa_ou_usuario_nao_autorizado_e_ignorado_e_auditado()
    {
        var fromStranger = await Commands().HandleAsync(Message("/pausar", chat: 999), CancellationToken.None);
        var wrongUser = await Commands().HandleAsync(Message("/status", user: 7), CancellationToken.None);

        Assert.Null(fromStranger);
        Assert.Null(wrongUser);
        Assert.Equal(2, await _db.AuditEntries.CountAsync(a => a.Action == "telegram.nao-autorizado"));
        Assert.False(_state.IsHalted);
    }

    [Fact]
    public async Task Pausar_so_trava_depois_da_confirmacao_com_o_codigo()
    {
        var ask = await Commands().HandleAsync(Message("/pausar"), CancellationToken.None);
        Assert.False(_state.IsHalted);

        var done = await Commands().HandleAsync(Message($"/confirmar {CodeFrom(ask!)}"), CancellationToken.None);

        Assert.True(_state.IsHalted);
        Assert.Contains("travado", done, StringComparison.Ordinal);
        Assert.Equal($"telegram:{User}", _state.ChangedBy);
        Assert.True(await _db.AuditEntries.AnyAsync(a => a.Action == "kill-switch.halt"));
    }

    [Fact]
    public async Task Codigo_vale_uma_vez_so_e_tentativa_errada_o_invalida()
    {
        var ask = await Commands().HandleAsync(Message("/pausar"), CancellationToken.None);
        var code = CodeFrom(ask!);
        var wrong = code == "111111" ? "222222" : "111111";

        await Commands().HandleAsync(Message($"/confirmar {wrong}"), CancellationToken.None);
        var replay = await Commands().HandleAsync(Message($"/confirmar {code}"), CancellationToken.None);

        Assert.False(_state.IsHalted);
        Assert.Contains("inválido", replay, StringComparison.Ordinal);
        Assert.True(await _db.AuditEntries.AnyAsync(a => a.Action == "telegram.confirmacao-recusada"));
    }

    [Fact]
    public async Task Codigo_expirado_nao_trava()
    {
        var ask = await Commands().HandleAsync(Message("/pausar"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(_options.ConfirmationSeconds + 1));

        await Commands().HandleAsync(Message($"/confirmar {CodeFrom(ask!)}"), CancellationToken.None);

        Assert.False(_state.IsHalted);
    }

    [Fact]
    public async Task Codigo_de_outro_usuario_nao_serve()
    {
        _options.AllowedUserIds = [User, 43];
        var ask = await Commands().HandleAsync(Message("/pausar"), CancellationToken.None);

        await Commands().HandleAsync(Message($"/confirmar {CodeFrom(ask!)}", user: 43), CancellationToken.None);

        Assert.False(_state.IsHalted);
    }

    [Fact]
    public async Task Mensagem_antiga_e_ignorada()
    {
        var old = new TelegramUpdate(1, Chat, User, "/pausar", _clock.GetUtcNow().AddMinutes(-10));

        Assert.Null(await Commands().HandleAsync(old, CancellationToken.None));
        Assert.True(await _db.AuditEntries.AnyAsync(a => a.Action == "telegram.mensagem-antiga"));
    }

    [Fact]
    public async Task Telegram_nunca_destrava_o_robo()
    {
        _state.Halt("divergência", "robo", _clock.GetUtcNow());

        var reply = await Commands().HandleAsync(Message("/retomar"), CancellationToken.None);

        Assert.True(_state.IsHalted);
        Assert.Contains("painel", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_responde_modo_trava_e_posicoes()
    {
        var reply = await Commands().HandleAsync(Message("/status"), CancellationToken.None);

        Assert.Contains("liberado", reply, StringComparison.Ordinal);
        Assert.Contains("Posições abertas: 0", reply, StringComparison.Ordinal);
    }

    private TelegramNotificationService Notifier(RecordingTelegram telegram, TelegramNotificationCursor cursor) => new(
        Options.Create(_options),
        telegram,
        cursor,
        new AuditLog(_db),
        new PaperRepository(_db),
        new FakeExecutionSettings(new Domain.Execution.ExecutionSettings(_clock.GetUtcNow())),
        _clock);

    [Fact]
    public async Task Avisos_comecam_do_presente_e_mandam_so_eventos_relevantes()
    {
        _db.AuditEntries.Add(new AuditEntry("kill-switch.halt", "robo", "antigo", _clock.GetUtcNow()));
        await _db.SaveChangesAsync();
        var telegram = new RecordingTelegram();
        var cursor = new TelegramNotificationCursor();

        Assert.Equal(0, await Notifier(telegram, cursor).RunAsync(CancellationToken.None));

        _db.AuditEntries.Add(new AuditEntry("kill-switch.halt", "robo", "divergência com a corretora", _clock.GetUtcNow()));
        _db.AuditEntries.Add(new AuditEntry("telegram.comando", "telegram:42", "status", _clock.GetUtcNow()));
        _db.PaperFills.Add(new PaperFill(Guid.NewGuid(), "WINZ26", OrderSide.Buy, 2, 130_005m, 1m, null, "entrada", _clock.GetUtcNow()));
        _db.PaperFills.Add(new PaperFill(Guid.NewGuid(), "WINZ26", OrderSide.Sell, 2, 130_200m, 1m, 78m, "alvo", _clock.GetUtcNow()));
        await _db.SaveChangesAsync();

        var sent = await Notifier(telegram, cursor).RunAsync(CancellationToken.None);

        Assert.Equal(3, sent);
        Assert.All(telegram.Sent, m => Assert.Equal(Chat, m.Chat));
        Assert.Contains(telegram.Sent, m => m.Text.Contains("divergência", StringComparison.Ordinal));
        Assert.DoesNotContain(telegram.Sent, m => m.Text.Contains("antigo", StringComparison.Ordinal));
        Assert.Contains(telegram.Sent, m => m.Text.Contains("Saída", StringComparison.Ordinal) && m.Text.Contains("alvo", StringComparison.Ordinal));
        Assert.Equal(0, await Notifier(telegram, cursor).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Resumo_diario_sai_uma_vez_depois_do_horario()
    {
        var telegram = new RecordingTelegram();
        var cursor = new TelegramNotificationCursor();
        await Notifier(telegram, cursor).RunAsync(CancellationToken.None);
        _db.PaperFills.Add(new PaperFill(Guid.NewGuid(), "WINZ26", OrderSide.Sell, 2, 130_200m, 1m, 78m, "alvo", _clock.GetUtcNow()));
        await _db.SaveChangesAsync();

        _clock.Advance(TimeSpan.FromHours(4.5)); // 18h30 em Brasília
        await Notifier(telegram, cursor).RunAsync(CancellationToken.None);
        await Notifier(telegram, cursor).RunAsync(CancellationToken.None);

        var summary = Assert.Single(telegram.Sent, m => m.Text.StartsWith("📊", StringComparison.Ordinal));
        Assert.Contains("1 saídas", summary.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_token_ou_desligado_nao_envia_nada()
    {
        _options.Enabled = false;
        var telegram = new RecordingTelegram();
        _db.PaperFills.Add(new PaperFill(Guid.NewGuid(), "WINZ26", OrderSide.Buy, 2, 130_005m, 1m, null, "entrada", _clock.GetUtcNow()));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await Notifier(telegram, new TelegramNotificationCursor()).RunAsync(CancellationToken.None));
        Assert.Empty(telegram.Sent);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private TelegramBotClient Client(Handler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") }, Options.Create(_options));

    [Fact]
    public async Task Cliente_le_mensagens_no_formato_da_api()
    {
        const string json = """
            {"ok":true,"result":[
              {"update_id":10,"message":{"message_id":1,"date":1794243600,"text":"/status","chat":{"id":1001},"from":{"id":42}}},
              {"update_id":11,"edited_message":{"message_id":1}}
            ]}
            """;
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        var updates = await Client(handler).GetUpdatesAsync(10, 25, CancellationToken.None);

        Assert.Equal(2, updates.Count);
        Assert.Equal(new TelegramUpdate(10, 1001, 42, "/status", DateTimeOffset.FromUnixTimeSeconds(1794243600)), updates[0]);
        Assert.Equal(0, updates[1].ChatId);
        Assert.Contains("offset=10", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Envio_manda_texto_puro_para_a_conversa()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") });

        await Client(handler).SendMessageAsync(Chat, "olá <b>", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/sendMessage", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.Equal(Chat, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("olá <b>", body.RootElement.GetProperty("text").GetString());
        Assert.False(body.RootElement.TryGetProperty("parse_mode", out _));
    }

    [Fact]
    public async Task Erro_nunca_expoe_o_token()
    {
        var failing = new Handler(r => throw new HttpRequestException($"falhou {r.RequestUri}"));
        var refused = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var network = await Assert.ThrowsAsync<TelegramException>(() => Client(failing).SendMessageAsync(Chat, "x", CancellationToken.None));
        var http = await Assert.ThrowsAsync<TelegramException>(() => Client(refused).GetUpdatesAsync(0, 0, CancellationToken.None));

        Assert.DoesNotContain("segredo", network.Message, StringComparison.Ordinal);
        Assert.Null(network.InnerException);
        Assert.DoesNotContain("segredo", http.Message, StringComparison.Ordinal);
    }
}

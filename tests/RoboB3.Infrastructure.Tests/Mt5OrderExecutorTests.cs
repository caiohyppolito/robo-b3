using System.Net;
using System.Text;
using System.Text.Json;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Trading;
using RoboB3.Infrastructure.Execution;

namespace RoboB3.Infrastructure.Tests;

public class Mt5OrderExecutorTests
{
    private sealed class Recorder(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public string? Path { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri!.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private static (Mt5OrderExecutor Executor, Recorder Recorder) Create(ExecutionMode mode, HttpStatusCode status, string response)
    {
        var recorder = new Recorder(status, response);
        var http = new HttpClient(recorder) { BaseAddress = new Uri("http://127.0.0.1:8765/") };
        return (new Mt5OrderExecutor(http, mode), recorder);
    }

    [Fact]
    public async Task Ordem_segue_o_contrato_do_conector()
    {
        var (executor, recorder) = Create(ExecutionMode.Mt5Demo, HttpStatusCode.OK,
            """{"filled":true,"price":130010.0,"volume":2,"ticket":501,"message":"Executada."}""");

        var result = await executor.SendAsync(
            new OrderInstruction("WINZ26", OrderSide.Buy, 2, 130_005m, 129_800m, null, "e-123456789", "entrada donchian-breakout"),
            CancellationToken.None);

        Assert.Equal("/orders", recorder.Path);
        using var body = JsonDocument.Parse(recorder.Body!);
        var root = body.RootElement;
        Assert.Equal("WINZ26", root.GetProperty("symbol").GetString());
        Assert.Equal("buy", root.GetProperty("side").GetString());
        Assert.Equal(2, root.GetProperty("volume").GetInt32());
        Assert.Equal(129_800m, root.GetProperty("stop_loss").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("position_ticket").ValueKind);
        Assert.Equal("e-123456789", root.GetProperty("client_id").GetString());
        Assert.Equal("demo", root.GetProperty("expected_mode").GetString());
        Assert.True(root.GetProperty("comment").GetString()!.Length <= 31);

        Assert.True(result.Filled);
        Assert.Equal(130_010m, result.Price);
        Assert.Equal(501, result.BrokerTicket);
    }

    [Fact]
    public async Task Conta_real_pede_modo_real()
    {
        var (executor, recorder) = Create(ExecutionMode.Mt5Real, HttpStatusCode.OK, """{"filled":true,"price":36.3,"volume":100,"ticket":7,"message":"ok"}""");

        await executor.SendAsync(new OrderInstruction("PETR4", OrderSide.Sell, 100, 36.3m, null, 7, "s-123456789", "sinal"), CancellationToken.None);

        using var body = JsonDocument.Parse(recorder.Body!);
        Assert.Equal("real", body.RootElement.GetProperty("expected_mode").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("position_ticket").GetInt64());
    }

    [Fact]
    public async Task Recusa_do_conector_vira_ordem_nao_executada_com_o_motivo()
    {
        var (executor, recorder) = Create(ExecutionMode.Mt5Demo, HttpStatusCode.Conflict,
            """{"detail":"A conta logada no MT5 é real, mas a ordem é para conta demo."}""");

        var result = await executor.SendAsync(new OrderInstruction("WINZ26", OrderSide.Buy, 1, 1m, null, null, "e-123456789", "x"), CancellationToken.None);

        Assert.False(result.Filled);
        Assert.Contains("conta logada no MT5 é real", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task Posicoes_identificam_as_do_robo_pelo_numero_magico()
    {
        const string json = """
            [{"ticket":1,"symbol":"WINZ26","side":"buy","volume":1,"price":130000,"stopLoss":129800,"profit":0,"magic":20261107},
             {"ticket":2,"symbol":"PETR4","side":"sell","volume":100,"price":36,"stopLoss":null,"profit":0,"magic":0}]
            """;
        var (executor, _) = Create(ExecutionMode.Mt5Demo, HttpStatusCode.OK, json);

        var positions = await executor.GetPositionsAsync(CancellationToken.None);

        Assert.True(positions![0].IsRobot);
        Assert.False(positions[1].IsRobot);
        Assert.Equal(OrderSide.Sell, positions[1].Side);
    }
}

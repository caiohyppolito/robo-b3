using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Trading;

namespace RoboB3.Infrastructure.Execution;

/// <summary>
/// Envia ordens ao MetaTrader 5 pelo conector local. Sem novas tentativas automáticas: repetir uma ordem pode duplicá-la.
/// Se a resposta não chegar, a ordem é tratada como não confirmada e a conciliação do próximo ciclo decide (e trava, se preciso).
/// </summary>
internal sealed class Mt5OrderExecutor(HttpClient http, ExecutionMode mode) : IOrderExecutor
{
    /// <summary>Número mágico das ordens do robô no MT5 (o mesmo do conector).</summary>
    public const long RobotMagic = 20261107;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ExecutionMode Mode => mode;

    public async Task<OrderResult> SendAsync(OrderInstruction instruction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var body = new OrderDto(
            instruction.Symbol,
            instruction.Side == OrderSide.Buy ? "buy" : "sell",
            instruction.Quantity,
            instruction.StopLoss,
            instruction.PositionTicket,
            instruction.ClientOrderId,
            mode == ExecutionMode.Mt5Real ? "real" : "demo",
            Truncate($"robo-b3 {instruction.Reason}", 31),
            instruction.TakeProfit);

        try
        {
            using var response = await http.PostAsJsonAsync("orders", body, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadDetailAsync(response, cancellationToken);
                return new OrderResult(false, 0, 0, null, $"Conector recusou a ordem: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<OrderResultDto>(Json, cancellationToken)
                ?? throw new JsonException("Resposta vazia.");
            return new OrderResult(result.Filled, result.Price ?? 0, result.Volume ?? 0, result.Ticket, result.Message ?? string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new OrderResult(false, 0, 0, null, $"Sem confirmação do conector ({ex.Message}). Confira a posição no MT5.");
        }
    }

    public async Task<bool> UpdateStopAsync(string symbol, long ticket, decimal stop, CancellationToken cancellationToken)
    {
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture, $"positions/{ticket}/stop?symbol={Uri.EscapeDataString(symbol)}&stop_loss={stop}");
            using var response = await http.PostAsync(url, null, cancellationToken);
            return response.IsSuccessStatusCode
                && (await response.Content.ReadFromJsonAsync<StopResultDto>(Json, cancellationToken))?.Ok == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<BrokerPosition>?> GetPositionsAsync(CancellationToken cancellationToken)
    {
        var positions = await http.GetFromJsonAsync<List<PositionDto>>("positions", Json, cancellationToken)
            ?? throw new InvalidOperationException("Conector não informou as posições.");
        return positions
            .Select(p => new BrokerPosition(p.Ticket, p.Symbol, p.Side == "buy" ? OrderSide.Buy : OrderSide.Sell, p.Volume, p.Price, p.StopLoss, p.Magic == RobotMagic))
            .ToList();
    }

    public async Task<decimal?> GetClosingPriceAsync(long ticket, CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<ClosingPriceDto>($"positions/{ticket}/closing-price", Json, cancellationToken))?.Price;

    public async Task<decimal?> GetEquityAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<AccountDto>("account", Json, cancellationToken))?.Equity;

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<DetailDto>(Json, cancellationToken);
            return problem?.Detail?.ToString() ?? $"HTTP {(int)response.StatusCode}";
        }
        catch (JsonException)
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    internal sealed record OrderDto(
        string Symbol,
        string Side,
        int Volume,
        [property: JsonPropertyName("stop_loss")] decimal? StopLoss,
        [property: JsonPropertyName("position_ticket")] long? PositionTicket,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("expected_mode")] string ExpectedMode,
        string Comment,
        [property: JsonPropertyName("take_profit")] decimal? TakeProfit = null);

    internal sealed record OrderResultDto(bool Filled, decimal? Price, int? Volume, long? Ticket, string? Message);

    internal sealed record StopResultDto(bool Ok, string? Message);

    internal sealed record PositionDto(long Ticket, string Symbol, string Side, int Volume, decimal Price, decimal? StopLoss, long Magic);

    internal sealed record ClosingPriceDto(long Ticket, decimal? Price);

    internal sealed record AccountDto(decimal Equity, bool IsDemo);

    internal sealed record DetailDto(JsonElement? Detail);
}

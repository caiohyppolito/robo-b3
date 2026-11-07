using RoboB3.Domain.Common;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Execution;

public enum OrderPurpose
{
    Entry = 1,
    Exit = 2,
}

public enum OrderIntentStatus
{
    /// <summary>Gravada antes do envio; sem resposta ainda.</summary>
    Pending = 1,

    Filled = 2,

    /// <summary>A corretora (ou o conector) recusou.</summary>
    Rejected = 3,

    /// <summary>Sem confirmação; resolvida pela conciliação, nunca reenviada.</summary>
    Unconfirmed = 4,

    /// <summary>Encontrada executada na corretora depois de um reinício.</summary>
    Recovered = 5,
}

/// <summary>
/// Intenção de ordem, gravada ANTES do envio. Se o processo cair no meio, a conciliação do próximo ciclo
/// decide o que aconteceu olhando a corretora, e a ordem nunca é reenviada automaticamente.
/// Guarda também a qualidade da execução: slippage em relação ao preço de referência e latência.
/// </summary>
public sealed class OrderIntent
{
    private OrderIntent()
    {
        ClientOrderId = string.Empty;
        Symbol = string.Empty;
        Reason = string.Empty;
    }

    public OrderIntent(
        ExecutionMode mode,
        string clientOrderId,
        OrderPurpose purpose,
        string symbol,
        OrderSide side,
        int quantity,
        decimal referencePrice,
        decimal? stopLoss,
        Guid? assignmentId,
        Guid? positionId,
        string strategyKey,
        string reason,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Mode = mode;
        ClientOrderId = Guard.NotBlank(clientOrderId, nameof(clientOrderId));
        Purpose = purpose;
        Symbol = Guard.NotBlank(symbol, nameof(symbol));
        Side = side;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        ReferencePrice = referencePrice;
        StopLoss = stopLoss;
        AssignmentId = assignmentId;
        PositionId = positionId;
        StrategyKey = strategyKey;
        Reason = Guard.NotBlank(reason, nameof(reason));
        CreatedAt = createdAt;
        Status = OrderIntentStatus.Pending;
    }

    public Guid Id { get; private set; }

    public ExecutionMode Mode { get; private set; }

    public string ClientOrderId { get; private set; }

    public OrderPurpose Purpose { get; private set; }

    public string Symbol { get; private set; }

    public OrderSide Side { get; private set; }

    public int Quantity { get; private set; }

    public decimal ReferencePrice { get; private set; }

    public decimal? StopLoss { get; private set; }

    public Guid? AssignmentId { get; private set; }

    public Guid? PositionId { get; private set; }

    public string? StrategyKey { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public OrderIntentStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public decimal? FillPrice { get; private set; }

    public int? FillQuantity { get; private set; }

    public long? BrokerTicket { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? Message { get; private set; }

    /// <summary>Quanto o preço executado foi pior (positivo) ou melhor (negativo) que a referência, no sentido da ordem.</summary>
    public decimal? Slippage => FillPrice is { } fill ? (fill - ReferencePrice) * (Side == OrderSide.Buy ? 1 : -1) : null;

    public bool IsOpen => Status == OrderIntentStatus.Pending;

    public void Complete(bool filled, decimal price, int quantity, long? ticket, string message, DateTimeOffset now)
    {
        EnsurePending();
        Status = filled ? OrderIntentStatus.Filled : OrderIntentStatus.Rejected;
        if (filled)
        {
            FillPrice = price;
            FillQuantity = quantity;
            BrokerTicket = ticket;
        }

        Message = message;
        CompletedAt = now;
        LatencyMs = (int)Math.Clamp((now - CreatedAt).TotalMilliseconds, 0, int.MaxValue);
    }

    public void Recover(decimal price, int quantity, long ticket, DateTimeOffset now)
    {
        EnsurePending();
        Status = OrderIntentStatus.Recovered;
        FillPrice = price;
        FillQuantity = quantity;
        BrokerTicket = ticket;
        Message = "Executada na corretora; recuperada após reinício.";
        CompletedAt = now;
    }

    public void MarkUnconfirmed(string message, DateTimeOffset now)
    {
        EnsurePending();
        Status = OrderIntentStatus.Unconfirmed;
        Message = Guard.NotBlank(message, nameof(message));
        CompletedAt = now;
    }

    public void LinkPosition(Guid positionId) => PositionId = positionId;

    private void EnsurePending()
    {
        if (Status != OrderIntentStatus.Pending)
        {
            throw new DomainException("Esta ordem já foi resolvida.");
        }
    }
}

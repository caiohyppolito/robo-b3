using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Paper;

/// <summary>Execução simulada (entrada ou saída), registrada para conferência.</summary>
public sealed class PaperFill
{
    private PaperFill()
    {
        Symbol = string.Empty;
        Reason = string.Empty;
    }

    public PaperFill(Guid positionId, string symbol, OrderSide side, int quantity, decimal price, decimal cost, decimal? realizedProfit, string reason, DateTimeOffset time)
    {
        PositionId = positionId;
        Symbol = symbol;
        Side = side;
        Quantity = quantity;
        Price = price;
        Cost = cost;
        RealizedProfit = realizedProfit;
        Reason = reason;
        Time = time;
    }

    public long Id { get; private set; }

    public Guid PositionId { get; private set; }

    public string Symbol { get; private set; }

    public OrderSide Side { get; private set; }

    public int Quantity { get; private set; }

    public decimal Price { get; private set; }

    public decimal Cost { get; private set; }

    /// <summary>Resultado líquido da operação, nas saídas.</summary>
    public decimal? RealizedProfit { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset Time { get; private set; }
}

using RoboB3.Domain.Common;
using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Trading;

/// <summary>Intenção de ordem, antes de passar pelo gestor de risco.</summary>
public sealed record OrderRequest
{
    public OrderRequest(string symbol, AssetClass assetClass, OrderSide side, int quantity, decimal price)
    {
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        AssetClass = assetClass;
        Side = side;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        Price = Guard.Positive(price, nameof(price));
    }

    public string Symbol { get; }

    public AssetClass AssetClass { get; }

    public OrderSide Side { get; }

    public int Quantity { get; }

    public decimal Price { get; }

    public decimal Notional => Quantity * Price;

    public int SignedQuantity => Side == OrderSide.Buy ? Quantity : -Quantity;
}

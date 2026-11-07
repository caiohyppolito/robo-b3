namespace RoboB3.Domain.MarketData;

/// <summary>Cotação atual de um símbolo da corretora (ex.: PETR4, WINZ26).</summary>
public sealed record Quote(string Symbol, decimal Bid, decimal Ask, decimal Last, DateTimeOffset Time)
{
    /// <summary>Preço para comprar a mercado (ask) ou vender (bid); cai para o último negócio se o livro estiver vazio.</summary>
    public decimal PriceFor(bool isBuy)
    {
        var price = isBuy ? Ask : Bid;
        return price > 0 ? price : Last;
    }

    public bool IsFresh(DateTimeOffset now, TimeSpan maxAge) => now - Time <= maxAge && Last > 0;
}

using RoboB3.Domain.Common;

namespace RoboB3.Domain.MarketData;

/// <summary>Barra OHLCV de um ativo em um timeframe.</summary>
public sealed class Candle
{
    private Candle()
    {
        Symbol = string.Empty;
    }

    public Candle(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume)
    {
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        Timeframe = timeframe;
        OpenTime = openTime.ToUniversalTime();
        SetPrices(open, high, low, close, volume);
    }

    public long Id { get; private set; }

    public string Symbol { get; private set; }

    public Timeframe Timeframe { get; private set; }

    /// <summary>Início da barra em UTC.</summary>
    public DateTimeOffset OpenTime { get; private set; }

    public decimal Open { get; private set; }

    public decimal High { get; private set; }

    public decimal Low { get; private set; }

    public decimal Close { get; private set; }

    public long Volume { get; private set; }

    /// <summary>Atualiza os preços de uma barra ainda em formação ou corrigida pelo provedor.</summary>
    public void UpdateFrom(Candle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Symbol != Symbol || other.Timeframe != Timeframe || other.OpenTime != OpenTime)
        {
            throw new DomainException("Só é possível atualizar um candle com a mesma chave.");
        }

        SetPrices(other.Open, other.High, other.Low, other.Close, other.Volume);
    }

    private void SetPrices(decimal open, decimal high, decimal low, decimal close, long volume)
    {
        Guard.Positive(open, nameof(open));
        Guard.Positive(high, nameof(high));
        Guard.Positive(low, nameof(low));
        Guard.Positive(close, nameof(close));

        if (high < Math.Max(open, close) || low > Math.Min(open, close) || low > high)
        {
            throw new DomainException($"Candle inconsistente para {Symbol}: O={open} H={high} L={low} C={close}.");
        }

        if (volume < 0)
        {
            throw new DomainException("Volume não pode ser negativo.");
        }

        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}

namespace RoboB3.Domain.MarketData;

/// <summary>CDI de um dia útil, em % ao dia (série 12 do SGS do Banco Central).</summary>
public sealed class CdiRate
{
    private CdiRate()
    {
    }

    public CdiRate(DateOnly date, decimal dailyPercent)
    {
        if (dailyPercent < 0 || dailyPercent > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyPercent), "CDI diário fora do intervalo plausível (0% a 1% ao dia).");
        }

        Date = date;
        DailyPercent = dailyPercent;
    }

    public DateOnly Date { get; private set; }

    public decimal DailyPercent { get; private set; }

    /// <summary>Equivalente anual em 252 dias úteis.</summary>
    public decimal AnnualizedPercent => Math.Round(((decimal)Math.Pow(1 + ((double)DailyPercent / 100), 252) - 1) * 100, 2);
}

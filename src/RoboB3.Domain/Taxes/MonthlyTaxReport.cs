namespace RoboB3.Domain.Taxes;

/// <summary>Operação encerrada para a apuração mensal.</summary>
/// <param name="ExitDate">Data (Brasília) da operação que encerrou a posição.</param>
/// <param name="SaleAmount">Valor de alienação (venda) que conta para o limite de isenção.</param>
public sealed record TaxableTrade(DateOnly ExitDate, TaxCategory Category, decimal NetProfit, decimal SaleAmount);

public sealed record TaxMonth(
    int Year,
    int Month,
    decimal StockSales,
    bool StockExempt,
    decimal ExemptProfit,
    decimal SwingResult,
    decimal DayTradeResult,
    decimal SwingLossUsed,
    decimal DayTradeLossUsed,
    decimal SwingLossCarry,
    decimal DayTradeLossCarry,
    decimal SwingTax,
    decimal DayTradeTax,
    decimal IrrfWithheld,
    decimal IrrfDeducted,
    decimal IrrfCarry,
    decimal TaxDue,
    decimal CarriedFromPrevious,
    decimal DarfAmount,
    DateOnly? DarfDueDate,
    int Trades);

/// <summary>
/// Apuração mensal de renda variável das operações do robô, regra a regra:
/// <list type="bullet">
/// <item>15% em operações comuns (Lei 11.033/2004 art. 2º II); 20% em day trade (IN RFB 1.585/2015).</item>
/// <item>Isenção do ganho em AÇÕES no mercado à vista quando as vendas de ações do mês somam até R$ 20 mil
/// (Lei 11.033/2004 art. 3º I). Units, ETFs, futuros e day trade ficam de fora.</item>
/// <item>Prejuízo compensa só na mesma modalidade (comum com comum, day trade com day trade), sem prazo;
/// prejuízo em ações de mês isento continua compensável.</item>
/// <item>IRRF estimado: 0,005% das vendas em operações comuns e 1% do resultado positivo do dia em day trade,
/// dispensado se ≤ R$ 1; deduz do imposto do mês e o saldo passa aos meses seguintes do mesmo ano (o de
/// dezembro vai para a declaração anual). Vale o que a corretora reteve na nota.</item>
/// <item>Imposto truncado nos centavos; DARF código 6015; abaixo de R$ 10 acumula (Lei 9.430/1996 art. 68 §1º);
/// vence no último dia útil do mês seguinte.</item>
/// </list>
/// Regras conferidas contra o COIR (AGPL; só as regras e fontes legais, sem código). Não substitui um contador.
/// </summary>
public static class MonthlyTaxReport
{
    public const string DarfCode = "6015";
    public const decimal SwingIrrfRate = 0.00005m;
    public const decimal DayTradeIrrfRate = 0.01m;
    public const decimal IrrfMinimum = 1m;

    public static IReadOnlyList<TaxMonth> Build(IEnumerable<TaxableTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var list = trades.OrderBy(t => t.ExitDate).ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var first = new DateOnly(list[0].ExitDate.Year, list[0].ExitDate.Month, 1);
        var last = new DateOnly(list[^1].ExitDate.Year, list[^1].ExitDate.Month, 1);
        var months = new List<TaxMonth>();
        decimal swingCarry = 0, dayTradeCarry = 0, irrfCarry = 0, pending = 0;

        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            if (month.Month == 1)
            {
                // IRRF não usado até dezembro vai para a declaração anual; não passa de um ano para o outro.
                irrfCarry = 0;
            }

            var inMonth = list.Where(t => t.ExitDate.Year == month.Year && t.ExitDate.Month == month.Month).ToList();
            var stock = inMonth.Where(t => t.Category == TaxCategory.StockSwing).ToList();
            var stockSales = stock.Sum(t => t.SaleAmount);
            var stockResult = stock.Sum(t => t.NetProfit);
            var exempt = stockSales <= BrazilianTaxCalculator.StockExemptionLimit && stockResult > 0;
            var exemptProfit = exempt ? stockResult : 0m;

            var swingResult = (exempt ? 0m : stockResult) + inMonth.Where(t => t.Category == TaxCategory.OtherSwing).Sum(t => t.NetProfit);
            var dayTradeResult = inMonth.Where(t => t.Category == TaxCategory.DayTrade).Sum(t => t.NetProfit);

            var (swingBase, swingUsed, newSwingCarry) = Compensate(swingResult, swingCarry);
            var (dayBase, dayUsed, newDayCarry) = Compensate(dayTradeResult, dayTradeCarry);
            swingCarry = newSwingCarry;
            dayTradeCarry = newDayCarry;

            var swingTax = Truncate(swingBase * BrazilianTaxCalculator.SwingRate);
            var dayTax = Truncate(dayBase * BrazilianTaxCalculator.DayTradeRate);

            var irrf = Irrf(inMonth);
            var available = irrfCarry + irrf;
            var gross = swingTax + dayTax;
            var deducted = Math.Min(available, gross);
            irrfCarry = available - deducted;
            var due = gross - deducted;

            var total = due + pending;
            var carriedIn = pending;
            decimal darf;
            DateOnly? dueDate = null;
            if (total >= BrazilianTaxCalculator.MinimumDarf)
            {
                darf = total;
                pending = 0;
                dueDate = DarfDueDate(month.Year, month.Month);
            }
            else
            {
                darf = 0;
                pending = total;
            }

            months.Add(new TaxMonth(month.Year, month.Month, Round(stockSales), exempt, Round(exemptProfit), Round(swingResult), Round(dayTradeResult),
                Round(swingUsed), Round(dayUsed), Round(swingCarry), Round(dayTradeCarry), swingTax, dayTax, Round(irrf), Round(deducted), Round(irrfCarry),
                Round(due), Round(carriedIn), Round(darf), dueDate, inMonth.Count));
        }

        return months;
    }

    /// <summary>IRRF estimado do mês: 0,005% das vendas comuns e 1% do lucro de cada dia de day trade, se passar de R$ 1.</summary>
    internal static decimal Irrf(IEnumerable<TaxableTrade> trades)
    {
        var list = trades.ToList();
        decimal total = 0;
        foreach (var day in list.GroupBy(t => t.ExitDate))
        {
            var swing = day.Where(t => t.Category != TaxCategory.DayTrade).Sum(t => t.SaleAmount) * SwingIrrfRate;
            var dayTradeProfit = day.Where(t => t.Category == TaxCategory.DayTrade).Sum(t => t.NetProfit);
            var dayTrade = dayTradeProfit > 0 ? dayTradeProfit * DayTradeIrrfRate : 0m;
            total += (swing > IrrfMinimum ? Truncate(swing) : 0m) + (dayTrade > IrrfMinimum ? Truncate(dayTrade) : 0m);
        }

        return total;
    }

    /// <summary>Último dia útil do mês seguinte ao da apuração.</summary>
    public static DateOnly DarfDueDate(int year, int month)
    {
        var next = new DateOnly(year, month, 1).AddMonths(1);
        var day = next.AddMonths(1).AddDays(-1);
        while (!BrazilianHolidays.IsBusinessDay(day))
        {
            day = day.AddDays(-1);
        }

        return day;
    }

    private static (decimal Base, decimal Used, decimal Carry) Compensate(decimal result, decimal carry)
    {
        if (result <= 0)
        {
            return (0, 0, carry - result);
        }

        var used = Math.Min(carry, result);
        return (result - used, used, carry - used);
    }

    private static decimal Truncate(decimal value) => Math.Truncate(value * 100m) / 100m;

    private static decimal Round(decimal value) => Math.Round(value, 2);
}

/// <summary>Feriados nacionais (fixos e móveis, a partir da Páscoa) e fins de semana.</summary>
public static class BrazilianHolidays
{
    public static bool IsBusinessDay(DateOnly day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(day);

    public static bool IsHoliday(DateOnly day)
    {
        var fixedDays = new[] { (1, 1), (4, 21), (5, 1), (9, 7), (10, 12), (11, 2), (11, 15), (12, 25) };
        if (fixedDays.Contains((day.Month, day.Day)) || (day.Year >= 2024 && day.Month == 11 && day.Day == 20))
        {
            return true;
        }

        var easter = Easter(day.Year);
        return day == easter.AddDays(-48) || day == easter.AddDays(-47) || day == easter.AddDays(-2) || day == easter.AddDays(60);
    }

    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher).</summary>
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var dayOfMonth = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, dayOfMonth);
    }
}

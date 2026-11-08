using System.Text.RegularExpressions;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Execution;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Taxes;

namespace RoboB3.Application.Paper;

public sealed record TaxReportResponse(ExecutionMode Mode, int Year, IReadOnlyList<TaxMonth> Months, decimal TotalDarf, decimal TotalExemptProfit, string DarfCode);

/// <summary>Apuração mensal de IR das operações encerradas pelo robô num modo de execução.</summary>
public sealed partial class TaxReportService(TradeHistoryService history, IAssetRepository assets)
{
    public async Task<TaxReportResponse> ReportAsync(ExecutionMode mode, int year, CancellationToken cancellationToken)
    {
        var trades = (await history.ListAsync(mode, null, 1000, cancellationToken)).Trades;
        var specs = (await assets.ListAsync(onlyActive: false, cancellationToken)).ToDictionary(a => a.Symbol, a => (Asset: a, Spec: InstrumentSpec.For(a)));

        var taxable = new List<TaxableTrade>();
        foreach (var t in trades)
        {
            var root = ContractSuffix().Replace(t.Symbol, string.Empty);
            if (!specs.TryGetValue(t.Symbol, out var info) && !specs.TryGetValue(root, out info))
            {
                continue;
            }

            var exitDate = B3Calendar.TradingDate(t.ExitTime);
            var dayTrade = t.EntryTime is { } entry && B3Calendar.TradingDate(entry) == exitDate;
            var category = dayTrade ? TaxCategory.DayTrade
                : info.Asset.AssetClass == AssetClass.Stock && !IsUnit(info.Asset.Symbol) ? TaxCategory.StockSwing
                : TaxCategory.OtherSwing;

            // Alienação: na compra, a venda é a saída; na venda a descoberto, é a entrada.
            var salePrice = t.Direction == "Compra" ? t.ExitPrice : t.EntryPrice ?? t.ExitPrice;
            var sale = salePrice * t.Quantity * info.Spec.Multiplier;
            taxable.Add(new TaxableTrade(exitDate, category, t.NetProfit, Math.Round(sale, 2)));
        }

        // Apura desde a primeira operação (prejuízos de anos anteriores compensam), mostra só o ano pedido.
        var months = MonthlyTaxReport.Build(taxable).Where(m => m.Year == year).ToList();
        return new TaxReportResponse(mode, year, months, months.Sum(m => m.DarfAmount), months.Sum(m => m.ExemptProfit), MonthlyTaxReport.DarfCode);
    }

    /// <summary>Units (ex.: BPAC11, TAEE11) não têm a isenção de ações.</summary>
    private static bool IsUnit(string symbol) => symbol.EndsWith("11", StringComparison.Ordinal);

    [GeneratedRegex("[FGHJKMNQUVXZ]\\d{2}$")]
    private static partial Regex ContractSuffix();
}

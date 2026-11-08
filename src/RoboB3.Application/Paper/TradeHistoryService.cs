using RoboB3.Application.Abstractions;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Paper;

/// <summary>Uma operação encerrada: a execução de entrada e a de saída da mesma posição.</summary>
public sealed record ClosedTrade(
    Guid PositionId,
    ExecutionMode Mode,
    string Symbol,
    string? Strategy,
    string Direction,
    int Quantity,
    DateTimeOffset? EntryTime,
    decimal? EntryPrice,
    DateTimeOffset ExitTime,
    decimal ExitPrice,
    string ExitReason,
    decimal Costs,
    decimal NetProfit);

public sealed record TradeHistory(IReadOnlyList<ClosedTrade> Trades, int Wins, decimal NetProfit, decimal AverageProfit, decimal WinRatePercent, decimal? ProfitFactor);

/// <summary>Histórico das operações do robô em todos os modos (simulado, MT5 demo e real), montado das execuções gravadas.</summary>
public sealed class TradeHistoryService(IPaperRepository paper)
{
    public async Task<TradeHistory> ListAsync(ExecutionMode? mode, string? symbol, int count, CancellationToken cancellationToken)
    {
        var modes = mode is { } m ? [m] : Enum.GetValues<ExecutionMode>();
        var fills = new List<PaperFill>();
        foreach (var each in modes)
        {
            fills.AddRange(await paper.ListRecentFillsAsync(each, 500, cancellationToken));
        }

        var trades = fills
            .GroupBy(f => f.PositionId)
            .Select(Build)
            .OfType<ClosedTrade>()
            .Where(t => string.IsNullOrWhiteSpace(symbol) || t.Symbol.StartsWith(symbol.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.ExitTime)
            .Take(Math.Clamp(count, 1, 1000))
            .ToList();

        var wins = trades.Count(t => t.NetProfit > 0);
        var gains = trades.Where(t => t.NetProfit > 0).Sum(t => t.NetProfit);
        var losses = -trades.Where(t => t.NetProfit < 0).Sum(t => t.NetProfit);
        var net = trades.Sum(t => t.NetProfit);
        return new TradeHistory(
            trades,
            wins,
            net,
            trades.Count == 0 ? 0 : Math.Round(net / trades.Count, 2),
            trades.Count == 0 ? 0 : Math.Round(100m * wins / trades.Count, 1),
            losses > 0 ? Math.Round(gains / losses, 2) : null);
    }

    private static ClosedTrade? Build(IGrouping<Guid, PaperFill> group)
    {
        var exit = group.Where(f => f.RealizedProfit is not null).OrderByDescending(f => f.Time).FirstOrDefault();
        if (exit is null)
        {
            return null;
        }

        var entry = group.Where(f => f.RealizedProfit is null).OrderBy(f => f.Time).FirstOrDefault();
        var long_ = entry is not null ? entry.Side == OrderSide.Buy : exit.Side == OrderSide.Sell;
        return new ClosedTrade(
            group.Key,
            exit.Mode,
            exit.Symbol,
            exit.StrategyKey ?? entry?.StrategyKey,
            long_ ? "Compra" : "Venda",
            exit.Quantity,
            entry?.Time,
            entry?.Price,
            exit.Time,
            exit.Price,
            exit.Reason,
            group.Sum(f => f.Cost),
            exit.RealizedProfit!.Value);
    }
}

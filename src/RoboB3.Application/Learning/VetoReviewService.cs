using RoboB3.Application.Abstractions;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;

namespace RoboB3.Application.Learning;

/// <summary>O que teria acontecido com uma entrada vetada. É simulação: a operação não existiu.</summary>
public sealed record VetoOutcome(
    long DecisionId,
    DateTimeOffset Time,
    string Symbol,
    string Strategy,
    string Direction,
    double? Probability,
    string Status,
    DateOnly? EntryDate,
    DateOnly? ExitDate,
    decimal? ReturnPercent,
    string? ExitReason);

public sealed record VetoReview(
    int Vetoes,
    int Evaluated,
    int WouldHaveWon,
    int WouldHaveLost,
    decimal AvoidedLossPercent,
    decimal MissedGainPercent,
    IReadOnlyList<VetoOutcome> Items)
{
    /// <summary>Vetos certos (teria perdido) sobre os avaliados.</summary>
    public decimal? HitRatePercent => Evaluated == 0 ? null : Math.Round(100m * WouldHaveLost / Evaluated, 1);
}

/// <summary>
/// Contrafactual dos vetos da IA: para cada entrada vetada em barras diárias, roda a mesma estratégia com os mesmos
/// parâmetros no histórico (sem filtro, mesmo motor e custos) e vê como teria terminado a operação daquele sinal.
/// Só informa; não muda modelo, limite nem trava.
/// </summary>
public sealed class VetoReviewService(IPaperRepository paper, LearningService learning, Microsoft.Extensions.Options.IOptions<Backtesting.BacktestOptions> options)
{
    public async Task<VetoReview> ReviewAsync(int count, CancellationToken cancellationToken)
    {
        var vetoes = await paper.ListRecentVetoesAsync(Math.Clamp(count, 1, 500), cancellationToken);
        if (vetoes.Count == 0)
        {
            return new VetoReview(0, 0, 0, 0, 0, 0, []);
        }

        var series = (await learning.LoadSeriesAsync(cancellationToken)).ToDictionary(s => s.Instrument.Symbol);
        var settings = new BacktestSettings(100_000m, 1m, new RiskLimits(100m, 100m, 100m, 5, 2), options.Value.Costs(), 0m, applyTaxes: false, earnRiskFreeOnCash: false);
        var runs = new Dictionary<string, BacktestResult?>();
        var items = new List<VetoOutcome>();

        foreach (var veto in vetoes)
        {
            var direction = veto.Signal == PositionDirection.Long ? "Compra" : "Venda";
            if (!series.TryGetValue(veto.Symbol, out var asset))
            {
                items.Add(Pending(veto, direction, "sem histórico diário deste ativo"));
                continue;
            }

            var key = $"{veto.Symbol}|{veto.StrategyKey}|{string.Join(',', veto.Parameters.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))}";
            if (!runs.TryGetValue(key, out var result))
            {
                result = Run(veto, asset, settings);
                runs[key] = result;
            }

            var signalDate = DateOnly.FromDateTime(veto.BarTime.UtcDateTime);
            var trade = result?.Trades.FirstOrDefault(t => t.EntryDate > signalDate && t.Direction == veto.Signal);
            if (trade is null || trade.EntryDate > signalDate.AddDays(7))
            {
                items.Add(Pending(veto, direction, "a operação não aparece no histórico (dados ainda não baixados?)"));
                continue;
            }

            if (trade.ExitReason == "fim do período")
            {
                items.Add(Pending(veto, direction, "ainda estaria aberta"));
                continue;
            }

            items.Add(new VetoOutcome(veto.Id, veto.Time, veto.Symbol, veto.StrategyKey, direction, veto.Probability,
                trade.NetProfit > 0 ? "teria ganhado" : "teria perdido", trade.EntryDate, trade.ExitDate, trade.ReturnPercent, trade.ExitReason));
        }

        var evaluated = items.Where(i => i.ReturnPercent is not null).ToList();
        return new VetoReview(
            vetoes.Count,
            evaluated.Count,
            evaluated.Count(i => i.ReturnPercent > 0),
            evaluated.Count(i => i.ReturnPercent <= 0),
            -evaluated.Where(i => i.ReturnPercent < 0).Sum(i => i.ReturnPercent!.Value),
            evaluated.Where(i => i.ReturnPercent > 0).Sum(i => i.ReturnPercent!.Value),
            items);
    }

    private static BacktestResult? Run(TradingDecision veto, AssetSeries asset, BacktestSettings settings)
    {
        try
        {
            var definition = StrategyCatalog.Get(veto.StrategyKey);
            var parameters = definition.Normalize(veto.Parameters);
            return BacktestEngine.Run(asset.Bars, definition.Create(parameters), parameters, asset.Instrument, settings);
        }
        catch (Domain.Common.DomainException)
        {
            return null;
        }
    }

    private static VetoOutcome Pending(TradingDecision veto, string direction, string status) =>
        new(veto.Id, veto.Time, veto.Symbol, veto.StrategyKey, direction, veto.Probability, status, null, null, null, null);
}

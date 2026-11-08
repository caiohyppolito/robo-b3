using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Strategies;

namespace RoboB3.Application.Backtesting;

public sealed record ScanRequest(DateOnly? From, DateOnly? To, decimal MinDailyVolumeMillions = 50m, decimal InSamplePercent = 70m, int Folds = 1);

/// <summary>Um ativo na varredura: liquidez e a estratégia escolhida DENTRO da amostra, com o resultado FORA dela.</summary>
public sealed record ScanItem(
    string Symbol,
    string Name,
    decimal AverageDailyVolumeMillions,
    bool Liquid,
    string? Strategy,
    IReadOnlyDictionary<string, decimal>? Parameters,
    decimal? InSampleSharpe,
    decimal? OutOfSampleSharpe,
    decimal? OutOfSampleReturnPercent,
    decimal? OutOfSampleMaxDrawdownPercent,
    int? OutOfSampleTrades,
    decimal? BuyAndHoldReturnPercent,
    decimal? RiskFreeReturnPercent,
    string Verdict,
    string Reason);

public sealed record ScanResult(
    DateOnly? OutOfSampleFrom,
    DateOnly? OutOfSampleTo,
    int TrialsEvaluated,
    IReadOnlyList<ScanItem> Items,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Varredura de ativos: filtra por liquidez e roda a comparação fora da amostra em cada ativo com histórico.
/// A estratégia de cada ativo é escolhida pelo Sharpe DENTRO da amostra (escolher olhando o fora da amostra
/// seria trapaça); o veredito usa só o fora da amostra, contra o CDI e o comprar e segurar.
/// É triagem: um candidato ainda precisa de período novo, sensibilidade e demo (ver docs/diagnostico.md).
/// </summary>
public sealed class ScannerService(
    IAssetRepository assets,
    ICandleRepository candles,
    StrategyComparisonService comparison,
    TimeProvider clock)
{
    public const int MinTradesForCandidate = 10;

    public async Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var universe = (await assets.ListAsync(onlyActive: true, cancellationToken))
            .Where(a => a.DailyDataSymbol is not null && a.AssetClass is AssetClass.Stock or AssetClass.Etf)
            .ToList();

        var now = clock.GetUtcNow();
        var items = new List<ScanItem>();
        var liquid = new List<string>();
        foreach (var asset in universe)
        {
            var recent = (await candles.ListAsync(asset.Symbol, Timeframe.D1, now.AddDays(-100), now, cancellationToken)).TakeLast(60).ToList();
            var volume = recent.Count == 0 ? 0m : recent.Average(c => c.Close * c.Volume) / 1_000_000m;
            if (recent.Count < 20)
            {
                items.Add(new ScanItem(asset.Symbol, asset.Name, Math.Round(volume, 1), false, null, null, null, null, null, null, null, null, null,
                    "sem dados", "Histórico diário ainda não baixado (rode a coleta)."));
            }
            else if (volume < request.MinDailyVolumeMillions)
            {
                items.Add(new ScanItem(asset.Symbol, asset.Name, Math.Round(volume, 1), false, null, null, null, null, null, null, null, null, null,
                    "ilíquido", $"Volume médio de R$ {volume:N1} mi/dia abaixo do mínimo de R$ {request.MinDailyVolumeMillions:N0} mi."));
            }
            else
            {
                liquid.Add(asset.Symbol);
            }
        }

        if (liquid.Count == 0)
        {
            return new ScanResult(null, null, 0, Order(items), []);
        }

        var result = await comparison.CompareAsync(
            new ComparisonRequest(liquid, request.From, request.To, new SimulationSettings(), request.InSamplePercent, Folds: request.Folds), cancellationToken);
        var names = universe.ToDictionary(a => a.Symbol, a => a.Name);
        foreach (var symbol in liquid)
        {
            var rows = result.Rows.Where(r => r.Symbol == symbol && r.StrategyKey != StrategyKeys.BuyAndHold).ToList();
            var adv = await AverageVolumeAsync(symbol, now, cancellationToken);
            if (rows.Count == 0)
            {
                items.Add(new ScanItem(symbol, names[symbol], adv, true, null, null, null, null, null, null, null, null, null,
                    "sem dados", "Histórico insuficiente para a comparação."));
                continue;
            }

            var chosen = rows.OrderByDescending(r => r.InSampleSharpe).First();
            var beatsCdi = chosen.OutOfSampleReturnPercent > chosen.OutOfSampleRiskFreeReturnPercent;
            var beatsHold = chosen.OutOfSampleReturnPercent > chosen.OutOfSampleBenchmarkReturnPercent;
            var consistent = chosen.Folds <= 1 || chosen.FoldsBeatingRiskFree * 2 > chosen.Folds;
            var edge = chosen.OutOfSampleTrades < 2 || chosen.ExpectancyPerTrade - chosen.ExpectancyMargin95 > 0;
            var (verdict, reason) = (beatsCdi, beatsHold, chosen.OutOfSampleTrades >= MinTradesForCandidate) switch
            {
                (true, true, true) when !consistent => ("observar", $"Venceu no total, mas só em {chosen.FoldsBeatingRiskFree} de {chosen.Folds} janelas: depende do período."),
                (true, true, true) when !edge => ("observar", $"Venceu, mas a margem de 95% (± R$ {chosen.ExpectancyMargin95:N0}) engole a média por operação (R$ {chosen.ExpectancyPerTrade:N0}): pode ser acaso."),
                (true, true, true) => ("candidato", "Venceu o CDI e o comprar e segurar fora da amostra. Confirme com período novo e na demo."),
                (true, true, false) => ("observar", $"Venceu os dois, mas com só {chosen.OutOfSampleTrades} operações: pouco para confiar."),
                (true, false, _) => ("observar", "Venceu o CDI, mas não o comprar e segurar."),
                _ => ("descartar", "Não venceu o CDI fora da amostra."),
            };

            items.Add(new ScanItem(symbol, names[symbol], adv, true, chosen.StrategyKey, chosen.Parameters, chosen.InSampleSharpe,
                chosen.OutOfSampleSharpe, chosen.OutOfSampleReturnPercent, chosen.OutOfSampleMaxDrawdownPercent, chosen.OutOfSampleTrades,
                chosen.OutOfSampleBenchmarkReturnPercent, chosen.OutOfSampleRiskFreeReturnPercent, verdict, reason));
        }

        return new ScanResult(result.OutOfSampleFrom, result.OutOfSampleTo, result.TrialsEvaluated, Order(items), result.Skipped);
    }

    private async Task<decimal> AverageVolumeAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recent = (await candles.ListAsync(symbol, Timeframe.D1, now.AddDays(-100), now, cancellationToken)).TakeLast(60).ToList();
        return recent.Count == 0 ? 0 : Math.Round(recent.Average(c => c.Close * c.Volume) / 1_000_000m, 1);
    }

    private static readonly Dictionary<string, int> Rank = new() { ["candidato"] = 0, ["observar"] = 1, ["descartar"] = 2, ["ilíquido"] = 3, ["sem dados"] = 4 };

    private static List<ScanItem> Order(IEnumerable<ScanItem> items) =>
        items.OrderBy(i => Rank.GetValueOrDefault(i.Verdict, 9)).ThenByDescending(i => i.OutOfSampleSharpe ?? decimal.MinValue).ThenByDescending(i => i.AverageDailyVolumeMillions).ToList();

    /// <summary>Liga (ou desliga) todos os ativos do universo amplo para a coleta diária e a varredura.</summary>
    public async Task<int> SetUniverseAsync(bool active, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        var changed = 0;
        foreach (var asset in await assets.ListAsync(onlyActive: false, cancellationToken))
        {
            if (!AssetUniverse.Symbols.Contains(asset.Symbol) || asset.IsActive == active)
            {
                continue;
            }

            if (active)
            {
                asset.Activate();
            }
            else
            {
                asset.Deactivate();
            }

            changed++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

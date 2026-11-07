using Microsoft.Extensions.Logging;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.Paper;

/// <summary>
/// Grava barras intradiárias vindas do MT5 (futuros em M5 e os tempos gráficos das alocações),
/// para backtest de day trade e para o aquecimento dos indicadores.
/// </summary>
public sealed partial class IntradayIngestionService(
    IMarketDataFeed feed,
    IAssetRepository assets,
    ICandleRepository candles,
    IStrategyAssignmentRepository assignments,
    ILogger<IntradayIngestionService> logger)
{
    public const int InitialBars = 20_000;
    public const int IncrementalBars = 600;

    public async Task<int> IngestAsync(CancellationToken cancellationToken)
    {
        if (!(await feed.GetStatusAsync(cancellationToken)).Connected)
        {
            return 0;
        }

        var all = (await assets.ListAsync(onlyActive: true, cancellationToken)).ToDictionary(a => a.Symbol);
        var targets = all.Values.Where(a => a.IsFuture).Select(a => (a.Symbol, Timeframe.M5))
            .Concat((await assignments.ListAsync(cancellationToken)).Where(a => a.Timeframe != Timeframe.D1).Select(a => (a.Symbol, a.Timeframe)))
            .Distinct();

        var saved = 0;
        foreach (var (symbol, timeframe) in targets)
        {
            if (!all.TryGetValue(symbol, out var asset))
            {
                continue;
            }

            try
            {
                var last = await candles.GetLastOpenTimeAsync(symbol, timeframe, cancellationToken);
                var history = await feed.GetHistoryAsync(asset, timeframe, last is null ? InitialBars : IncrementalBars, cancellationToken);
                saved += await candles.UpsertAsync(history, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                LogFailed(symbol, timeframe, ex.Message);
            }
        }

        return saved;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao gravar barras {Timeframe} de {Symbol}: {Reason}")]
    private partial void LogFailed(string symbol, Timeframe timeframe, string reason);
}

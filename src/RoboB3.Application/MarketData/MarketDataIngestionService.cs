using Microsoft.Extensions.Logging;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.MarketData;

public sealed record IngestionResult(int AssetsProcessed, int CandlesSaved, IReadOnlyList<string> Failures);

/// <summary>Baixa candles diários de todos os ativos ativos e grava de forma incremental.</summary>
public sealed partial class MarketDataIngestionService(
    IAssetRepository assets,
    ICandleRepository candles,
    IMarketDataProvider provider,
    TimeProvider clock,
    ILogger<MarketDataIngestionService> logger)
{
    /// <summary>Histórico baixado na primeira carga de um ativo.</summary>
    public const int InitialHistoryYears = 5;

    /// <summary>Dias re-baixados para capturar correções do provedor.</summary>
    public const int OverlapDays = 5;

    public async Task<IngestionResult> IngestDailyAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var failures = new List<string>();
        var processed = 0;
        var saved = 0;

        foreach (var asset in await assets.ListAsync(onlyActive: true, cancellationToken))
        {
            if (asset.DailyDataSymbol is null)
            {
                continue;
            }

            try
            {
                var last = await candles.GetLastOpenTimeAsync(asset.Symbol, Timeframe.D1, cancellationToken);
                var from = last is null
                    ? today.AddYears(-InitialHistoryYears)
                    : DateOnly.FromDateTime(last.Value.UtcDateTime).AddDays(-OverlapDays);

                var downloaded = await provider.GetDailyCandlesAsync(asset, from, today, cancellationToken);
                saved += await candles.UpsertAsync(downloaded, cancellationToken);
                processed++;
                LogAssetIngested(asset.Symbol, downloaded.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Um ativo com problema não pode impedir a coleta dos demais.
                failures.Add($"{asset.Symbol}: {ex.Message}");
                LogAssetFailed(ex, asset.Symbol);
            }
        }

        return new IngestionResult(processed, saved, failures);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Candles diários de {Symbol}: {Count} recebidos")]
    private partial void LogAssetIngested(string symbol, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao coletar dados de {Symbol}")]
    private partial void LogAssetFailed(Exception exception, string symbol);
}

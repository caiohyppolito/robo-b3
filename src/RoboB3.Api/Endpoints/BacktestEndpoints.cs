using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Strategies;

namespace RoboB3.Api.Endpoints;

public sealed record ParameterDto(string Name, string Description, decimal Default, decimal Min, decimal Max);

public sealed record StrategyDto(string Key, string Name, string Description, IReadOnlyList<ParameterDto> Parameters)
{
    public static StrategyDto From(StrategyDefinition d) => new(
        d.Key,
        d.Name,
        d.Description,
        d.Parameters.Select(p => new ParameterDto(p.Name, p.Description, p.Default, p.Min, p.Max)).ToList());
}

public sealed record SimulationDto(
    decimal? InitialCapital,
    decimal? RiskPerTradePercent,
    decimal? MaxPositionPercent,
    decimal? RiskFreeRatePercent,
    bool? ApplyTaxes)
{
    public SimulationSettings ToSettings()
    {
        var defaults = new SimulationSettings();
        return new SimulationSettings(
            InitialCapital ?? defaults.InitialCapital,
            RiskPerTradePercent ?? defaults.RiskPerTradePercent,
            MaxPositionPercent ?? defaults.MaxPositionPercent,
            RiskFreeRatePercent,
            ApplyTaxes ?? defaults.ApplyTaxes);
    }
}

public sealed record RunBacktestDto(
    string Symbol,
    string Strategy,
    Dictionary<string, decimal>? Parameters,
    DateOnly? From,
    DateOnly? To,
    SimulationDto? Simulation,
    bool? UseMetaModel,
    Domain.MarketData.Timeframe? Timeframe,
    bool? FlattenAtDayEnd);

public sealed record CompareDto(
    List<string>? Symbols,
    DateOnly? From,
    DateOnly? To,
    decimal? InSamplePercent,
    SimulationDto? Simulation);

public sealed record BacktestSummaryDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Symbol,
    string Strategy,
    IReadOnlyDictionary<string, decimal> Parameters,
    DateOnly StartDate,
    DateOnly EndDate,
    PerformanceMetrics Metrics,
    Guid? MetaModelId)
{
    public static BacktestSummaryDto From(BacktestRun r) =>
        new(r.Id, r.CreatedAt, r.Symbol, r.StrategyKey, r.Parameters, r.From, r.To, r.Metrics, r.MetaModelId);
}

public sealed record BacktestDetailDto(
    BacktestSummaryDto Summary,
    decimal RiskPerTradePercent,
    bool ApplyTaxes,
    IReadOnlyList<BacktestTrade> Trades,
    IReadOnlyList<EquityPoint> Equity,
    IReadOnlyList<BacktestEvent> Events)
{
    public static BacktestDetailDto From(BacktestRun r) =>
        new(BacktestSummaryDto.From(r), r.RiskPerTradePercent, r.ApplyTaxes, r.Trades, r.Equity, r.Events);
}

internal static class BacktestEndpoints
{
    public static IEndpointRouteBuilder MapBacktestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/strategies", () => StrategyCatalog.All.Select(StrategyDto.From)).WithTags("Backtest");

        var group = app.MapGroup("/api/backtests").WithTags("Backtest");

        group.MapPost("/scan", (ScanRequest request, ScannerService scanner, CancellationToken ct) => scanner.ScanAsync(request, ct));

        group.MapPost("/scan/universe", async (UniverseRequest request, ScannerService scanner, IUnitOfWork unitOfWork, CancellationToken ct) =>
            new { Changed = await scanner.SetUniverseAsync(request.Active, unitOfWork, ct), Universe = Domain.MarketData.AssetUniverse.Liquid.Count });

        group.MapPost("/ingest", async (Application.MarketData.MarketDataIngestionService ingestion, CancellationToken ct) =>
        {
            var result = await ingestion.IngestDailyAsync(ct);
            return new { result.AssetsProcessed, result.CandlesSaved, result.Failures };
        });

        group.MapGet("/experiments", async (IExperimentRepository experiments, CancellationToken ct, int count = 30) =>
            (await experiments.ListRecentAsync(count, ct)).Select(e => new { e.Id, e.Kind, e.Description, e.Trials, e.CodeVersion, e.CreatedAt }));

        group.MapGet("/defaults", (IOptions<BacktestOptions> options) => options.Value);

        group.MapPost("/", async (RunBacktestDto dto, BacktestService service, CancellationToken ct) =>
        {
            var request = new BacktestRequest(
                dto.Symbol,
                dto.Strategy,
                dto.Parameters,
                dto.From,
                dto.To,
                (dto.Simulation ?? new SimulationDto(null, null, null, null, null)).ToSettings(),
                dto.UseMetaModel ?? false,
                dto.Timeframe ?? Domain.MarketData.Timeframe.D1,
                dto.FlattenAtDayEnd ?? false);
            var run = await service.RunAsync(request, ct);
            return Results.Created($"/api/backtests/{run.Id}", BacktestDetailDto.From(run));
        });

        group.MapGet("/", async (IBacktestRunRepository runs, CancellationToken ct, int count = 30) =>
            (await runs.ListRecentAsync(count, ct)).Select(BacktestSummaryDto.From));

        group.MapGet("/{id:guid}", async (Guid id, IBacktestRunRepository runs, CancellationToken ct) =>
            await runs.FindAsync(id, ct) is { } run ? Results.Ok(BacktestDetailDto.From(run)) : Results.NotFound());

        group.MapPost("/compare", async (CompareDto dto, StrategyComparisonService service, CancellationToken ct) =>
            await service.CompareAsync(
                new ComparisonRequest(
                    dto.Symbols,
                    dto.From,
                    dto.To,
                    (dto.Simulation ?? new SimulationDto(null, null, null, null, null)).ToSettings(),
                    dto.InSamplePercent ?? 70m),
                ct));

        return app;
    }
}

public sealed record UniverseRequest(bool Active);

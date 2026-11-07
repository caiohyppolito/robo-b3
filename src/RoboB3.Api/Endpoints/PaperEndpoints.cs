using RoboB3.Api.Infrastructure;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Paper;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;

namespace RoboB3.Api.Endpoints;

public sealed record AssignmentDto(
    Guid Id,
    string Symbol,
    string Strategy,
    IReadOnlyDictionary<string, decimal> Parameters,
    Timeframe Timeframe,
    bool UseMetaModel,
    bool FlattenAtDayEnd,
    bool IsEnabled,
    DateTimeOffset? LastProcessedBar)
{
    public static AssignmentDto From(StrategyAssignment a) =>
        new(a.Id, a.Symbol, a.StrategyKey, a.Parameters, a.Timeframe, a.UseMetaModel, a.FlattenAtDayEnd, a.IsEnabled, a.LastProcessedBar);
}

public sealed record AssignmentRequestDto(
    string Symbol,
    string Strategy,
    Dictionary<string, decimal>? Parameters,
    Timeframe Timeframe,
    bool UseMetaModel,
    bool FlattenAtDayEnd)
{
    public AssignmentRequest ToRequest() => new(Symbol, Strategy, Parameters, Timeframe, UseMetaModel, FlattenAtDayEnd);
}

public sealed record PaperPositionDto(
    Guid Id,
    Guid? AssignmentId,
    string Symbol,
    string Contract,
    string Strategy,
    string Direction,
    int Quantity,
    decimal EntryPrice,
    decimal? Stop,
    decimal MarkPrice,
    DateTimeOffset MarkTime,
    decimal UnrealizedProfit,
    double? Probability,
    DateTimeOffset EntryTime)
{
    public static PaperPositionDto From(PaperPosition p) => new(p.Id, p.AssignmentId, p.Symbol, p.ContractSymbol, p.StrategyKey,
        p.Direction.ToString(), p.Quantity, p.EntryPrice, p.Stop, p.MarkPrice, p.MarkTime, Math.Round(p.UnrealizedProfit, 2), p.Probability, p.EntryTime);
}

public sealed record PaperFillDto(DateTimeOffset Time, string Symbol, string Side, int Quantity, decimal Price, decimal Cost, decimal? RealizedProfit, string Reason)
{
    public static PaperFillDto From(PaperFill f) => new(f.Time, f.Symbol, f.Side.ToString(), f.Quantity, f.Price, f.Cost, f.RealizedProfit, f.Reason);
}

public sealed record PaperAccountDto(
    decimal InitialCapital,
    decimal Equity,
    decimal Cash,
    decimal DayProfit,
    decimal TotalProfit,
    decimal RealizedProfit,
    decimal CostsPaid,
    decimal PeakEquity,
    IReadOnlyList<PaperPositionDto> Positions,
    IReadOnlyList<PaperFillDto> Fills);

internal static class PaperEndpoints
{
    public static IEndpointRouteBuilder MapPaperEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/feed/status", (IMarketDataFeed feed, CancellationToken ct) => feed.GetStatusAsync(ct)).WithTags("Operação");

        var group = app.MapGroup("/api/paper").WithTags("Operação");

        group.MapGet("/account", async (IPaperRepository paper, Microsoft.Extensions.Options.IOptions<TradingCapitalOptions> capital, CancellationToken ct) =>
        {
            var account = await paper.GetAccountAsync(ct);
            var positions = await paper.ListPositionsAsync(ct);
            var fills = await paper.ListRecentFillsAsync(50, ct);
            var initial = account?.InitialCapital ?? capital.Value.InitialCapital;
            var equity = account?.Equity(positions) ?? initial;
            return new PaperAccountDto(
                initial,
                Math.Round(equity, 2),
                Math.Round(account?.Cash ?? initial, 2),
                Math.Round(equity - (account?.DayStartEquity ?? initial), 2),
                Math.Round(equity - initial, 2),
                Math.Round(account?.RealizedProfit ?? 0, 2),
                Math.Round(account?.CostsPaid ?? 0, 2),
                Math.Round(account?.PeakEquity ?? initial, 2),
                positions.Select(PaperPositionDto.From).ToList(),
                fills.Select(PaperFillDto.From).ToList());
        });

        group.MapPost("/cycle", (PaperTradingService service, CancellationToken ct) => service.RunCycleAsync(ct));

        group.MapPost("/positions/{id:guid}/close", async (Guid id, HttpContext http, PaperTradingService service, CancellationToken ct) =>
        {
            await service.ClosePositionAsync(id, http.Actor(), ct);
            return Results.NoContent();
        });

        group.MapPost("/reset", async (HttpContext http, PaperTradingService service, CancellationToken ct) =>
        {
            await service.ResetAsync(http.Actor(), ct);
            return Results.NoContent();
        });

        var assignments = group.MapGroup("/assignments");

        assignments.MapGet("/", async (StrategyAssignmentService service, CancellationToken ct) =>
            (await service.ListAsync(ct)).Select(AssignmentDto.From));

        assignments.MapPost("/", async (AssignmentRequestDto dto, HttpContext http, StrategyAssignmentService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(dto.ToRequest(), http.Actor(), ct);
            return Results.Created($"/api/paper/assignments/{created.Id}", AssignmentDto.From(created));
        });

        assignments.MapPut("/{id:guid}", async (Guid id, AssignmentRequestDto dto, HttpContext http, StrategyAssignmentService service, CancellationToken ct) =>
            AssignmentDto.From(await service.UpdateAsync(id, dto.ToRequest(), http.Actor(), ct)));

        assignments.MapPost("/{id:guid}/enable", async (Guid id, HttpContext http, StrategyAssignmentService service, CancellationToken ct) =>
            AssignmentDto.From(await service.SetEnabledAsync(id, true, http.Actor(), ct)));

        assignments.MapPost("/{id:guid}/disable", async (Guid id, HttpContext http, StrategyAssignmentService service, CancellationToken ct) =>
            AssignmentDto.From(await service.SetEnabledAsync(id, false, http.Actor(), ct)));

        assignments.MapDelete("/{id:guid}", async (Guid id, HttpContext http, StrategyAssignmentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, http.Actor(), ct);
            return Results.NoContent();
        });

        return app;
    }
}

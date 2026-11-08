using RoboB3.Api.Infrastructure;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Execution;
using RoboB3.Application.Paper;
using RoboB3.Domain.Execution;
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
    DateTimeOffset EntryTime,
    long? BrokerTicket)
{
    public static PaperPositionDto From(PaperPosition p) => new(p.Id, p.AssignmentId, p.Symbol, p.ContractSymbol, p.StrategyKey,
        p.Direction.ToString(), p.Quantity, p.EntryPrice, p.Stop, p.MarkPrice, p.MarkTime, Math.Round(p.UnrealizedProfit, 2), p.Probability, p.EntryTime,
        p.BrokerTicket);
}

public sealed record PaperFillDto(DateTimeOffset Time, string Symbol, string Side, int Quantity, decimal Price, decimal Cost, decimal? RealizedProfit, string Reason)
{
    public static PaperFillDto From(PaperFill f) => new(f.Time, f.Symbol, f.Side.ToString(), f.Quantity, f.Price, f.Cost, f.RealizedProfit, f.Reason);
}

public sealed record PaperAccountDto(
    ExecutionMode Mode,
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

public sealed record ChangeModeDto(ExecutionMode Mode, string? Confirmation);

public sealed record FlattenDto(string? Confirmation);

public sealed record OrderIntentDto(
    string ClientOrderId,
    DateTimeOffset CreatedAt,
    OrderPurpose Purpose,
    string Symbol,
    string Side,
    int Quantity,
    decimal ReferencePrice,
    decimal? StopLoss,
    OrderIntentStatus Status,
    decimal? FillPrice,
    int? FillQuantity,
    decimal? Slippage,
    int? LatencyMs,
    long? BrokerTicket,
    string Reason,
    string? Message)
{
    public static OrderIntentDto From(OrderIntent i) => new(i.ClientOrderId, i.CreatedAt, i.Purpose, i.Symbol, i.Side.ToString(), i.Quantity,
        i.ReferencePrice, i.StopLoss, i.Status, i.FillPrice, i.FillQuantity, i.Slippage, i.LatencyMs, i.BrokerTicket, i.Reason, i.Message);
}

public sealed record DecisionDto(
    long Id,
    DateTimeOffset Time,
    string Symbol,
    string Contract,
    string Strategy,
    DateTimeOffset BarTime,
    string Signal,
    DecisionOutcome Outcome,
    string Detail,
    double? Probability,
    int? Quantity,
    decimal? Price,
    decimal? Stop,
    string? Sizing)
{
    public static DecisionDto From(TradingDecision d) => new(d.Id, d.Time, d.Symbol, d.Contract, d.StrategyKey, d.BarTime,
        d.Signal.ToString(), d.Outcome, d.Detail, d.Probability, d.Quantity, d.Price, d.Stop, d.Sizing);
}

internal static class PaperEndpoints
{
    public static IEndpointRouteBuilder MapPaperEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/market/chart/{symbol}", (string symbol, Application.MarketData.ChartService charts, CancellationToken ct, Timeframe timeframe = Timeframe.M5, int count = 300) =>
            charts.GetAsync(symbol, timeframe, count, ct)).WithTags("Operação");

        app.MapGet("/api/history/trades", (TradeHistoryService history, CancellationToken ct, ExecutionMode? mode, string? symbol, int count = 300) =>
            history.ListAsync(mode, symbol, count, ct)).WithTags("Operação");

        app.MapGet("/api/market/watch", (MarketWatchService service, CancellationToken ct) => service.GetAsync(ct)).WithTags("Operação");

        app.MapGet("/api/feed/status", (IMarketDataFeed feed, CancellationToken ct) => feed.GetStatusAsync(ct)).WithTags("Operação");

        var group = app.MapGroup("/api/paper").WithTags("Operação");

        group.MapGet("/account", async (
            IPaperRepository paper,
            IExecutionSettingsRepository execution,
            Microsoft.Extensions.Options.IOptions<TradingCapitalOptions> capital,
            CancellationToken ct) =>
        {
            var mode = (await execution.GetAsync(ct)).Mode;
            var account = await paper.GetAccountAsync(mode, ct);
            var positions = await paper.ListPositionsAsync(mode, ct);
            var fills = await paper.ListRecentFillsAsync(mode, 50, ct);
            var initial = account?.InitialCapital ?? capital.Value.InitialCapital;
            var equity = account?.Equity(positions) ?? initial;
            return new PaperAccountDto(
                mode,
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

        group.MapGet("/settings", (Microsoft.Extensions.Options.IOptions<PaperTradingOptions> options) => options.Value);

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

        app.MapGet("/api/decisions", async (IPaperRepository paper, IExecutionSettingsRepository execution, CancellationToken ct, int count = 50) =>
        {
            var mode = (await execution.GetAsync(ct)).Mode;
            return (await paper.ListRecentDecisionsAsync(mode, count, ct)).Select(DecisionDto.From);
        }).WithTags("Operação");

        app.MapGet("/api/orders", async (IOrderIntentRepository intents, IExecutionSettingsRepository execution, CancellationToken ct, int count = 50) =>
        {
            var mode = (await execution.GetAsync(ct)).Mode;
            return (await intents.ListRecentAsync(mode, count, ct)).Select(OrderIntentDto.From);
        }).WithTags("Operação");

        // Emergência: trava o robô e encerra a mercado todas as posições do modo atual. Exige a frase digitada.
        app.MapPost("/api/emergency/flatten", async (FlattenDto dto, HttpContext http, PaperTradingService service, CancellationToken ct) =>
            string.Equals(dto.Confirmation?.Trim(), "ZERAR TUDO", StringComparison.Ordinal)
                ? Results.Ok(await service.FlattenAllAsync(http.Actor(), ct))
                : Results.Problem(statusCode: 400, title: "Confirmação necessária", detail: "Digite exatamente: ZERAR TUDO")).WithTags("Operação");

        app.MapGet("/api/execution", (ExecutionService service, CancellationToken ct) => service.GetAsync(ct)).WithTags("Operação");

        app.MapPut("/api/execution", async (ChangeModeDto dto, HttpContext http, ExecutionService service, CancellationToken ct) =>
            await service.ChangeModeAsync(dto.Mode, dto.Confirmation, http.Actor(), ct)).WithTags("Operação");

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

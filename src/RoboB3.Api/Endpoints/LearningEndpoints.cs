using RoboB3.Api.Infrastructure;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Learning;
using RoboB3.Domain.Learning;

namespace RoboB3.Api.Endpoints;

public sealed record TrainRequest(string Strategy);

public sealed record MetaModelDto(
    Guid Id,
    string Strategy,
    DateTimeOffset CreatedAt,
    MetaModelStatus Status,
    string Decision,
    DateOnly TrainingFrom,
    DateOnly SplitDate,
    DateOnly ValidationTo,
    int TrainingExamples,
    int ValidationExamples,
    double Threshold,
    double Auc,
    decimal BaseWinRatePercent,
    decimal FilteredWinRatePercent,
    decimal BaseAverageReturnPercent,
    decimal FilteredAverageReturnPercent,
    decimal KeptPercent,
    decimal UnfilteredSharpe,
    decimal FilteredSharpe,
    decimal? ChampionSharpe,
    IReadOnlyDictionary<string, double> FeatureImportance,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RetiredAt,
    string? Analysis,
    CalibrationReport? Calibration)
{
    public static MetaModelDto From(MetaModel m) => new(
        m.Id, m.StrategyKey, m.CreatedAt, m.Status, m.Decision, m.TrainingFrom, m.SplitDate, m.ValidationTo,
        m.TrainingExamples, m.ValidationExamples, m.Threshold, m.Auc, m.BaseWinRatePercent, m.FilteredWinRatePercent,
        m.BaseAverageReturnPercent, m.FilteredAverageReturnPercent, m.KeptPercent, m.UnfilteredSharpe, m.FilteredSharpe,
        m.ChampionSharpe, m.FeatureImportance, m.ActivatedAt, m.RetiredAt, m.Analysis, m.Calibration);
}

public sealed record JournalEntryDto(
    Guid Id,
    TradeSource Source,
    string Strategy,
    string Symbol,
    string Direction,
    DateOnly EntryDate,
    DateOnly ExitDate,
    decimal NetProfit,
    decimal ReturnPercent,
    double? ModelProbability)
{
    public static JournalEntryDto From(TradeJournalEntry e) => new(
        e.Id, e.Source, e.StrategyKey, e.Symbol, e.Direction.ToString(), e.EntryDate, e.ExitDate, e.NetProfit, e.ReturnPercent, e.ModelProbability);
}

internal static class LearningEndpoints
{
    public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/learning").WithTags("IA");

        group.MapGet("/models", async (IMetaModelRepository models, CancellationToken ct, int count = 50) =>
            (await models.ListRecentAsync(count, ct)).Select(MetaModelDto.From));

        group.MapGet("/models/{id:guid}", async (Guid id, IMetaModelRepository models, CancellationToken ct) =>
            await models.FindAsync(id, ct) is { } model ? Results.Ok(MetaModelDto.From(model)) : Results.NotFound());

        group.MapPost("/train", async (TrainRequest request, LearningService learning, CancellationToken ct) =>
            MetaModelDto.From(await learning.TrainAsync(request.Strategy, ct)));

        group.MapPost("/models/{id:guid}/analysis", async (Guid id, LearningService learning, IModelAnalyst analyst, CancellationToken ct) =>
            MetaModelDto.From(await learning.AnalyzeAsync(id, analyst, ct)));

        group.MapGet("/models/{id:guid}/drift", async (Guid id, LearningService learning, IPaperRepository paper, CancellationToken ct) =>
            await learning.CheckDriftAsync(id, paper, ct));

        group.MapPost("/models/{id:guid}/reactivate", async (Guid id, LearningService learning, HttpContext http, CancellationToken ct) =>
            MetaModelDto.From(await learning.ReactivateAsync(id, http.Actor(), ct)));

        group.MapGet("/analyst", (IModelAnalyst analyst) => new { analyst.IsConfigured });

        group.MapGet("/journal", async (ITradeJournalRepository journal, CancellationToken ct, int count = 100) =>
            (await journal.ListRecentAsync(count, ct)).Select(JournalEntryDto.From));

        return app;
    }
}

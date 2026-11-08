using RoboB3.Application.Abstractions;
using RoboB3.Domain.MarketData;

namespace RoboB3.Api.Endpoints;

internal static class MarketDataEndpoints
{
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets").WithTags("Market data");

        // CDI do Banco Central: o último dia gravado e uma atualização sob demanda.
        app.MapGet("/api/cdi", async (Application.MarketData.RiskFreeRateService cdi, CancellationToken ct) =>
            await cdi.GetLatestAsync(ct) is { } latest
                ? Results.Ok(new { latest.Date, latest.DailyPercent, latest.AnnualizedPercent, Source = "Banco Central (SGS 12)" })
                : Results.Ok(new { Date = (DateOnly?)null, DailyPercent = (decimal?)null, AnnualizedPercent = (decimal?)null, Source = "taxa fixa configurada" }))
            .WithTags("Market data");
        app.MapPost("/api/cdi/sync", async (Application.MarketData.RiskFreeRateService cdi, CancellationToken ct) =>
            new { Saved = await cdi.SyncAsync(ct) })
            .WithTags("Market data");

        group.MapGet("/", async (IAssetRepository assets, CancellationToken ct) =>
            (await assets.ListAsync(onlyActive: false, ct)).Select(AssetDto.From));

        group.MapGet("/{symbol}/candles", async (
            string symbol,
            IAssetRepository assets,
            ICandleRepository candles,
            TimeProvider clock,
            CancellationToken ct,
            Timeframe timeframe = Timeframe.D1,
            int days = 120) =>
        {
            var asset = await assets.FindAsync(symbol, ct);
            if (asset is null)
            {
                return Results.NotFound();
            }

            var to = clock.GetUtcNow();
            var from = to.AddDays(-Math.Clamp(days, 1, 3650));
            var result = await candles.ListAsync(asset.Symbol, timeframe, from, to, ct);
            return Results.Ok(result.Select(CandleDto.From));
        });

        return app;
    }
}

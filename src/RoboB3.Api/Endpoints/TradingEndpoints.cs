using RoboB3.Api.Infrastructure;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Risk;

namespace RoboB3.Api.Endpoints;

internal static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Trading");

        group.MapGet("/status", async (
            KillSwitchService killSwitch,
            RiskSettingsService risk,
            IPortfolioProvider portfolio,
            CancellationToken ct) =>
        {
            var state = await killSwitch.GetStateAsync(ct);
            var limits = await risk.GetLimitsAsync(ct);
            var snapshot = await portfolio.GetSnapshotAsync(ct);
            return new StatusDto(TradingStateDto.From(state), RiskLimitsDto.From(limits), PortfolioDto.From(snapshot));
        });

        group.MapPost("/kill-switch/halt", async (HaltRequest request, HttpContext http, KillSwitchService killSwitch, CancellationToken ct) =>
            TradingStateDto.From(await killSwitch.HaltAsync(request.Reason, http.Actor(), ct)));

        group.MapPost("/kill-switch/resume", async (HttpContext http, KillSwitchService killSwitch, CancellationToken ct) =>
            TradingStateDto.From(await killSwitch.ResumeAsync(http.Actor(), ct)));

        group.MapGet("/risk/limits", async (RiskSettingsService risk, CancellationToken ct) =>
            RiskLimitsDto.From(await risk.GetLimitsAsync(ct)));

        group.MapPut("/risk/limits", async (RiskLimitsDto request, HttpContext http, RiskSettingsService risk, CancellationToken ct) =>
            RiskLimitsDto.From(await risk.UpdateLimitsAsync(request.ToDomain(), http.Actor(), ct)));

        group.MapGet("/audit", async (IAuditLog audit, CancellationToken ct, int count = 50) =>
            (await audit.ListRecentAsync(count, ct)).Select(AuditEntryDto.From));

        return app;
    }
}

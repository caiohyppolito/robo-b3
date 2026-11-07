using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Risk;
using RoboB3.Domain.Risk;

namespace RoboB3.Application.Tests;

public class RiskServicesTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAuditLog _audit = new();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private TradingState RunningState()
    {
        var state = TradingState.CreateHalted(_clock.GetUtcNow());
        state.Resume("teste", _clock.GetUtcNow());
        return state;
    }

    private KillSwitchService KillSwitch(TradingState state) =>
        new(new FakeTradingStateRepository(state), _audit, _unitOfWork, _clock);

    [Fact]
    public async Task Halt_trava_audita_e_salva()
    {
        var state = RunningState();

        await KillSwitch(state).HaltAsync("mercado estranho", "caio", CancellationToken.None);

        Assert.True(state.IsHalted);
        Assert.Contains(_audit.Entries, e => e.Action == "kill-switch.halt" && e.Actor == "caio");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Monitor_trava_quando_perda_diaria_estoura()
    {
        var state = RunningState();
        var settings = new RiskSettings(RiskLimits.Default, _clock.GetUtcNow());
        var losing = new PortfolioSnapshot(97_000m, 100_000m, 100_000m, []);
        var monitor = new RiskMonitorService(
            new FakePortfolioProvider(losing),
            new FakeRiskSettingsRepository(settings),
            KillSwitch(state),
            NullLogger<RiskMonitorService>.Instance);

        var halted = await monitor.CheckAsync(CancellationToken.None);

        Assert.True(halted);
        Assert.True(state.IsHalted);
        Assert.Equal(RiskMonitorService.Actor, state.ChangedBy);
    }

    [Fact]
    public async Task Monitor_nao_faz_nada_dentro_dos_limites()
    {
        var state = RunningState();
        var settings = new RiskSettings(RiskLimits.Default, _clock.GetUtcNow());
        var fine = new PortfolioSnapshot(99_500m, 100_000m, 100_000m, []);
        var monitor = new RiskMonitorService(
            new FakePortfolioProvider(fine),
            new FakeRiskSettingsRepository(settings),
            KillSwitch(state),
            NullLogger<RiskMonitorService>.Instance);

        Assert.False(await monitor.CheckAsync(CancellationToken.None));
        Assert.False(state.IsHalted);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Atualizar_limites_registra_valores_anterior_e_novo()
    {
        var settings = new RiskSettings(RiskLimits.Default, _clock.GetUtcNow());
        var service = new RiskSettingsService(new FakeRiskSettingsRepository(settings), _audit, _unitOfWork, _clock);
        var newLimits = new RiskLimits(1m, 10m, 10m, 3, 1);

        var result = await service.UpdateLimitsAsync(newLimits, "caio", CancellationToken.None);

        Assert.Equal(newLimits, result);
        Assert.Single(_audit.Entries);
        Assert.Contains("->", _audit.Entries[0].Details, StringComparison.Ordinal);
    }
}

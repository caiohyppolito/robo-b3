using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Application.Paper;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Infrastructure.Learning;
using RoboB3.Infrastructure.MarketData.Mt5;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;
using RoboB3.Infrastructure.Portfolio;

namespace RoboB3.Infrastructure.Tests;

/// <summary>Feed de mercado controlado pelo teste.</summary>
internal sealed class FakeFeed(FakeTimeProvider clock) : IMarketDataFeed
{
    public bool Connected { get; set; } = true;

    public decimal Price { get; set; } = 130_000m;

    public decimal Spread { get; set; } = 5m;

    public Task<FeedStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FeedStatus(Connected, "teste", "Servidor", 1, true, "Corretora", Connected ? null : "terminal fechado"));

    public Task<string> ResolveSymbolAsync(Asset asset, CancellationToken cancellationToken) =>
        Task.FromResult(asset.IsFuture ? $"{asset.Symbol}Z26" : asset.Symbol);

    public Task<IReadOnlyList<Candle>> GetBarsAsync(Asset asset, string feedSymbol, Timeframe timeframe, int count, CancellationToken cancellationToken)
    {
        // Barras de 5 minutos terminando na barra em formação, com preço subindo devagar.
        var now = clock.GetUtcNow();
        var current = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, count)
            .Select(i =>
            {
                var price = Price - ((count - 1 - i) * 1m);
                return new Candle(asset.Symbol, timeframe, current.AddMinutes(-5 * (count - 1 - i)), price, price + 10, price - 10, price, 100);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<Candle>>(candles);
    }

    public Task<IReadOnlyList<Candle>> GetHistoryAsync(Asset asset, Timeframe timeframe, int count, CancellationToken cancellationToken) =>
        GetBarsAsync(asset, asset.Symbol, timeframe, count, cancellationToken);

    public Task<Quote> GetQuoteAsync(string feedSymbol, CancellationToken cancellationToken) =>
        Task.FromResult(new Quote(feedSymbol, Price - Spread, Price + Spread, Price, clock.GetUtcNow()));
}

public class PaperTradingTests
{
    // Segunda-feira, 14h em Brasília.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero));
    private readonly RoboB3DbContext _db = new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeFeed _feed;
    private readonly TradingState _state;

    public PaperTradingTests()
    {
        _feed = new FakeFeed(_clock);
        _state = TradingState.CreateHalted(_clock.GetUtcNow());
        _state.Resume("teste", _clock.GetUtcNow());
    }

    private sealed class FixedState(TradingState state) : ITradingStateRepository
    {
        public Task<TradingState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(state);
    }

    private PaperTradingService Service() => new(
        _feed,
        new PaperRepository(_db),
        new StrategyAssignmentRepository(_db),
        new AssetRepository(_db),
        new FixedState(_state),
        new FixedRiskSettings(),
        new TradeJournalRepository(_db),
        LearningIntegrationTests.Service(_db),
        new AuditLog(_db),
        _db,
        Options.Create(new PaperTradingOptions()),
        Options.Create(new BacktestOptions { SlippagePercent = 0, FeesPercent = 0, FuturesFeePerContract = 0 }),
        Options.Create(new TradingCapitalOptions()),
        _clock,
        NullLogger<PaperTradingService>.Instance);

    private async Task<StrategyAssignment> AssignAsync(string symbol, string strategy, bool dayTrade = false, bool enabled = true)
    {
        await new DatabaseInitializer(_db, _clock, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(false, CancellationToken.None);
        var assignment = new StrategyAssignment(symbol, strategy, new Dictionary<string, decimal>(), Timeframe.M5, false, dayTrade, _clock.GetUtcNow());
        if (enabled)
        {
            assignment.Enable();
        }

        _db.StrategyAssignments.Add(assignment);
        await _db.SaveChangesAsync();
        return assignment;
    }

    [Fact]
    public async Task Sem_conexao_com_o_mt5_nada_acontece()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _feed.Connected = false;

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.False(report.FeedConnected);
        Assert.Contains("terminal fechado", report.Messages[0], StringComparison.Ordinal);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
    }

    [Fact]
    public async Task Sinal_em_barra_fechada_abre_posicao_no_ask_uma_unica_vez()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);

        var first = await Service().RunCycleAsync(CancellationToken.None);
        var second = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, first.Opened);
        Assert.Equal(0, second.Opened);
        var position = Assert.Single(await _db.PaperPositions.ToListAsync());
        Assert.Equal("WINZ26", position.ContractSymbol);
        Assert.Equal(PositionDirection.Long, position.Direction);
        Assert.Equal(130_005m, position.EntryPrice);
        Assert.Equal(2, position.Quantity);
        Assert.Single(await _db.PaperFills.ToListAsync());
    }

    [Fact]
    public async Task Alocacao_desligada_nao_opera()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold, enabled: false);

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, report.Opened);
    }

    [Fact]
    public async Task Robo_travado_bloqueia_entrada_e_explica()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _state.Halt("teste", "caio", _clock.GetUtcNow());

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, report.Opened);
        Assert.Contains(report.Messages, m => m.Contains("robô travado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fora_do_horario_nao_entra()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 10, 12, 30, 0, TimeSpan.Zero)); // 9h30 em Brasília

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, report.Opened);
        Assert.Contains(report.Messages, m => m.Contains("fora do horário", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_atingido_encerra_mesmo_com_robo_travado_e_alimenta_o_diario()
    {
        var assignment = await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        await Service().RunCycleAsync(CancellationToken.None);
        var position = await _db.PaperPositions.SingleAsync();
        position.TightenStop(129_900m);
        await _db.SaveChangesAsync();

        _state.Halt("teste", "caio", _clock.GetUtcNow());
        _feed.Price = 129_800m;
        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, report.Closed);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
        var fill = await _db.PaperFills.OrderBy(f => f.Id).LastAsync();
        Assert.Equal("stop", fill.Reason);
        Assert.Equal(129_795m, fill.Price);
        var entry = Assert.Single(await _db.TradeJournal.ToListAsync());
        Assert.Equal(TradeSource.Paper, entry.Source);
        Assert.False(entry.IsWin);
        Assert.Equal(assignment.StrategyKey, entry.StrategyKey);
    }

    [Fact]
    public async Task Day_trade_e_zerado_no_horario_de_encerramento()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold, dayTrade: true);
        await Service().RunCycleAsync(CancellationToken.None);

        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 20, 55, 0, TimeSpan.Zero)); // 17h55 em Brasília
        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, report.Closed);
        Assert.Equal(0, report.Opened);
        Assert.Equal("fim do pregão", (await _db.PaperFills.OrderBy(f => f.Id).LastAsync()).Reason);
    }

    [Fact]
    public async Task Carteira_do_monitor_de_risco_reflete_a_conta_simulada()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        await Service().RunCycleAsync(CancellationToken.None);
        _feed.Price = 128_000m;
        await Service().RunCycleAsync(CancellationToken.None);

        var snapshot = await new PaperPortfolioProvider(new PaperRepository(_db), Options.Create(new TradingCapitalOptions()))
            .GetSnapshotAsync(CancellationToken.None);

        // 2 contratos comprados a 130.005 e marcados a 128.000: −2.005 pts × R$ 0,20 × 2.
        Assert.Equal(100_000m - 802m, snapshot.Equity);
        Assert.Single(snapshot.Positions);
        Assert.True(snapshot.DrawdownPercent > 0);
    }

    [Fact]
    public async Task Reiniciar_conta_exige_robo_travado_e_sem_posicoes()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        await Service().RunCycleAsync(CancellationToken.None);

        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => Service().ResetAsync("caio", CancellationToken.None));
        _state.Halt("teste", "caio", _clock.GetUtcNow());
        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => Service().ResetAsync("caio", CancellationToken.None));

        var position = await _db.PaperPositions.SingleAsync();
        await Service().ClosePositionAsync(position.Id, "caio", CancellationToken.None);
        await Service().ResetAsync("caio", CancellationToken.None);

        Assert.Empty(await _db.PaperAccounts.ToListAsync());
        Assert.Empty(await _db.PaperFills.ToListAsync());
        Assert.Single(await _db.TradeJournal.ToListAsync());
    }

    [Fact]
    public void Barras_do_mt5_viram_candles_do_ativo_e_diarios_usam_a_data_do_pregao()
    {
        var rates = new Mt5GatewayFeed.RatesDto("WINZ26",
        [
            new(new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero), 10m, 12m, 9m, 11m, 5), // 0h em Brasília
            new(new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero), 0m, 12m, 9m, 11m, 5),  // inválida
        ]);

        var daily = Mt5GatewayFeed.ToCandles("WIN", Timeframe.D1, rates);
        var intraday = Mt5GatewayFeed.ToCandles("WIN", Timeframe.M5, rates);

        var candle = Assert.Single(daily);
        Assert.Equal("WIN", candle.Symbol);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), candle.OpenTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero), Assert.Single(intraday).OpenTime);
    }

    [Fact]
    public async Task Conector_fora_do_ar_vira_status_desconectado_com_explicacao()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/"), Timeout = TimeSpan.FromSeconds(2) };

        var status = await new Mt5GatewayFeed(http).GetStatusAsync(CancellationToken.None);

        Assert.False(status.Connected);
        Assert.Contains("rodando", status.Message, StringComparison.Ordinal);
    }
}

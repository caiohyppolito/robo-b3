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

    public bool? IsDemo { get; set; } = true;

    public decimal Price { get; set; } = 130_000m;

    public decimal Spread { get; set; } = 5m;

    /// <summary>Distância da máxima e da mínima ao fechamento. Define o ATR das barras do teste.</summary>
    public decimal BarRange { get; set; } = 10m;

    /// <summary>Quando definido, as barras terminam aqui (mercado fechado) em vez de no horário atual.</summary>
    public DateTimeOffset? LastBarOpen { get; set; }

    /// <summary>Atraso artificial, para provocar ciclos simultâneos.</summary>
    public TimeSpan Delay { get; set; }

    public async Task<FeedStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Delay, cancellationToken);
        return new FeedStatus(Connected, "teste", "Servidor", 1, IsDemo, "Corretora", Connected ? null : "terminal fechado");
    }

    public Task<string> ResolveSymbolAsync(Asset asset, CancellationToken cancellationToken) =>
        Task.FromResult(asset.IsFuture ? $"{asset.Symbol}Z26" : asset.Symbol);

    public Task<IReadOnlyList<Candle>> GetBarsAsync(Asset asset, string feedSymbol, Timeframe timeframe, int count, CancellationToken cancellationToken)
    {
        // Barras de 5 minutos terminando na barra em formação, com preço subindo devagar.
        var now = clock.GetUtcNow();
        var current = LastBarOpen ?? new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute / 5 * 5, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, count)
            .Select(i =>
            {
                var price = Price - ((count - 1 - i) * 1m);
                return new Candle(asset.Symbol, timeframe, current.AddMinutes(-5 * (count - 1 - i)), price, price + BarRange, price - BarRange, price, 100);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<Candle>>(candles);
    }

    public Task<IReadOnlyList<Candle>> GetHistoryAsync(Asset asset, Timeframe timeframe, int count, CancellationToken cancellationToken) =>
        GetBarsAsync(asset, asset.Symbol, timeframe, count, cancellationToken);

    public Task<Quote> GetQuoteAsync(string feedSymbol, CancellationToken cancellationToken) =>
        Task.FromResult(new Quote(feedSymbol, Price - Spread, Price + Spread, Price, clock.GetUtcNow()));
}

/// <summary>Corretora em memória (compensação por símbolo), com recusas e stops controlados pelo teste.</summary>
internal sealed class FakeBroker(Domain.Execution.ExecutionMode mode) : IOrderExecutor
{
    private long _nextTicket = 500;

    public Domain.Execution.ExecutionMode Mode => mode;

    public List<BrokerPosition> Positions { get; } = [];

    public Dictionary<long, decimal> ClosingPrices { get; } = [];

    public List<OrderInstruction> Sent { get; } = [];

    public List<(string Symbol, long Ticket, decimal Stop)> StopUpdates { get; } = [];

    public bool RejectNext { get; set; }

    public decimal Equity { get; set; } = 50_000m;

    public Task<OrderResult> SendAsync(OrderInstruction instruction, CancellationToken cancellationToken)
    {
        Sent.Add(instruction);
        if (RejectNext)
        {
            RejectNext = false;
            return Task.FromResult(new OrderResult(false, 0, 0, null, "Ordem recusada (10019): sem margem"));
        }

        var price = instruction.ReferencePrice + (instruction.Side == Domain.Trading.OrderSide.Buy ? 5m : -5m);
        if (instruction.PositionTicket is { } ticket)
        {
            Positions.RemoveAll(p => p.Ticket == ticket);
            return Task.FromResult(new OrderResult(true, price, instruction.Quantity, ticket, "Executada."));
        }

        var opened = new BrokerPosition(++_nextTicket, instruction.Symbol, instruction.Side, instruction.Quantity, price, instruction.StopLoss, true);
        Positions.Add(opened);
        return Task.FromResult(new OrderResult(true, price, instruction.Quantity, opened.Ticket, "Executada."));
    }

    public Task<bool> UpdateStopAsync(string symbol, long ticket, decimal stop, CancellationToken cancellationToken)
    {
        StopUpdates.Add((symbol, ticket, stop));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<BrokerPosition>?> GetPositionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BrokerPosition>?>(Positions.ToList());

    public Task<decimal?> GetClosingPriceAsync(long ticket, CancellationToken cancellationToken) =>
        Task.FromResult(ClosingPrices.TryGetValue(ticket, out var price) ? price : (decimal?)null);

    public Task<decimal?> GetEquityAsync(CancellationToken cancellationToken) => Task.FromResult<decimal?>(Equity);
}

internal sealed class FakeExecutors(IOrderExecutor paper, IOrderExecutor broker) : IOrderExecutorFactory
{
    public IOrderExecutor For(Domain.Execution.ExecutionMode mode) => mode == Domain.Execution.ExecutionMode.Paper ? paper : broker;
}

internal sealed class FakeExecutionSettings(Domain.Execution.ExecutionSettings settings) : IExecutionSettingsRepository
{
    public Task<Domain.Execution.ExecutionSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
}

public class PaperTradingTests
{
    // Segunda-feira, 14h em Brasília.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero));
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly RoboB3DbContext _db;
    private readonly FakeFeed _feed;
    private readonly TradingState _state;
    private readonly Domain.Execution.ExecutionSettings _execution;
    private readonly FakeBroker _broker = new(Domain.Execution.ExecutionMode.Mt5Demo);

    public PaperTradingTests()
    {
        _db = NewContext();
        _feed = new FakeFeed(_clock);
        _execution = new Domain.Execution.ExecutionSettings(_clock.GetUtcNow());
        _state = TradingState.CreateHalted(_clock.GetUtcNow());
        _state.Resume("teste", _clock.GetUtcNow());
    }

    private RoboB3DbContext NewContext() => new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(_dbName).Options);

    private sealed class FixedState(TradingState state) : ITradingStateRepository
    {
        public Task<TradingState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(state);
    }

    private PaperTradingService Service(RoboB3DbContext? context = null)
    {
        var db = context ?? _db;
        return new(
        _feed,
        new PaperRepository(db),
        new StrategyAssignmentRepository(db),
        new AssetRepository(db),
        new FixedState(_state),
        new FixedRiskSettings(),
        new TradeJournalRepository(db),
        new FakeExecutionSettings(_execution),
        new FakeExecutors(new Application.Execution.SimulatedOrderExecutor(Options.Create(new BacktestOptions { SlippagePercent = 0 })), _broker),
        new OrderIntentRepository(db),
        new Execution.SqlServerTradingLock(db),
        LearningIntegrationTests.Service(db),
        new AuditLog(db),
        db,
        Options.Create(new PaperTradingOptions()),
        Options.Create(new BacktestOptions { SlippagePercent = 0, FeesPercent = 0, FuturesFeePerContract = 0 }),
        Options.Create(new TradingCapitalOptions()),
        _clock,
        NullLogger<PaperTradingService>.Instance);
    }

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
    public async Task Barra_em_formacao_e_ignorada_e_o_ciclo_informa_contrato_e_barra()
    {
        var assignment = await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 17, 2, 0, TimeSpan.Zero)); // 14h02: barra das 14h00 em formação

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 16, 55, 0, TimeSpan.Zero), (await _db.StrategyAssignments.SingleAsync(a => a.Id == assignment.Id)).LastProcessedBar);
        Assert.Contains(report.Messages, m => m.StartsWith("WIN → WINZ26: barra das 09/11 13:55 avaliada, sinal: comprado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Com_mercado_fechado_a_ultima_barra_do_pregao_ja_conta_como_fechada()
    {
        var assignment = await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero)); // sábado
        _feed.LastBarOpen = new DateTimeOffset(2026, 11, 13, 21, 25, 0, TimeSpan.Zero); // 18h25 de sexta, em Brasília

        await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(_feed.LastBarOpen, (await _db.StrategyAssignments.SingleAsync(a => a.Id == assignment.Id)).LastProcessedBar);
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
    public async Task Alvo_atingido_encerra_a_posicao_com_lucro()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        await Service().RunCycleAsync(CancellationToken.None);
        var position = await _db.PaperPositions.SingleAsync();
        _db.Entry(position).Property(p => p.TargetPrice).CurrentValue = 130_200m;
        await _db.SaveChangesAsync();

        _feed.Price = 130_300m;
        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, report.Closed);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
        var fill = await _db.PaperFills.OrderBy(f => f.Id).LastAsync();
        Assert.Equal("alvo", fill.Reason);
        Assert.True(Assert.Single(await _db.TradeJournal.ToListAsync()).IsWin);
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

        var snapshot = await new PaperPortfolioProvider(new PaperRepository(_db), new FakeExecutionSettings(_execution), Options.Create(new TradingCapitalOptions()))
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

    // ---------- Fase 5: execução no MetaTrader 5 ----------

    private async Task<StrategyAssignment> AssignInDemoAsync(string symbol = "WIN")
    {
        var assignment = await AssignAsync(symbol, StrategyKeys.BuyAndHold);
        _execution.ChangeTo(Domain.Execution.ExecutionMode.Mt5Demo, "teste", _clock.GetUtcNow());
        return assignment;
    }

    [Fact]
    public async Task No_mt5_a_entrada_vai_para_a_corretora_e_o_livro_usa_o_preco_e_o_ticket_dela()
    {
        await AssignInDemoAsync();

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, report.Opened);
        var sent = Assert.Single(_broker.Sent);
        Assert.Equal("WINZ26", sent.Symbol);
        var position = await _db.PaperPositions.SingleAsync();
        Assert.Equal(Domain.Execution.ExecutionMode.Mt5Demo, position.Mode);
        Assert.Equal(_broker.Positions[0].Ticket, position.BrokerTicket);
        Assert.Equal(130_010m, position.EntryPrice);
        Assert.Equal(50_000m, (await _db.PaperAccounts.SingleAsync()).InitialCapital);
        Assert.Contains(await _db.AuditEntries.ToListAsync(), a => a.Action == "ordem.executada");
    }

    [Fact]
    public async Task Stop_movel_vai_para_a_corretora_arredondado_no_tick_do_mini_indice()
    {
        // Amplitude fracionária: o stop da estratégia (3 ATRs) não cai num múltiplo de 5,
        // e fora do tick a corretora recusaria a alteração.
        _feed.BarRange = 3.7m;
        var assignment = await AssignAsync("WIN", StrategyKeys.SmaCross);
        _execution.ChangeTo(Domain.Execution.ExecutionMode.Mt5Demo, "teste", _clock.GetUtcNow());

        // O cruzamento de médias não acontece numa série só de alta, então a posição entra
        // semeada, já aberta na corretora com o stop registrado lá.
        await Service().RunCycleAsync(CancellationToken.None);
        var account = await _db.PaperAccounts.SingleAsync();
        var asset = await _db.Assets.SingleAsync(a => a.Symbol == "WIN");
        var (position, _) = account.OpenPosition(assignment.Id, InstrumentSpec.For(asset), "WINZ26", StrategyKeys.SmaCross,
            PositionDirection.Long, 1, 129_000m, 128_900m, new CostModel(0, 0, 0, 0), null, null, _clock.GetUtcNow(), brokerTicket: 777);
        _db.PaperPositions.Add(position);
        _broker.Positions.Add(new BrokerPosition(777, "WINZ26", Domain.Trading.OrderSide.Buy, 1, 129_000m, 128_900m, true));
        await _db.SaveChangesAsync();

        _clock.SetUtcNow(_clock.GetUtcNow().AddMinutes(5));
        await Service().RunCycleAsync(CancellationToken.None);

        var atualizacao = Assert.Single(_broker.StopUpdates);
        Assert.Equal(0m, atualizacao.Stop % 5m);
        // O livro do robô guarda exatamente o stop que a corretora recebeu.
        Assert.Equal(atualizacao.Stop, (await _db.PaperPositions.SingleAsync()).Stop);
    }

    [Fact]
    public async Task Conta_logada_diferente_do_modo_trava_o_robo_sem_enviar_ordens()
    {
        await AssignInDemoAsync();
        _feed.IsDemo = false;

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.True(_state.IsHalted);
        Assert.Empty(_broker.Sent);
        Assert.Contains(report.Messages, m => m.Contains("não corresponde ao modo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ordem_recusada_nao_abre_posicao_e_fica_na_auditoria()
    {
        await AssignInDemoAsync();
        _broker.RejectNext = true;

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, report.Opened);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
        Assert.Contains(report.Messages, m => m.Contains("sem margem", StringComparison.Ordinal));
        Assert.Contains(await _db.AuditEntries.ToListAsync(), a => a.Action == "ordem.recusada");
    }

    [Fact]
    public async Task Stop_executado_na_corretora_encerra_no_livro_ao_preco_real()
    {
        await AssignInDemoAsync();
        await Service().RunCycleAsync(CancellationToken.None);
        var ticket = _broker.Positions[0].Ticket;
        _broker.Positions.Clear();
        _broker.ClosingPrices[ticket] = 129_700m;

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Empty(await _db.PaperPositions.ToListAsync());
        var fill = await _db.PaperFills.OrderBy(f => f.Id).LastAsync();
        Assert.Equal("encerrada na corretora", fill.Reason);
        Assert.Equal(129_700m, fill.Price);
        Assert.False(_state.IsHalted);
        Assert.Single(await _db.TradeJournal.ToListAsync());
    }

    [Fact]
    public async Task Posicao_do_robo_na_corretora_sem_registro_trava_o_robo()
    {
        await AssignInDemoAsync();
        _broker.Positions.Add(new BrokerPosition(9999, "WDOZ26", Domain.Trading.OrderSide.Buy, 1, 5_400m, null, IsRobot: true));

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.True(_state.IsHalted);
        Assert.Contains(report.Messages, m => m.Contains("sem registro interno", StringComparison.Ordinal));
        Assert.Equal(0, report.Opened);
    }

    [Fact]
    public async Task Posicao_manual_na_corretora_nao_e_do_robo_e_nao_trava()
    {
        await AssignInDemoAsync();
        _broker.Positions.Add(new BrokerPosition(9999, "PETR4", Domain.Trading.OrderSide.Buy, 100, 36m, null, IsRobot: false));

        await Service().RunCycleAsync(CancellationToken.None);

        Assert.False(_state.IsHalted);
    }

    [Fact]
    public async Task Falha_ao_encerrar_trava_o_robo_e_mantem_a_posicao_para_nova_tentativa()
    {
        await AssignInDemoAsync();
        await Service().RunCycleAsync(CancellationToken.None);
        var position = await _db.PaperPositions.SingleAsync();
        _broker.RejectNext = true;

        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => Service().ClosePositionAsync(position.Id, "caio", CancellationToken.None));

        Assert.True(_state.IsHalted);
        Assert.Single(await _db.PaperPositions.ToListAsync());

        // Travado, a saída continua permitida: a nova tentativa encerra.
        await Service().ClosePositionAsync(position.Id, "caio", CancellationToken.None);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
    }

    [Fact]
    public async Task No_mt5_duas_alocacoes_nao_abrem_posicao_no_mesmo_contrato()
    {
        await AssignInDemoAsync();
        var second = new StrategyAssignment("WIN", StrategyKeys.SmaCross, new Dictionary<string, decimal>(), Timeframe.M5, false, false, _clock.GetUtcNow());
        second.Enable();
        _db.StrategyAssignments.Add(second);
        await _db.SaveChangesAsync();
        await Service().RunCycleAsync(CancellationToken.None);

        // Força a segunda alocação a querer entrar no mesmo contrato numa barra nova.
        _clock.Advance(TimeSpan.FromMinutes(5));
        var buyAndHold = await _db.StrategyAssignments.SingleAsync(a => a.StrategyKey == StrategyKeys.BuyAndHold);
        _db.StrategyAssignments.Remove(buyAndHold);
        var another = new StrategyAssignment("WIN", StrategyKeys.BuyAndHold, new Dictionary<string, decimal>(), Timeframe.M15, false, false, _clock.GetUtcNow());
        another.Enable();
        _db.StrategyAssignments.Add(another);
        await _db.SaveChangesAsync();

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Single(_broker.Positions);
        Assert.Contains(report.Messages, m => m.Contains("já existe posição do robô neste contrato", StringComparison.Ordinal));
    }

    // ---------- Registro de decisões ----------

    [Fact]
    public async Task Entrada_fica_registrada_com_a_conta_do_tamanho()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);

        await Service().RunCycleAsync(CancellationToken.None);

        var decision = Assert.Single(await _db.TradingDecisions.ToListAsync());
        Assert.Equal(DecisionOutcome.Entered, decision.Outcome);
        Assert.Equal("WINZ26", decision.Contract);
        Assert.Equal(PositionDirection.Long, decision.Signal);
        Assert.Equal(2, decision.Quantity);
        Assert.Contains("teto de 2 contratos", decision.Sizing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entrada_bloqueada_e_posicao_mantida_ficam_registradas_com_o_motivo()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _state.Halt("teste", "caio", _clock.GetUtcNow());
        await Service().RunCycleAsync(CancellationToken.None);

        _state.Resume("caio", _clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Service().RunCycleAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Service().RunCycleAsync(CancellationToken.None);

        var decisions = await _db.TradingDecisions.OrderBy(d => d.Id).ToListAsync();
        Assert.Equal([DecisionOutcome.Blocked, DecisionOutcome.Entered, DecisionOutcome.Held], decisions.Select(d => d.Outcome));
        Assert.Contains("robô travado", decisions[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cotacoes_do_painel_trazem_contrato_variacao_e_serie_do_dia()
    {
        await AssignAsync("PETR4", StrategyKeys.BuyAndHold);
        var service = new MarketWatchService(_feed, new AssetRepository(_db), new StrategyAssignmentRepository(_db),
            Options.Create(new PaperTradingOptions()), _clock);

        var watch = await service.GetAsync(CancellationToken.None);

        Assert.True(watch.Connected);
        Assert.Equal(["WIN", "WDO", "IBOV", "PETR4"], watch.Items.Select(i => i.Symbol));
        var win = watch.Items[0];
        Assert.Equal("WINZ26", win.Contract);
        Assert.True(win.IsFresh);
        Assert.NotEmpty(win.Spark);
        Assert.Equal(130_000m, win.Last);

        _feed.Connected = false;
        var offline = await service.GetAsync(CancellationToken.None);
        Assert.False(offline.Connected);
        Assert.Empty(offline.Items);
    }

    // ---------- Robustez: concorrência, reinício, emergência, rastreabilidade ----------

    [Fact]
    public async Task Dois_ciclos_simultaneos_nao_enviam_a_mesma_ordem_duas_vezes()
    {
        await AssignInDemoAsync();
        _feed.Delay = TimeSpan.FromMilliseconds(300);
        await using var first = NewContext();
        await using var second = NewContext();

        var reports = await Task.WhenAll(Service(first).RunCycleAsync(CancellationToken.None), Service(second).RunCycleAsync(CancellationToken.None));

        Assert.Single(_broker.Sent);
        Assert.Contains(reports, r => r.Messages.Contains("Outro ciclo está em andamento; este foi ignorado."));
    }

    [Fact]
    public async Task Entrada_grava_intencao_antes_do_envio_com_slippage_e_latencia()
    {
        await AssignInDemoAsync();

        await Service().RunCycleAsync(CancellationToken.None);

        var intent = Assert.Single(await _db.OrderIntents.ToListAsync());
        Assert.Equal(Domain.Execution.OrderIntentStatus.Filled, intent.Status);
        Assert.Equal(130_005m, intent.ReferencePrice);
        Assert.Equal(5m, intent.Slippage);
        Assert.NotNull(intent.LatencyMs);
        var decision = await _db.TradingDecisions.SingleAsync();
        Assert.Equal(intent.ClientOrderId, decision.ClientOrderId);
        Assert.Equal(intent.PositionId, (await _db.PaperPositions.SingleAsync()).Id);
    }

    [Fact]
    public async Task Apos_reinicio_entrada_executada_na_corretora_e_adotada_sem_reenviar()
    {
        var assignment = await AssignInDemoAsync();
        // Simula a queda: a intenção foi gravada e a ordem executou na corretora, mas o livro não registrou.
        var intent = new Domain.Execution.OrderIntent(Domain.Execution.ExecutionMode.Mt5Demo, "e-queda-0001", Domain.Execution.OrderPurpose.Entry, "WINZ26",
            Domain.Trading.OrderSide.Buy, 2, 130_000m, 129_800m, assignment.Id, null, StrategyKeys.BuyAndHold, "entrada", _clock.GetUtcNow());
        _db.OrderIntents.Add(intent);
        await _db.SaveChangesAsync();
        _broker.Positions.Add(new BrokerPosition(777, "WINZ26", Domain.Trading.OrderSide.Buy, 2, 130_010m, 129_800m, true));
        assignment.MarkProcessed(_clock.GetUtcNow());
        await _db.SaveChangesAsync();

        var report = await Service().RunCycleAsync(CancellationToken.None);

        Assert.Empty(_broker.Sent);
        Assert.False(_state.IsHalted);
        var position = await _db.PaperPositions.SingleAsync();
        Assert.Equal(777, position.BrokerTicket);
        Assert.Equal(130_010m, position.EntryPrice);
        Assert.Equal(Domain.Execution.OrderIntentStatus.Recovered, (await _db.OrderIntents.SingleAsync()).Status);
        Assert.Contains(report.Messages, m => m.Contains("adotada pelo livro", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apos_reinicio_entrada_sem_posicao_na_corretora_vira_nao_confirmada()
    {
        var assignment = await AssignInDemoAsync();
        _db.OrderIntents.Add(new Domain.Execution.OrderIntent(Domain.Execution.ExecutionMode.Mt5Demo, "e-queda-0002", Domain.Execution.OrderPurpose.Entry, "WINZ26",
            Domain.Trading.OrderSide.Buy, 2, 130_000m, null, assignment.Id, null, StrategyKeys.BuyAndHold, "entrada", _clock.GetUtcNow()));
        assignment.MarkProcessed(_clock.GetUtcNow());
        await _db.SaveChangesAsync();

        await Service().RunCycleAsync(CancellationToken.None);

        Assert.Empty(_broker.Sent);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
        Assert.Equal(Domain.Execution.OrderIntentStatus.Unconfirmed, (await _db.OrderIntents.SingleAsync()).Status);
    }

    [Fact]
    public async Task Zerar_tudo_trava_e_encerra_todas_as_posicoes_do_modo()
    {
        await AssignInDemoAsync();
        await Service().RunCycleAsync(CancellationToken.None);

        var report = await Service().FlattenAllAsync("caio", CancellationToken.None);

        Assert.True(_state.IsHalted);
        Assert.Equal(1, report.Closed);
        Assert.Empty(await _db.PaperPositions.ToListAsync());
        Assert.Empty(_broker.Positions);
        Assert.Contains(await _db.AuditEntries.ToListAsync(), a => a.Action == "emergencia.zerar-tudo");
    }

    [Fact]
    public async Task Decisao_guarda_parametros_contexto_e_versao_do_codigo()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        _feed.Price = 130_000m;

        await Service().RunCycleAsync(CancellationToken.None);

        var decision = await _db.TradingDecisions.SingleAsync();
        Assert.Equal(Domain.Learning.FeatureVector.Count, decision.Features.Count);
        Assert.False(string.IsNullOrWhiteSpace(decision.CodeVersion));
        Assert.NotNull(decision.Parameters);
    }

    [Fact]
    public async Task Excursoes_maxima_e_minima_vao_para_o_diario()
    {
        await AssignAsync("WIN", StrategyKeys.BuyAndHold);
        await Service().RunCycleAsync(CancellationToken.None);
        _feed.Price = 130_200m;
        await Service().RunCycleAsync(CancellationToken.None);
        _feed.Price = 129_900m;
        await Service().RunCycleAsync(CancellationToken.None);

        await Service().ClosePositionAsync((await _db.PaperPositions.SingleAsync()).Id, "caio", CancellationToken.None);

        var entry = await _db.TradeJournal.SingleAsync();
        Assert.Equal(Math.Round(195m / 130_005m * 100m, 3), entry.MfePercent);
        // A saída manual é no bid (129.895): 110 pontos abaixo da entrada.
        Assert.Equal(Math.Round(110m / 130_005m * 100m, 3), entry.MaePercent);
    }
}

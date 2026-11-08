using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Application.MarketData;
using RoboB3.Application.Paper;
using RoboB3.Domain.Execution;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Trading;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

/// <summary>Serviços das telas de Histórico e Gráfico.</summary>
public class ScreensTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 17, 0, 0, TimeSpan.Zero));
    private readonly RoboB3DbContext _db = new(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Historico_junta_entrada_e_saida_de_cada_posicao_em_todos_os_modos()
    {
        var now = _clock.GetUtcNow();
        var win = Guid.NewGuid();
        var loss = Guid.NewGuid();
        var open = Guid.NewGuid();
        _db.PaperFills.AddRange(
            new PaperFill(win, "WINZ26", OrderSide.Buy, 1, 130_000m, 1m, null, "entrada", now.AddMinutes(-30), ExecutionMode.Mt5Demo, "ema-vwap"),
            new PaperFill(win, "WINZ26", OrderSide.Sell, 1, 130_200m, 1m, 38m, "alvo", now.AddMinutes(-10), ExecutionMode.Mt5Demo, "ema-vwap"),
            new PaperFill(loss, "PETR4", OrderSide.Buy, 100, 36m, 1m, null, "entrada", now.AddHours(-3)),
            new PaperFill(loss, "PETR4", OrderSide.Sell, 100, 35m, 1m, -102m, "stop", now.AddHours(-2)),
            new PaperFill(open, "WDOZ26", OrderSide.Sell, 1, 5_400m, 1m, null, "entrada", now));
        await _db.SaveChangesAsync();

        var history = await new TradeHistoryService(new PaperRepository(_db)).ListAsync(null, null, 100, CancellationToken.None);

        Assert.Equal(2, history.Trades.Count);
        var first = history.Trades[0];
        Assert.Equal(("WINZ26", "ema-vwap", "Compra", "alvo", ExecutionMode.Mt5Demo), (first.Symbol, first.Strategy, first.Direction, first.ExitReason, first.Mode));
        Assert.Equal(130_000m, first.EntryPrice);
        Assert.Equal(2m, first.Costs);
        Assert.Equal(-64m, history.NetProfit);
        Assert.Equal(50m, history.WinRatePercent);
        Assert.Equal(0.37m, history.ProfitFactor);

        var onlyWin = await new TradeHistoryService(new PaperRepository(_db)).ListAsync(ExecutionMode.Mt5Demo, "WIN", 100, CancellationToken.None);
        Assert.Single(onlyWin.Trades);
    }

    [Fact]
    public async Task Grafico_traz_candles_medias_vwap_leitura_e_execucoes_no_horario_de_brasilia()
    {
        await new DatabaseInitializer(_db, _clock, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(false, CancellationToken.None);
        var feed = new FakeFeed(_clock);
        _db.PaperFills.Add(new PaperFill(Guid.NewGuid(), "WINZ26", OrderSide.Buy, 1, 129_990m, 1m, null, "entrada", _clock.GetUtcNow().AddMinutes(-30)));
        await _db.SaveChangesAsync();
        var service = new ChartService(feed, new AssetRepository(_db), new CandleRepository(_db), new PaperRepository(_db),
            new FakeExecutionSettings(new ExecutionSettings(_clock.GetUtcNow())), _clock);

        var chart = await service.GetAsync("WIN", Timeframe.M5, 120, CancellationToken.None);

        Assert.Equal("WINZ26", chart.Contract);
        Assert.Equal(120, chart.Candles.Count);
        Assert.Equal(101, chart.Sma20.Count);
        Assert.Equal(71, chart.Sma50.Count);
        Assert.NotEmpty(chart.Vwap);
        Assert.Single(chart.Markers);
        Assert.Equal("alta", chart.Reading!.Trend);
        Assert.NotNull(chart.Reading.Adx14);

        // 17h UTC = 14h em Brasília: o gráfico recebe o relógio local.
        var last = DateTimeOffset.FromUnixTimeSeconds(chart.Candles[^1].Time);
        Assert.Equal(B3Calendar.ToLocal(_clock.GetUtcNow()).Hour, last.Hour);
    }

    [Fact]
    public async Task Intradiario_sem_mt5_explica_em_vez_de_inventar_candles()
    {
        await new DatabaseInitializer(_db, _clock, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(false, CancellationToken.None);
        var feed = new FakeFeed(_clock) { Connected = false };
        var service = new ChartService(feed, new AssetRepository(_db), new CandleRepository(_db), new PaperRepository(_db),
            new FakeExecutionSettings(new ExecutionSettings(_clock.GetUtcNow())), _clock);

        var ex = await Assert.ThrowsAsync<Domain.Common.DomainException>(() => service.GetAsync("WIN", Timeframe.M5, 120, CancellationToken.None));
        Assert.Contains("MetaTrader 5", ex.Message, StringComparison.Ordinal);
    }
}

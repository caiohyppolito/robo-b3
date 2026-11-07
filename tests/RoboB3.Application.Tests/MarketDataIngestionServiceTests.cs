using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RoboB3.Application.Abstractions;
using RoboB3.Application.MarketData;
using RoboB3.Domain.MarketData;

namespace RoboB3.Application.Tests;

public class MarketDataIngestionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 22, 0, 0, TimeSpan.Zero);
    private readonly IAssetRepository _assets = Substitute.For<IAssetRepository>();
    private readonly ICandleRepository _candles = Substitute.For<ICandleRepository>();
    private readonly IMarketDataProvider _provider = Substitute.For<IMarketDataProvider>();

    private MarketDataIngestionService CreateService() =>
        new(_assets, _candles, _provider, new FakeTimeProvider(Now), NullLogger<MarketDataIngestionService>.Instance);

    [Fact]
    public async Task Primeira_carga_baixa_historico_completo()
    {
        var petr = new Asset("PETR4", "Petrobras", AssetClass.Stock, "PETR4.SA", 100);
        _assets.ListAsync(true, Arg.Any<CancellationToken>()).Returns([petr]);
        _candles.GetLastOpenTimeAsync("PETR4", Timeframe.D1, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        _provider.GetDailyCandlesAsync(petr, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new Candle("PETR4", Timeframe.D1, Now.Date, 10m, 11m, 9m, 10m, 100)]);
        _candles.UpsertAsync(Arg.Any<IReadOnlyCollection<Candle>>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await CreateService().IngestDailyAsync(CancellationToken.None);

        Assert.Equal(1, result.CandlesSaved);
        await _provider.Received(1).GetDailyCandlesAsync(
            petr, new DateOnly(2021, 11, 6), new DateOnly(2026, 11, 6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carga_incremental_comeca_alguns_dias_antes_do_ultimo_candle()
    {
        var petr = new Asset("PETR4", "Petrobras", AssetClass.Stock, "PETR4.SA", 100);
        _assets.ListAsync(true, Arg.Any<CancellationToken>()).Returns([petr]);
        _candles.GetLastOpenTimeAsync("PETR4", Timeframe.D1, Arg.Any<CancellationToken>())
            .Returns(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        _provider.GetDailyCandlesAsync(default!, default, default, default).ReturnsForAnyArgs([]);

        await CreateService().IngestDailyAsync(CancellationToken.None);

        await _provider.Received(1).GetDailyCandlesAsync(
            petr, new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignora_ativos_sem_fonte_diaria_e_isola_falhas()
    {
        var win = new Asset("WIN", "Mini Índice", AssetClass.Future, null, 1);
        var vale = new Asset("VALE3", "Vale", AssetClass.Stock, "VALE3.SA", 100);
        var itub = new Asset("ITUB4", "Itaú", AssetClass.Stock, "ITUB4.SA", 100);
        _assets.ListAsync(true, Arg.Any<CancellationToken>()).Returns([win, vale, itub]);
        _provider.GetDailyCandlesAsync(vale, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));
        _provider.GetDailyCandlesAsync(itub, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateService().IngestDailyAsync(CancellationToken.None);

        Assert.Equal(1, result.AssetsProcessed);
        Assert.Single(result.Failures);
        Assert.StartsWith("VALE3", result.Failures[0], StringComparison.Ordinal);
        await _provider.DidNotReceive().GetDailyCandlesAsync(win, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}

using RoboB3.Domain.MarketData;
using RoboB3.Infrastructure.MarketData;

namespace RoboB3.Infrastructure.Tests;

public class YahooChartParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Converte_resposta_em_candles_e_ignora_pregao_sem_dados()
    {
        var candles = YahooChartParser.ParseDaily("PETR4", Fixture("yahoo-petr4.json"));

        Assert.Equal(2, candles.Count);
        var first = candles[0];
        Assert.Equal("PETR4", first.Symbol);
        Assert.Equal(Timeframe.D1, first.Timeframe);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), first.OpenTime);
        Assert.Equal(32.10m, first.Open);
        Assert.Equal(32.45m, first.Close);
        Assert.Equal(41_234_500, first.Volume);
    }

    [Fact]
    public void Lanca_erro_quando_yahoo_retorna_erro()
    {
        const string json = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""";

        var ex = Assert.Throws<InvalidOperationException>(() => YahooChartParser.ParseDaily("XXXX3", json));

        Assert.Contains("No data found", ex.Message, StringComparison.Ordinal);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoboB3.Application.MarketData;
using RoboB3.Domain.MarketData;
using RoboB3.Infrastructure.MarketData.Bcb;
using RoboB3.Infrastructure.Persistence;
using RoboB3.Infrastructure.Persistence.Repositories;

namespace RoboB3.Infrastructure.Tests;

public class CdiTests
{
    [Fact]
    public void Le_a_resposta_do_sgs_com_texto_ou_numero_e_virgula()
    {
        const string json = """[{"data":"03/11/2026","valor":"0.055131"},{"data":"04/11/2026","valor":"0,055131"},{"data":"05/11/2026","valor":0.05}]""";

        var rates = BcbCdiProvider.Parse(json);

        Assert.Equal(3, rates.Count);
        Assert.Equal(new DateOnly(2026, 11, 3), rates[0].Date);
        Assert.Equal(0.055131m, rates[0].DailyPercent);
        Assert.Equal(0.055131m, rates[1].DailyPercent);
        Assert.Equal(14.90m, rates[0].AnnualizedPercent);
    }

    [Fact]
    public void Resposta_fora_do_formato_falha_em_vez_de_inventar_taxa()
    {
        Assert.ThrowsAny<Exception>(() => BcbCdiProvider.Parse("""{"erro":"limite"}"""));
        Assert.ThrowsAny<Exception>(() => BcbCdiProvider.Parse("""[{"data":"03/11/2026","valor":"55"}]"""));
    }

    private sealed class FakeProvider : ICdiProvider
    {
        public List<(DateOnly From, DateOnly To)> Calls { get; } = [];

        public Task<IReadOnlyList<CdiRate>> GetDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Calls.Add((from, to));
            IReadOnlyList<CdiRate> rates = [new(to.AddDays(-1), 0.05m), new(to, 0.05m)];
            return Task.FromResult(rates);
        }
    }

    [Fact]
    public async Task Sincronizacao_baixa_tudo_na_primeira_vez_e_so_o_que_falta_depois()
    {
        await using var db = new RoboB3DbContext(new DbContextOptionsBuilder<RoboB3DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero));
        var provider = new FakeProvider();
        var service = new RiskFreeRateService(provider, new CdiRepository(db), clock, NullLogger<RiskFreeRateService>.Instance);

        Assert.Equal(2, await service.SyncAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, await service.SyncAsync(CancellationToken.None));

        Assert.Equal(RiskFreeRateService.SeriesStart, provider.Calls[0].From);
        Assert.Equal(new DateOnly(2026, 11, 10), provider.Calls[1].From);
        Assert.Equal(3, await db.CdiRates.CountAsync());
        Assert.NotNull(await service.GetCurveAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), 10m, CancellationToken.None));
    }

    [Fact]
    public async Task Consulta_vai_em_janelas_de_ate_cinco_anos()
    {
        var handler = new CountingHandler();
        var provider = new BcbCdiProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.bcb.gov.br/") });

        await provider.GetDailyAsync(new DateOnly(2000, 1, 3), new DateOnly(2026, 11, 9), CancellationToken.None);

        Assert.Equal(6, handler.Urls.Count);
        Assert.All(handler.Urls, u => Assert.Contains("bcdata.sgs.12/dados?formato=json&dataInicial=", u, StringComparison.Ordinal));
        Assert.Contains("dataFinal=09/11/2026", handler.Urls[^1], StringComparison.Ordinal);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}

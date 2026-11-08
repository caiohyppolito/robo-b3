using System.Globalization;
using System.Text.Json;
using RoboB3.Application.MarketData;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.MarketData.Bcb;

/// <summary>
/// CDI diário pela API pública do SGS do Banco Central (série 12, % ao dia), sem chave.
/// A API limita o intervalo das séries diárias por consulta, então os pedidos vão em janelas de até 5 anos.
/// </summary>
internal sealed class BcbCdiProvider(HttpClient http) : ICdiProvider
{
    public const int Series = 12;

    public async Task<IReadOnlyList<CdiRate>> GetDailyAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = new List<CdiRate>();
        for (var start = from; start <= to; start = start.AddYears(5))
        {
            var end = start.AddYears(5).AddDays(-1);
            if (end > to)
            {
                end = to;
            }

            var url = $"dados/serie/bcdata.sgs.{Series}/dados?formato=json&dataInicial={start:dd/MM/yyyy}&dataFinal={end:dd/MM/yyyy}";
            using var response = await http.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);

            // Sem dias úteis no intervalo, o SGS responde 404 em vez de lista vazia.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            result.AddRange(Parse(await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        return result;
    }

    /// <summary>Lê <c>[{"data":"dd/MM/aaaa","valor":"0.055131"}]</c>; aceita valor como texto ou número, com ponto ou vírgula.</summary>
    internal static IReadOnlyList<CdiRate> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Resposta do Banco Central fora do formato esperado (não é uma lista).");
        }

        var rates = new List<CdiRate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var date = DateOnly.ParseExact(item.GetProperty("data").GetString()!, "dd/MM/yyyy", CultureInfo.InvariantCulture);
            var raw = item.GetProperty("valor");
            var value = raw.ValueKind == JsonValueKind.Number
                ? raw.GetDecimal()
                : decimal.Parse(raw.GetString()!.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);
            rates.Add(new CdiRate(date, value));
        }

        return rates;
    }
}

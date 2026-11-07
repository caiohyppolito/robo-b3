using System.Text.Json;
using RoboB3.Domain.MarketData;

namespace RoboB3.Infrastructure.MarketData;

/// <summary>Converte a resposta JSON do endpoint "chart" do Yahoo Finance em candles diários.</summary>
internal static class YahooChartParser
{
    public static IReadOnlyList<Candle> ParseDaily(string symbol, string json)
    {
        using var document = JsonDocument.Parse(json);
        var chart = document.RootElement.GetProperty("chart");

        if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            var description = error.TryGetProperty("description", out var d) ? d.GetString() : error.ToString();
            throw new InvalidOperationException($"Yahoo retornou erro para {symbol}: {description}");
        }

        var result = chart.GetProperty("result")[0];
        if (!result.TryGetProperty("timestamp", out var timestamps))
        {
            return [];
        }

        var gmtOffset = result.GetProperty("meta").TryGetProperty("gmtoffset", out var offset) ? offset.GetInt32() : 0;
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var candles = new List<Candle>(timestamps.GetArrayLength());
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            // Pregões sem negócio vêm com valores nulos: são ignorados.
            if (!TryGetDecimal(opens[i], out var open)
                || !TryGetDecimal(highs[i], out var high)
                || !TryGetDecimal(lows[i], out var low)
                || !TryGetDecimal(closes[i], out var close))
            {
                continue;
            }

            var volume = volumes[i].ValueKind == JsonValueKind.Number ? volumes[i].GetInt64() : 0;

            // A chave do candle diário é a data do pregão no fuso da B3, gravada como meia-noite UTC.
            var localDate = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64() + gmtOffset).UtcDateTime.Date;
            var openTime = new DateTimeOffset(localDate, TimeSpan.Zero);

            candles.Add(new Candle(symbol, Timeframe.D1, openTime, open, Math.Max(high, Math.Max(open, close)),
                Math.Min(low, Math.Min(open, close)), close, volume));
        }

        return candles;
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = Math.Round(element.GetDecimal(), 6);
        return value > 0;
    }
}

using RoboB3.Domain.Common;

namespace RoboB3.Domain.Strategies;

public static class StrategyKeys
{
    public const string BuyAndHold = "buy-and-hold";
    public const string SmaCross = "sma-cross";
    public const string DonchianBreakout = "donchian-breakout";
    public const string RsiMeanReversion = "rsi-mean-reversion";
    public const string EmaVwap = "ema-vwap";
}

public sealed record StrategyDefinition(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<ParameterDefinition> Parameters,
    Func<StrategyParameters, IStrategy> Factory)
{
    public StrategyParameters DefaultParameters() =>
        new(Parameters.ToDictionary(p => p.Name, p => p.Default));

    /// <summary>Valida os parâmetros e completa os ausentes com o padrão.</summary>
    public StrategyParameters Normalize(IReadOnlyDictionary<string, decimal>? values)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in Parameters)
        {
            var value = values is not null && values.TryGetValue(parameter.Name, out var v) ? v : parameter.Default;
            if (value < parameter.Min || value > parameter.Max)
            {
                throw new DomainException($"Parâmetro '{parameter.Name}' deve estar entre {parameter.Min} e {parameter.Max}.");
            }

            result[parameter.Name] = value;
        }

        if (values is not null)
        {
            var unknown = values.Keys.Where(k => !result.ContainsKey(k)).ToList();
            if (unknown.Count > 0)
            {
                throw new DomainException($"Parâmetros desconhecidos para {Key}: {string.Join(", ", unknown)}.");
            }
        }

        return new StrategyParameters(result);
    }

    public IStrategy Create(StrategyParameters parameters) => Factory(parameters);

    /// <summary>Todas as combinações da grade de otimização.</summary>
    public IEnumerable<StrategyParameters> Grid()
    {
        IEnumerable<Dictionary<string, decimal>> combos = [new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)];
        foreach (var parameter in Parameters)
        {
            combos = combos.SelectMany(c => parameter.Grid.Select(value => new Dictionary<string, decimal>(c, StringComparer.OrdinalIgnoreCase) { [parameter.Name] = value }));
        }

        return combos.Select(c => new StrategyParameters(c));
    }
}

/// <summary>Catálogo das estratégias disponíveis, com parâmetros padrão e grade de otimização.</summary>
public static class StrategyCatalog
{
    public static IReadOnlyList<StrategyDefinition> All { get; } =
    [
        new(
            StrategyKeys.BuyAndHold,
            "Comprar e segurar",
            "Referência: compra no início e segura até o fim. Toda estratégia precisa vencer esta.",
            [],
            _ => new BuyAndHoldStrategy()),
        new(
            StrategyKeys.SmaCross,
            "Cruzamento de médias",
            "Tendência: comprado com a média rápida acima da lenta. Stop em ATRs.",
            [
                new("fast", "Média rápida (dias)", 20, 2, 100, [10, 20, 50]),
                new("slow", "Média lenta (dias)", 100, 10, 300, [100, 200]),
                new("atrStop", "Stop (múltiplos de ATR)", 3, 0.5m, 10, [2, 3]),
                new("adxMin", "ADX mínimo para entrar (0 = sem filtro)", 0, 0, 60, [0, 25]),
            ],
            p => new SmaCrossStrategy(p.GetInt("fast"), p.GetInt("slow"), p.Get("atrStop"), p.Get("adxMin"))),
        new(
            StrategyKeys.DonchianBreakout,
            "Rompimento de canal",
            "Momentum: entra no rompimento da máxima/mínima de N dias, sai no rompimento de um canal mais curto.",
            [
                new("entry", "Canal de entrada (dias)", 55, 5, 250, [20, 55]),
                new("exit", "Canal de saída (dias)", 20, 2, 100, [10, 20]),
                new("atrStop", "Stop (múltiplos de ATR)", 2, 0.5m, 10, [2, 3]),
                new("adxMin", "ADX mínimo para entrar (0 = sem filtro)", 0, 0, 60, [0, 25]),
            ],
            p => new DonchianBreakoutStrategy(p.GetInt("entry"), p.GetInt("exit"), p.Get("atrStop"), p.Get("adxMin"))),
        new(
            StrategyKeys.RsiMeanReversion,
            "Reversão à média (RSI)",
            "Compra quedas fortes dentro de tendência de alta e sai no repique.",
            [
                new("rsiPeriod", "Período do RSI (dias)", 2, 2, 30, [2, 3]),
                new("entryLevel", "RSI de entrada (abaixo de)", 10, 1, 50, [5, 10, 20]),
                new("trend", "Média de tendência (dias)", 200, 20, 300, [100, 200]),
                new("maxBars", "Máximo de dias na posição", 10, 1, 60, [10]),
            ],
            p => new RsiMeanReversionStrategy(p.GetInt("rsiPeriod"), p.Get("entryLevel"), p.GetInt("trend"), p.GetInt("maxBars"))),
        new(
            StrategyKeys.EmaVwap,
            "EMA × SMA com VWAP (intradiário)",
            "WIN em 5 min: cruzamento da EMA rápida sobre a SMA lenta confirmado pela VWAP do dia; stop e alvo fixos em pontos. Use com day trade.",
            [
                new("fast", "EMA rápida (barras)", 9, 2, 50, [9]),
                new("slow", "SMA lenta (barras)", 21, 5, 100, [21, 34]),
                new("stopPoints", "Stop (pontos)", 100, 5, 2000, [100, 150]),
                new("targetPoints", "Alvo (pontos)", 200, 5, 5000, [200, 300]),
                new("startHhmm", "Entradas a partir de (HHMM)", 915, 900, 1800, [915]),
                new("endHhmm", "Entradas até (HHMM)", 1700, 900, 1800, [1700]),
            ],
            p => new EmaVwapStrategy(p.GetInt("fast"), p.GetInt("slow"), p.Get("stopPoints"), p.Get("targetPoints"), p.GetInt("startHhmm"), p.GetInt("endHhmm"))),
    ];

    public static StrategyDefinition Get(string key) =>
        All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainException($"Estratégia '{key}' não existe.");
}

using System.Globalization;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Common;

namespace RoboB3.Domain.Strategies;

public enum PositionDirection
{
    Short = -1,
    Flat = 0,
    Long = 1,
}

/// <summary>
/// Posição desejada após o fechamento da barra atual, com stop e alvo opcionais.
/// Com <paramref name="EntryStop"/>, a entrada só acontece se o preço tocar esse nível durante a barra seguinte
/// (ordem stop de entrada: compra acima, venda abaixo); se não tocar, a ordem expira.
/// </summary>
public sealed record StrategySignal(PositionDirection Target, decimal? StopPrice = null, decimal? TargetPrice = null, decimal? EntryStop = null)
{
    public static StrategySignal Flat { get; } = new(PositionDirection.Flat);
}

/// <summary>Contexto da posição atual, entregue à estratégia a cada barra.</summary>
public sealed record PositionContext(PositionDirection Direction, decimal? EntryPrice, int BarsHeld)
{
    public static PositionContext None { get; } = new(PositionDirection.Flat, null, 0);
}

/// <summary>Estratégia de trading: decide a posição-alvo olhando só o passado.</summary>
public interface IStrategy
{
    string Key { get; }

    /// <summary>Barras necessárias antes do primeiro sinal válido.</summary>
    int WarmupBars { get; }

    StrategySignal Evaluate(History history, PositionContext position);
}

public sealed record ParameterDefinition(string Name, string Description, decimal Default, decimal Min, decimal Max, IReadOnlyList<decimal> Grid);

/// <summary>Parâmetros nomeados de uma estratégia, validados contra as definições.</summary>
public sealed class StrategyParameters
{
    private readonly Dictionary<string, decimal> _values;

    public StrategyParameters(IReadOnlyDictionary<string, decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, decimal>(values, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, decimal> Values => _values;

    public decimal Get(string name) =>
        _values.TryGetValue(name, out var value) ? value : throw new DomainException($"Parâmetro '{name}' não informado.");

    public int GetInt(string name) => (int)Get(name);

    public override string ToString() =>
        string.Join(", ", _values.OrderBy(kv => kv.Key).Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value}")));
}

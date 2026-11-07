using RoboB3.Domain.MarketData;

namespace RoboB3.Domain.Backtesting;

/// <summary>Características de negociação de um ativo.</summary>
public sealed record InstrumentSpec(string Symbol, AssetClass AssetClass, int LotSize, decimal Multiplier, bool AllowShort)
{
    public bool IsFuture => AssetClass == AssetClass.Future;

    /// <summary>
    /// Variação mínima de preço: o mini índice anda de 5 em 5 pontos, o mini dólar de 0,5 e as ações de R$ 0,01.
    /// Preço fora do tick é recusado pela corretora.
    /// </summary>
    public decimal TickSize => Symbol switch
    {
        var s when s.StartsWith("WIN", StringComparison.Ordinal) => 5m,
        var s when s.StartsWith("WDO", StringComparison.Ordinal) => 0.5m,
        _ => 0.01m,
    };

    /// <summary>Aproxima o preço para o tick mais próximo, para a corretora aceitar a ordem ou o stop.</summary>
    public decimal RoundToTick(decimal price) => Math.Round(price / TickSize, MidpointRounding.AwayFromZero) * TickSize;

    /// <summary>
    /// Mini índice: R$ 0,20 por ponto. Mini dólar: R$ 10,00 por ponto.
    /// Venda a descoberto só em futuros (em ações exigiria aluguel).
    /// </summary>
    public static InstrumentSpec For(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var multiplier = asset.Symbol switch
        {
            var s when s.StartsWith("WIN", StringComparison.Ordinal) => 0.20m,
            var s when s.StartsWith("WDO", StringComparison.Ordinal) => 10m,
            _ => 1m,
        };
        return new InstrumentSpec(asset.Symbol, asset.AssetClass, asset.LotSize, multiplier, asset.IsFuture);
    }
}

using RoboB3.Domain.MarketData;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Risk;

/// <summary>
/// Regras de risco puras (sem I/O). Toda ordem passa por aqui antes de ir para a corretora.
/// </summary>
public static class RiskManager
{
    /// <summary>Retorna o motivo para travar o robô, ou nulo se os limites globais estão respeitados.</summary>
    public static string? FindGlobalBreach(PortfolioSnapshot portfolio, RiskLimits limits)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(limits);

        if (portfolio.DailyLossPercent >= limits.MaxDailyLossPercent)
        {
            return $"Perda diária de {portfolio.DailyLossPercent:F2}% atingiu o limite de {limits.MaxDailyLossPercent:F2}%.";
        }

        if (portfolio.DrawdownPercent >= limits.MaxDrawdownPercent)
        {
            return $"Drawdown de {portfolio.DrawdownPercent:F2}% atingiu o limite de {limits.MaxDrawdownPercent:F2}%.";
        }

        return null;
    }

    public static RiskDecision Evaluate(
        OrderRequest order,
        PortfolioSnapshot portfolio,
        RiskLimits limits,
        TradingState state)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(state);

        var violations = new List<string>();
        var current = portfolio.FindPosition(order.Symbol);
        var currentQuantity = current?.Quantity ?? 0;
        var resultingQuantity = currentQuantity + order.SignedQuantity;
        var reducesRisk = Math.Abs(resultingQuantity) < Math.Abs(currentQuantity)
            && Math.Sign(resultingQuantity) != -Math.Sign(currentQuantity);

        // Ordens que só reduzem posição são sempre permitidas: zerar nunca pode ser bloqueado.
        if (reducesRisk)
        {
            return RiskDecision.Approved;
        }

        if (state.IsHalted)
        {
            violations.Add($"Robô travado: {state.HaltReason}");
        }

        var breach = FindGlobalBreach(portfolio, limits);
        if (breach is not null)
        {
            violations.Add(breach);
        }

        if (current is null && portfolio.Positions.Count >= limits.MaxOpenPositions)
        {
            violations.Add($"Limite de {limits.MaxOpenPositions} posições abertas atingido.");
        }

        if (order.AssetClass == AssetClass.Future)
        {
            var otherContracts = portfolio.Positions
                .Where(p => p.AssetClass == AssetClass.Future && p != current)
                .Sum(p => Math.Abs(p.Quantity));
            var totalContracts = otherContracts + Math.Abs(resultingQuantity);
            if (totalContracts > limits.MaxFuturesContracts)
            {
                violations.Add($"Total de {totalContracts} contratos futuros excede o limite de {limits.MaxFuturesContracts}.");
            }
        }
        else
        {
            var exposure = Math.Abs(resultingQuantity) * order.Price;
            var maxExposure = portfolio.Equity * limits.MaxPositionPercentPerAsset / 100m;
            if (exposure > maxExposure)
            {
                violations.Add(
                    $"Exposição de {exposure:N2} em {order.Symbol} excede {limits.MaxPositionPercentPerAsset:F2}% do patrimônio ({maxExposure:N2}).");
            }
        }

        return violations.Count == 0 ? RiskDecision.Approved : new RiskDecision(violations);
    }
}

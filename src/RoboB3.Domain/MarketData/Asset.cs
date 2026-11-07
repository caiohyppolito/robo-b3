using RoboB3.Domain.Common;

namespace RoboB3.Domain.MarketData;

/// <summary>Ativo negociável ou acompanhado (ação, ETF, índice ou contrato futuro).</summary>
public sealed class Asset
{
    private Asset()
    {
        Symbol = string.Empty;
        Name = string.Empty;
    }

    public Asset(string symbol, string name, AssetClass assetClass, string? dailyDataSymbol, int lotSize)
    {
        Symbol = Guard.NotBlank(symbol, nameof(symbol)).ToUpperInvariant();
        Name = Guard.NotBlank(name, nameof(name));
        AssetClass = assetClass;
        DailyDataSymbol = string.IsNullOrWhiteSpace(dailyDataSymbol) ? null : dailyDataSymbol.Trim();
        LotSize = Guard.Positive(lotSize, nameof(lotSize));
        IsActive = true;
    }

    /// <summary>Código na B3 (ex.: PETR4, BOVA11, WIN, WDO).</summary>
    public string Symbol { get; private set; }

    public string Name { get; private set; }

    public AssetClass AssetClass { get; private set; }

    /// <summary>Código no provedor de dados diários (ex.: PETR4.SA). Nulo quando os dados vêm só do MT5.</summary>
    public string? DailyDataSymbol { get; private set; }

    /// <summary>Lote padrão (100 para ações, 1 para futuros e lote fracionário).</summary>
    public int LotSize { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsFuture => AssetClass == AssetClass.Future;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

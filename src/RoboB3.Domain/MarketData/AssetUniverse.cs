namespace RoboB3.Domain.MarketData;

public static class AssetUniverse
{
    /// <summary>
    /// Universo amplo para a varredura: ações e ETFs líquidos da B3, cadastrados DESLIGADOS (sem baixar dados)
    /// até você ligá-los na tela de Varredura. Lista de partida: códigos que mudarem aparecem como "sem dados".
    /// </summary>
    public static IReadOnlyList<Asset> Liquid { get; } =
    [
        new("PETR3", "Petrobras ON", AssetClass.Stock, "PETR3.SA", 100),
        new("ITSA4", "Itaúsa PN", AssetClass.Stock, "ITSA4.SA", 100),
        new("BBSE3", "BB Seguridade ON", AssetClass.Stock, "BBSE3.SA", 100),
        new("BPAC11", "BTG Pactual Unit", AssetClass.Stock, "BPAC11.SA", 100),
        new("SANB11", "Santander Unit", AssetClass.Stock, "SANB11.SA", 100),
        new("SUZB3", "Suzano ON", AssetClass.Stock, "SUZB3.SA", 100),
        new("KLBN11", "Klabin Unit", AssetClass.Stock, "KLBN11.SA", 100),
        new("GGBR4", "Gerdau PN", AssetClass.Stock, "GGBR4.SA", 100),
        new("GOAU4", "Metalúrgica Gerdau PN", AssetClass.Stock, "GOAU4.SA", 100),
        new("CSNA3", "CSN ON", AssetClass.Stock, "CSNA3.SA", 100),
        new("USIM5", "Usiminas PNA", AssetClass.Stock, "USIM5.SA", 100),
        new("PRIO3", "PRIO ON", AssetClass.Stock, "PRIO3.SA", 100),
        new("RECV3", "PetroReconcavo ON", AssetClass.Stock, "RECV3.SA", 100),
        new("UGPA3", "Ultrapar ON", AssetClass.Stock, "UGPA3.SA", 100),
        new("VBBR3", "Vibra ON", AssetClass.Stock, "VBBR3.SA", 100),
        new("CSAN3", "Cosan ON", AssetClass.Stock, "CSAN3.SA", 100),
        new("EQTL3", "Equatorial ON", AssetClass.Stock, "EQTL3.SA", 100),
        new("CMIG4", "Cemig PN", AssetClass.Stock, "CMIG4.SA", 100),
        new("CPFE3", "CPFL Energia ON", AssetClass.Stock, "CPFE3.SA", 100),
        new("EGIE3", "Engie Brasil ON", AssetClass.Stock, "EGIE3.SA", 100),
        new("TAEE11", "Taesa Unit", AssetClass.Stock, "TAEE11.SA", 100),
        new("ENGI11", "Energisa Unit", AssetClass.Stock, "ENGI11.SA", 100),
        new("SBSP3", "Sabesp ON", AssetClass.Stock, "SBSP3.SA", 100),
        new("VIVT3", "Telefônica Brasil ON", AssetClass.Stock, "VIVT3.SA", 100),
        new("TIMS3", "TIM ON", AssetClass.Stock, "TIMS3.SA", 100),
        new("TOTS3", "Totvs ON", AssetClass.Stock, "TOTS3.SA", 100),
        new("RENT3", "Localiza ON", AssetClass.Stock, "RENT3.SA", 100),
        new("RAIL3", "Rumo ON", AssetClass.Stock, "RAIL3.SA", 100),
        new("LREN3", "Lojas Renner ON", AssetClass.Stock, "LREN3.SA", 100),
        new("MGLU3", "Magazine Luiza ON", AssetClass.Stock, "MGLU3.SA", 100),
        new("ASAI3", "Assaí ON", AssetClass.Stock, "ASAI3.SA", 100),
        new("RADL3", "Raia Drogasil ON", AssetClass.Stock, "RADL3.SA", 100),
        new("HAPV3", "Hapvida ON", AssetClass.Stock, "HAPV3.SA", 100),
        new("RDOR3", "Rede D'Or ON", AssetClass.Stock, "RDOR3.SA", 100),
        new("CYRE3", "Cyrela ON", AssetClass.Stock, "CYRE3.SA", 100),
        new("MULT3", "Multiplan ON", AssetClass.Stock, "MULT3.SA", 100),
        new("ALOS3", "Allos ON", AssetClass.Stock, "ALOS3.SA", 100),
        new("IVVB11", "iShares S&P 500 (em reais)", AssetClass.Etf, "IVVB11.SA", 1),
        new("DIVO11", "It Now Dividendos", AssetClass.Etf, "DIVO11.SA", 1),
    ];

    public static IReadOnlySet<string> Symbols { get; } = Liquid.Select(a => a.Symbol).ToHashSet();
}

using System.ComponentModel.DataAnnotations;

namespace RoboB3.Application.Paper;

/// <summary>Configuração da operação simulada (seção "PaperTrading"). Horários em Brasília.</summary>
public sealed class PaperTradingOptions
{
    public const string SectionName = "PaperTrading";

    [Range(0.1, 10)]
    public decimal RiskPerTradePercent { get; set; } = 1m;

    /// <summary>Cotação mais velha que isto é tratada como mercado fechado.</summary>
    [Range(10, 3600)]
    public int MaxQuoteAgeSeconds { get; set; } = 120;

    public TimeOnly NoEntriesBefore { get; set; } = new(10, 5);

    public TimeOnly NoEntriesAfter { get; set; } = new(17, 30);

    /// <summary>Horário em que alocações de day trade são zeradas.</summary>
    public TimeOnly DayTradeFlattenAt { get; set; } = new(17, 50);

    [Range(250, 5000)]
    public int BarsToLoad { get; set; } = 400;
}

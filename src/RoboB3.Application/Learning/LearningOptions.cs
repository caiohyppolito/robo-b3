using System.ComponentModel.DataAnnotations;

namespace RoboB3.Application.Learning;

/// <summary>Configuração do aprendizado (seção "Learning").</summary>
public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    /// <summary>Chance mínima de acerto estimada para um sinal passar.</summary>
    [Range(0.3, 0.9)]
    public double Threshold { get; set; } = 0.5;

    /// <summary>Fatia final das operações (no tempo) reservada para validar.</summary>
    [Range(10, 50)]
    public decimal ValidationPercent { get; set; } = 30m;

    [Range(10, 100_000)]
    public int MinTrainingExamples { get; set; } = 40;

    [Range(5, 100_000)]
    public int MinValidationExamples { get; set; } = 15;

    [Range(0.5, 1.0)]
    public double MinAuc { get; set; } = 0.55;

    [Range(0, 5)]
    public decimal MinSharpeImprovement { get; set; } = 0.05m;

    [Range(1, 100)]
    public decimal MinKeptPercent { get; set; } = 20m;

    /// <summary>Peso das operações reais/simuladas do diário em relação às de backtest.</summary>
    [Range(1, 20)]
    public float JournalWeight { get; set; } = 2f;
}

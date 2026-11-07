namespace RoboB3.Domain.Learning;

public sealed record SignalAssessment(double Probability, bool Allowed);

/// <summary>
/// Filtro de sinais da IA. Por desenho ele só pode vetar entradas: nunca cria operações,
/// nunca bloqueia saídas. No pior caso, o robô opera menos.
/// </summary>
public interface ISignalFilter
{
    SignalAssessment Assess(FeatureVector features);
}

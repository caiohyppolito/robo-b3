namespace RoboB3.Domain.Learning;

/// <summary>Modelo treinado que estima a chance de uma operação dar lucro.</summary>
public interface IProbabilityModel
{
    double Predict(FeatureVector features);
}

/// <summary>Aprova o sinal quando a chance estimada de acerto atinge o limiar.</summary>
public sealed class ThresholdSignalFilter(IProbabilityModel model, double threshold) : ISignalFilter
{
    public SignalAssessment Assess(FeatureVector features)
    {
        var probability = model.Predict(features);
        return new SignalAssessment(probability, probability >= threshold);
    }
}

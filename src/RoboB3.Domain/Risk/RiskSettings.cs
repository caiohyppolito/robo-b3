namespace RoboB3.Domain.Risk;

/// <summary>Registro único que guarda os limites de risco vigentes.</summary>
public sealed class RiskSettings
{
    public const int SingletonId = 1;

    private RiskSettings()
    {
        Limits = RiskLimits.Default;
    }

    public RiskSettings(RiskLimits limits, DateTimeOffset now)
    {
        Id = SingletonId;
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        UpdatedAt = now;
    }

    public int Id { get; private set; }

    public RiskLimits Limits { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(RiskLimits limits, DateTimeOffset now)
    {
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        UpdatedAt = now;
    }
}

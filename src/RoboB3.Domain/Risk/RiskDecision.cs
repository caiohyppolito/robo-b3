namespace RoboB3.Domain.Risk;

public sealed record RiskDecision(IReadOnlyList<string> Violations)
{
    public bool IsApproved => Violations.Count == 0;

    public static RiskDecision Approved { get; } = new([]);
}

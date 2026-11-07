using RoboB3.Domain.Common;

namespace RoboB3.Domain.Audit;

/// <summary>Registro imutável de ações sensíveis (kill switch, limites, login).</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        Action = string.Empty;
        Actor = string.Empty;
    }

    public AuditEntry(string action, string actor, string? details, DateTimeOffset occurredAt)
    {
        Action = Guard.NotBlank(action, nameof(action));
        Actor = Guard.NotBlank(actor, nameof(actor));
        Details = details;
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }

    public string Action { get; private set; }

    public string Actor { get; private set; }

    public string? Details { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

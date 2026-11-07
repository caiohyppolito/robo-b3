using RoboB3.Domain.Common;

namespace RoboB3.Domain.Execution;

/// <summary>Modo de execução vigente (registro único). Nasce no modo simulado.</summary>
public sealed class ExecutionSettings
{
    public const int SingletonId = 1;

    private ExecutionSettings()
    {
        ChangedBy = string.Empty;
    }

    public ExecutionSettings(DateTimeOffset now)
    {
        Id = SingletonId;
        Mode = ExecutionMode.Paper;
        ChangedBy = "sistema";
        ChangedAt = now;
    }

    public int Id { get; private set; }

    public ExecutionMode Mode { get; private set; }

    public string ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public void ChangeTo(ExecutionMode mode, string actor, DateTimeOffset now)
    {
        Mode = mode;
        ChangedBy = Guard.NotBlank(actor, nameof(actor));
        ChangedAt = now;
    }
}

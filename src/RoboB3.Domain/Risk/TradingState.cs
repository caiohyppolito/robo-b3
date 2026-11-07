using RoboB3.Domain.Common;

namespace RoboB3.Domain.Risk;

/// <summary>
/// Estado global de negociação (kill switch). Quando travado, nenhuma ordem nova é aceita.
/// Destravar sempre exige uma ação humana explícita.
/// </summary>
public sealed class TradingState
{
    public const int SingletonId = 1;

    private TradingState()
    {
    }

    private TradingState(DateTimeOffset now)
    {
        Id = SingletonId;
        IsHalted = true;
        HaltReason = "Estado inicial: libere manualmente após revisar as configurações.";
        ChangedBy = "sistema";
        ChangedAt = now;
    }

    public int Id { get; private set; }

    public bool IsHalted { get; private set; }

    public string? HaltReason { get; private set; }

    public string? ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>O robô nasce travado: operar exige liberação consciente.</summary>
    public static TradingState CreateHalted(DateTimeOffset now) => new(now);

    public void Halt(string reason, string actor, DateTimeOffset now)
    {
        HaltReason = Guard.NotBlank(reason, nameof(reason));
        ChangedBy = Guard.NotBlank(actor, nameof(actor));
        IsHalted = true;
        ChangedAt = now;
    }

    public void Resume(string actor, DateTimeOffset now)
    {
        if (!IsHalted)
        {
            throw new DomainException("O robô já está liberado.");
        }

        ChangedBy = Guard.NotBlank(actor, nameof(actor));
        IsHalted = false;
        HaltReason = null;
        ChangedAt = now;
    }
}

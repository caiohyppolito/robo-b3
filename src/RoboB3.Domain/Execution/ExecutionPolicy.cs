using RoboB3.Domain.Common;

namespace RoboB3.Domain.Execution;

/// <summary>Tudo o que a política precisa saber para autorizar uma troca de modo.</summary>
public sealed record ModeChangeContext(
    bool RobotHalted,
    int OpenPositionsInCurrentMode,
    bool AllowMt5,
    bool AllowRealAccount,
    bool FeedConnected,
    bool? AccountIsDemo,
    string? Confirmation);

/// <summary>
/// Regras para trocar o modo de execução. Cada trava é independente:
/// configuração da API, conector, tipo da conta logada e confirmação humana.
/// </summary>
public static class ExecutionPolicy
{
    public const string RealAccountConfirmation = "OPERAR CONTA REAL";

    public static void EnsureCanChange(ExecutionMode current, ExecutionMode target, ModeChangeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (current == target)
        {
            throw new DomainException($"O robô já está no modo {target.Describe()}.");
        }

        if (!context.RobotHalted)
        {
            throw new DomainException("Trave o robô antes de mudar o modo de execução.");
        }

        if (context.OpenPositionsInCurrentMode > 0)
        {
            throw new DomainException($"Encerre as {context.OpenPositionsInCurrentMode} posições abertas no modo {current.Describe()} antes de mudar.");
        }

        if (!target.SendsToBroker())
        {
            return;
        }

        if (!context.AllowMt5)
        {
            throw new DomainException("Envio de ordens ao MT5 desabilitado na configuração (Execution:AllowMt5).");
        }

        if (!context.FeedConnected)
        {
            throw new DomainException("O MetaTrader 5 não está conectado.");
        }

        var requiresDemo = target.RequiresDemoAccount()!.Value;
        if (context.AccountIsDemo != requiresDemo)
        {
            var logged = context.AccountIsDemo switch { true => "demo", false => "real", _ => "desconhecida" };
            throw new DomainException($"A conta logada no MT5 é {logged}, mas o modo {target.Describe()} exige conta {(requiresDemo ? "demo" : "real")}.");
        }

        if (target == ExecutionMode.Mt5Real)
        {
            if (!context.AllowRealAccount)
            {
                throw new DomainException("Operação em conta real desabilitada na configuração (Execution:AllowRealAccount).");
            }

            if (!string.Equals(context.Confirmation?.Trim(), RealAccountConfirmation, StringComparison.Ordinal))
            {
                throw new DomainException($"Para operar a conta real, digite exatamente: {RealAccountConfirmation}");
            }
        }
    }

    /// <summary>Motivo para travar o robô se a conta logada não corresponde ao modo, ou nulo se está tudo certo.</summary>
    public static string? FindAccountMismatch(ExecutionMode mode, bool feedConnected, bool? accountIsDemo)
    {
        if (!mode.SendsToBroker())
        {
            return null;
        }

        if (!feedConnected)
        {
            return $"MetaTrader 5 desconectado no modo {mode.Describe()}.";
        }

        return accountIsDemo == mode.RequiresDemoAccount()
            ? null
            : $"A conta logada no MT5 não corresponde ao modo {mode.Describe()}.";
    }
}

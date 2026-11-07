using RoboB3.Domain.Common;
using RoboB3.Domain.Execution;

namespace RoboB3.Domain.Tests;

public class ExecutionPolicyTests
{
    private static ModeChangeContext Ready(bool? isDemo = true, string? confirmation = null) =>
        new(RobotHalted: true, OpenPositionsInCurrentMode: 0, AllowMt5: true, AllowRealAccount: true, FeedConnected: true, isDemo, confirmation);

    [Fact]
    public void Demo_com_todas_as_travas_liberadas_e_permitido()
    {
        ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Demo, Ready());
    }

    [Theory]
    [InlineData(false, 0, true, true, true, "Trave o robô")]
    [InlineData(true, 2, true, true, true, "Encerre as 2 posições")]
    [InlineData(true, 0, false, true, true, "Execution:AllowMt5")]
    [InlineData(true, 0, true, false, true, "não está conectado")]
    [InlineData(true, 0, true, true, false, "conta logada no MT5 é real")]
    public void Cada_trava_impede_ir_para_demo(bool halted, int positions, bool allowMt5, bool connected, bool isDemo, string expected)
    {
        var context = new ModeChangeContext(halted, positions, allowMt5, true, connected, isDemo, null);

        var ex = Assert.Throws<DomainException>(() => ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Demo, context));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Conta_real_exige_configuracao_conta_real_e_confirmacao_digitada()
    {
        Assert.Throws<DomainException>(() => ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Real, Ready(isDemo: true, ExecutionPolicy.RealAccountConfirmation)));
        Assert.Throws<DomainException>(() => ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Real, Ready(isDemo: false, "operar conta real")));
        Assert.Throws<DomainException>(() => ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Real,
            Ready(isDemo: false, ExecutionPolicy.RealAccountConfirmation) with { AllowRealAccount = false }));

        ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Mt5Real, Ready(isDemo: false, ExecutionPolicy.RealAccountConfirmation));
    }

    [Fact]
    public void Voltar_ao_simulado_so_exige_robo_travado_e_sem_posicoes()
    {
        var context = new ModeChangeContext(true, 0, AllowMt5: false, AllowRealAccount: false, FeedConnected: false, null, null);

        ExecutionPolicy.EnsureCanChange(ExecutionMode.Mt5Real, ExecutionMode.Paper, context);
        Assert.Throws<DomainException>(() => ExecutionPolicy.EnsureCanChange(ExecutionMode.Paper, ExecutionMode.Paper, context));
    }

    [Theory]
    [InlineData(ExecutionMode.Paper, false, null, false)]
    [InlineData(ExecutionMode.Mt5Demo, true, true, false)]
    [InlineData(ExecutionMode.Mt5Demo, true, false, true)]
    [InlineData(ExecutionMode.Mt5Real, true, true, true)]
    [InlineData(ExecutionMode.Mt5Real, false, false, true)]
    public void Detecta_conta_diferente_do_modo(ExecutionMode mode, bool connected, bool? isDemo, bool mismatch)
    {
        Assert.Equal(mismatch, ExecutionPolicy.FindAccountMismatch(mode, connected, isDemo) is not null);
    }
}

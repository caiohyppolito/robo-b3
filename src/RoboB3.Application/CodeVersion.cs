using System.Reflection;

namespace RoboB3.Application;

/// <summary>Versão do código em execução (com o commit, quando o build sabe), gravada em cada decisão.</summary>
public static class CodeVersion
{
    public static string Current { get; } =
        typeof(CodeVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "desconhecida";
}

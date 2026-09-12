using System.Reflection;

namespace BidParser.Desktop.Services;

/// <summary>
/// The installed build's version, read once from the assembly. It is the single source for the
/// command bar, the HTTP User-Agent, the update comparison and failure diagnostics.
/// </summary>
public static class DesktopVersion
{
    /// <summary>The exact informational version, e.g. "1.0.0". Empty when the assembly declares none.</summary>
    public static string Installed { get; } = Read();

    /// <summary>What the command bar shows.</summary>
    public static string Display => Installed.Length == 0 ? "development" : Installed;

    public static string UserAgent => $"BidParser/{(Installed.Length == 0 ? "1.0.0-dev" : Installed)}";

    private static string Read()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? string.Empty;
}

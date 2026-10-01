using System.Runtime.InteropServices;

namespace OpenIGI.Launcher.Services;

/// <summary>Where the launcher keeps its settings and, by default, the builds it installs.</summary>
/// <remarks>
/// Each platform's own convention: <c>%APPDATA%</c> and <c>%LOCALAPPDATA%</c> on Windows,
/// <c>~/Library/Application Support</c> on macOS, the XDG config and data homes on Linux. The library holds
/// downloads of a few hundred megabytes, so it goes where an OS expects bulky per-user data, never into
/// a roaming profile.
/// </remarks>
internal static class LauncherPaths
{
    /// <summary>Gets the folder holding <c>settings.json</c>.</summary>
    public static string ConfigDirectory { get; } = Path.Combine(ConfigRoot(), "OpenIGI", "Launcher");

    /// <summary>Gets the library folder used until the player chooses another.</summary>
    public static string DefaultLibrary { get; } = Path.Combine(DataRoot(), "OpenIGI", "Library");

    /// <summary>Gets the runtime identifier the launcher's and the game's archives are published for.</summary>
    /// <remarks>The same names <c>tools/release/package.sh</c> gives them; anything else has no build.</remarks>
    public static string Runtime { get; } = DetectRuntime();

    private static string ConfigRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    private static string DataRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;
        }

        return OperatingSystem.IsMacOS()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static string DetectRuntime()
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        return $"{os}-{arch}";
    }
}

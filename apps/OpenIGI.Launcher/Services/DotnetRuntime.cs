using Microsoft.Win32;

namespace OpenIGI.Launcher.Services;

/// <summary>Whether this machine has the .NET runtime the smaller game build runs on.</summary>
/// <remarks>
/// <para>
/// The launcher is self-contained, so its own runtime says nothing about the machine's. The answer comes
/// from the places a framework-dependent program's host searches for one, in its order: <c>DOTNET_ROOT</c>,
/// the install location the .NET installer registers (the registry on Windows, <c>/etc/dotnet</c> elsewhere),
/// then the default install folders. A runtime is there when <c>shared/Microsoft.NETCore.App/&lt;major&gt;.x</c>
/// is, which is exactly what that host looks for.
/// </para>
/// <para>
/// Only the major version is checked: a framework-dependent build rolls forward to any later minor and
/// patch of the major it was built for.
/// </para>
/// </remarks>
internal static class DotnetRuntime
{
    /// <summary>The .NET major version the game is built for.</summary>
    public const int RequiredMajor = 10;

    /// <summary>Gets whether a .NET runtime the smaller game build can run on is installed.</summary>
    /// <returns>Whether the framework-dependent build will start here.</returns>
    public static bool IsInstalled() => IsInstalled(RequiredMajor, Roots());

    /// <summary>Whether any of the given install folders holds a runtime of a major version.</summary>
    /// <param name="major">The major version.</param>
    /// <param name="roots">The .NET install folders to look in.</param>
    /// <returns>Whether one is found.</returns>
    public static bool IsInstalled(int major, IEnumerable<string?> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        foreach (string? root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string shared = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            try
            {
                if (Directory.Exists(shared)
                    && Directory.EnumerateDirectories(shared, $"{major}.*").Any(static dir => !Path.GetFileName(dir).Contains('-', StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An install folder that cannot be read is not one the game could start from either.
            }
        }

        return false;
    }

    private static IEnumerable<string?> Roots()
    {
        string architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        yield return Environment.GetEnvironmentVariable($"DOTNET_ROOT_{architecture.ToUpperInvariant()}");
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (OperatingSystem.IsWindows())
        {
            yield return RegisteredWindowsLocation(architecture);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            yield break;
        }

        // The installer's record of where it put .NET, per architecture first.
        foreach (string file in new[] { $"/etc/dotnet/install_location_{architecture}", "/etc/dotnet/install_location" })
        {
            string? recorded = null;
            try
            {
                if (File.Exists(file))
                {
                    recorded = File.ReadLines(file).FirstOrDefault()?.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                recorded = null;
            }

            yield return recorded;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/share/dotnet";
            yield return "/usr/local/share/dotnet/x64";
        }
        else
        {
            yield return "/usr/lib/dotnet";
            yield return "/usr/share/dotnet";
            yield return "/usr/lib64/dotnet";
        }

        yield return Path.Combine(home, ".dotnet");
    }

    private static string? RegisteredWindowsLocation(string architecture)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{architecture}");
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

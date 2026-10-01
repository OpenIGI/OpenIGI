using System.Diagnostics;
using System.Reflection;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>Replaces the running launcher with a newer release of itself.</summary>
/// <remarks>
/// <para>
/// The new launcher is downloaded and unpacked beside the settings (through a <see cref="BuildInstaller"/>
/// of its own, so it gets the same resume and hash check as the game) while this one keeps running. Applying
/// it moves each of the running launcher's files aside as <c>*.old</c> — which every platform allows for a
/// file that is open, Windows included, where overwriting one is refused — copies the new files in, and starts
/// the new launcher. The <c>*.old</c> files are deleted the next time the launcher starts.
/// </para>
/// <para>
/// On macOS the files are those inside the application bundle, so the bundle keeps its place in the Dock and
/// in Finder. A development build (version <c>0.0.0-dev</c>) never replaces itself.
/// </para>
/// </remarks>
internal sealed class LauncherUpdater(BuildInstaller staging)
{
    private const string OldSuffix = ".old";

    /// <summary>Gets this launcher's own version.</summary>
    public static ReleaseVersion OwnVersion { get; } = ReadOwnVersion();

    /// <summary>Gets the folder (macOS: the application bundle) the running launcher was installed as.</summary>
    public static string InstallRoot { get; } = FindInstallRoot();

    /// <summary>Gets whether this launcher may replace itself: a released build in a folder it can write to.</summary>
    public static bool CanUpdateSelf => OwnVersion > ReleaseVersion.Parse("0.0.0") && IsWritable(InstallRoot);

    /// <summary>Gets the release staged and waiting for a restart, if any.</summary>
    public InstalledBuild? Staged { get; private set; }

    /// <summary>Removes what the last update moved aside.</summary>
    public static void CleanUp()
    {
        try
        {
            foreach (string old in Directory.EnumerateFiles(InstallRoot, "*" + OldSuffix, SearchOption.AllDirectories))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still held by the launcher that is exiting; the next start gets it.
        }
    }

    /// <summary>Downloads and unpacks a launcher release, ready for <see cref="ApplyAndRestart"/>.</summary>
    /// <param name="version">The release version.</param>
    /// <param name="asset">The launcher archive for this platform.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the release is staged.</returns>
    public async Task StageAsync(string version, ReleaseAsset asset, CancellationToken cancellationToken)
    {
        if (Staged?.Version == version)
        {
            return;
        }

        Staged = await staging.InstallAsync(version, asset, new Progress<InstallProgress>(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Swaps the staged release in and starts it; the caller then exits.</summary>
    /// <returns>Whether the new launcher was started.</returns>
    public bool ApplyAndRestart()
    {
        if (Staged is not { } staged)
        {
            return false;
        }

        // A macOS archive holds the bundle itself; its contents are what replace the running bundle's.
        string source = staged.Folder;
        if (OperatingSystem.IsMacOS() && Directory.EnumerateDirectories(source, "*.app").FirstOrDefault() is { } bundle)
        {
            source = bundle;
        }

        Apply(source, InstallRoot);
        string executable = OperatingSystem.IsMacOS()
            ? Path.Combine(InstallRoot, "Contents", "MacOS", "OpenIGI.Launcher")
            : Path.Combine(InstallRoot, OperatingSystem.IsWindows() ? "OpenIGI.Launcher.exe" : "OpenIGI.Launcher");

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! });
        return true;
    }

    /// <summary>Moves every file of <paramref name="target"/> that <paramref name="source"/> replaces aside, and copies the replacement in.</summary>
    /// <param name="source">The new release's files.</param>
    /// <param name="target">The running launcher's files.</param>
    public static void Apply(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                File.Move(destination, destination + OldSuffix, overwrite: true);
            }

            File.Copy(file, destination);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, File.GetUnixFileMode(file));
            }
        }
    }

    private static ReleaseVersion ReadOwnVersion()
    {
        string? informational = typeof(LauncherUpdater).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ReleaseVersion.TryParse(informational, out ReleaseVersion version) ? version : ReleaseVersion.Parse("0.0.0-dev");
    }

    private static string FindInstallRoot()
    {
        string folder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (OperatingSystem.IsMacOS())
        {
            // .../OpenIGI Launcher.app/Contents/MacOS -> the bundle.
            DirectoryInfo? contents = Directory.GetParent(folder);
            if (contents?.Name == "Contents" && contents.Parent is { } app && app.Name.EndsWith(".app", StringComparison.Ordinal))
            {
                return app.FullName;
            }
        }

        return folder;
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

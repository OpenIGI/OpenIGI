using System.Reflection;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// Decides where settings and other per-user files live.
/// </summary>
/// <remarks>
/// <para>
/// A <c>settings.cfg</c> beside the executable wins over the per-user location. That makes a portable
/// install — a copied folder on a stick, or a checkout being tested — self-contained, while an
/// ordinary installation keeps the player's settings out of Program Files where it cannot write them.
/// </para>
/// <para>
/// The per-user location is <c>%APPDATA%\OpenIGI</c> on Windows and the platform's application-data
/// directory elsewhere, which .NET already maps to the XDG location on Linux and to
/// <c>~/Library/Application Support</c> on macOS.
/// </para>
/// </remarks>
public static class SettingsPaths
{
    /// <summary>The file name the console reads settings from and writes them to.</summary>
    public const string SettingsFileName = "settings.cfg";

    /// <summary>The folder name used under the per-user application-data directory.</summary>
    public const string ProductFolder = "OpenIGI";

    /// <summary>Gets the directory beside the running executable.</summary>
    /// <remarks>
    /// Not <see cref="AppContext.BaseDirectory"/>, which is where the app's own files are rather than
    /// where its executable is. Published as a single file those are different places: the bundle
    /// unpacks itself into a temporary folder and runs from there, so BaseDirectory names a directory
    /// the player has never seen and cannot drop a <c>settings.cfg</c> into. The process path is the
    /// bundle itself, which is the file they actually double-clicked.
    /// </remarks>
    public static string PortableDirectory => ExecutableDirectory;

    /// <summary>Gets the directory holding the executable that started this process.</summary>
    /// <remarks>
    /// Falls back to <see cref="AppContext.BaseDirectory"/> when the process is not the app's own
    /// executable — running through the shared host as <c>dotnet OpenIGI.Desktop.dll</c> makes the
    /// process <c>dotnet</c> itself, whose folder is the SDK's and has nothing to do with the game.
    /// The two agree for an ordinary published app, so this only ever differs where it must.
    /// </remarks>
    public static string ExecutableDirectory
    {
        get
        {
            string? processPath = Environment.ProcessPath;
            string? entryName = Assembly.GetEntryAssembly()?.GetName().Name;

            if (processPath is not null
                && entryName is not null
                && string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    entryName,
                    StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(processPath) is { Length: > 0 } directory)
            {
                return directory;
            }

            return AppContext.BaseDirectory;
        }
    }

    /// <summary>Gets the per-user configuration directory, creating it when necessary.</summary>
    /// <returns>The directory path.</returns>
    /// <remarks>Falls back to <see cref="PortableDirectory"/> when the platform reports no data folder.</remarks>
    public static string GetUserDirectory()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrEmpty(appData))
        {
            return PortableDirectory;
        }

        string directory = Path.Combine(appData, ProductFolder);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Gets the settings file that should be read, preferring a portable one.</summary>
    /// <returns>The full path of the settings file. The file need not exist.</returns>
    public static string GetSettingsPath() => GetFilePath(SettingsFileName);

    /// <summary>Gets where a per-user file should be read from and written to.</summary>
    /// <param name="fileName">The file's name, without a directory.</param>
    /// <returns>The full path. The file need not exist.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is blank.</exception>
    /// <remarks>
    /// The same rule <see cref="GetSettingsPath"/> follows, for everything else the player accumulates:
    /// a copy beside the executable wins, so a folder on a stick stays self-contained, and otherwise it
    /// is the per-user directory, which is writable when the game's own is not. Nothing the player owns
    /// belongs in the installation — it may sit in Program Files, on a read-only mount, or be shared
    /// between people.
    /// </remarks>
    public static string GetFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string portable = Path.Combine(PortableDirectory, fileName);
        return File.Exists(portable) ? portable : Path.Combine(GetUserDirectory(), fileName);
    }
}

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
    public static string PortableDirectory => AppContext.BaseDirectory;

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
    public static string GetSettingsPath()
    {
        string portable = Path.Combine(PortableDirectory, SettingsFileName);
        return File.Exists(portable) ? portable : Path.Combine(GetUserDirectory(), SettingsFileName);
    }
}

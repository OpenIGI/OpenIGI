using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>What the player chose in the launcher's settings, kept in <c>settings.json</c>.</summary>
internal sealed class LauncherSettings
{
    /// <summary>Gets or sets the update channel the launcher follows.</summary>
    /// <remarks>
    /// Until the player chooses, the channel of the launcher they downloaded: a beta launcher (the one
    /// beta.openigi.com hands out) follows beta from its first start, a stable one follows stable.
    /// </remarks>
    public ReleaseChannel Channel { get; set; } =
        LauncherUpdater.OwnVersion.IsPrerelease && LauncherUpdater.OwnVersion > ReleaseVersion.Parse("0.0.0")
            ? ReleaseChannel.Beta
            : ReleaseChannel.Stable;

    /// <summary>Gets or sets whether a new release is downloaded as soon as it is seen.</summary>
    public bool AutomaticUpdates { get; set; } = true;

    /// <summary>Gets or sets where builds are installed.</summary>
    public string Library { get; set; } = LauncherPaths.DefaultLibrary;

    /// <summary>Gets or sets the original game's folder for each title id (<c>igi1</c>, <c>igi2</c>).</summary>
    public Dictionary<string, string> GameFolders { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets extra arguments passed to the game.</summary>
    public string LaunchArguments { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the launcher closes once the game has started.</summary>
    public bool CloseOnLaunch { get; set; }

    /// <summary>Reads the settings, or the defaults when there are none yet or the file is unreadable.</summary>
    /// <param name="path">The settings file.</param>
    /// <returns>The settings.</returns>
    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize(File.ReadAllText(path), LauncherSettingsJson.Default.LauncherSettings) is { } settings)
            {
                settings.GameFolders = new Dictionary<string, string>(settings.GameFolders, StringComparer.Ordinal);
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file costs the player their choices, not the launcher.
        }

        return new LauncherSettings();
    }

    /// <summary>Writes the settings: to a temporary file first, so a crash mid-write cannot lose them.</summary>
    /// <param name="path">The settings file.</param>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, LauncherSettingsJson.Default.LauncherSettings));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>The settings file's JSON shape.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LauncherSettings))]
internal sealed partial class LauncherSettingsJson : JsonSerializerContext;

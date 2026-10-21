using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>What the player chose in the launcher's settings, kept in <c>settings.json</c>.</summary>
internal sealed class LauncherSettings
{
    /// <summary>Gets or sets the channel whose game builds the launcher installs: stable unless the player opts in.</summary>
    /// <remarks>The launcher itself has one channel, stable, and updates only to stable releases of itself.</remarks>
    public ReleaseChannel Channel { get; set; } = ReleaseChannel.Stable;

    /// <summary>Gets or sets whether a new release is downloaded as soon as it is seen.</summary>
    public bool AutomaticUpdates { get; set; } = true;

    /// <summary>Gets or sets where builds are installed.</summary>
    public string Library { get; set; } = LauncherPaths.DefaultLibrary;

    /// <summary>Gets or sets the original game's folder for each title id (<c>igi1</c>, <c>igi2</c>).</summary>
    public Dictionary<string, string> GameFolders { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets extra arguments passed to the game.</summary>
    public string LaunchArguments { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the launcher goes to the tray once the game has started, and comes back when it exits.</summary>
    public bool MinimizeOnLaunch { get; set; }

    /// <summary>Gets or sets what the close button does: ask, go to the tray, or exit.</summary>
    public CloseAction CloseButton { get; set; } = CloseAction.Ask;

    /// <summary>Gets or sets whether the first-run guide has been finished or skipped, so it is never shown again.</summary>
    public bool Onboarded { get; set; }

    /// <summary>Reads the setting <see cref="MinimizeOnLaunch"/> replaced, from a file an earlier launcher wrote.</summary>
    /// <remarks>Closing on launch now means going to the tray, so a player who chose it keeps the choice.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CloseOnLaunch
    {
        get => null;
        set => MinimizeOnLaunch |= value == true;
    }

    /// <summary>Gets or sets whether a desktop notification announces a new release while the launcher is in the background.</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>Gets or sets the last version announced of each of <c>game</c> and <c>launcher</c>, so none is announced twice.</summary>
    public Dictionary<string, string> Announced { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets, per game id, the content load order: highest priority first, each entry on or off.</summary>
    /// <remarks>The game folder's own overrides are an entry too; see <see cref="ContentLoadOrder"/>.</remarks>
    public Dictionary<string, List<ContentOrderEntry>> ContentOrder { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the on/off state of each pack from before there was a load order.</summary>
    /// <remarks>
    /// Read once, to carry each pack's choice into its game's <see cref="ContentOrder"/>, and emptied as the
    /// packs are met; never written to otherwise.
    /// </remarks>
    public Dictionary<string, bool> Content { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets, per game id, the mod order: in the order the mods apply, each entry on or off.</summary>
    /// <remarks>See <see cref="ModLoadOrder"/>.</remarks>
    public Dictionary<string, List<ContentOrderEntry>> ModOrder { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the games started without mods, by id, whatever their orders say.</summary>
    public List<string> ModsOff { get; set; } = [];

    /// <summary>Gets or sets the mods with code the player agreed to run, by id: asked once each, on the first install.</summary>
    public List<string> TrustedMods { get; set; } = [];

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
                settings.Announced = new Dictionary<string, string>(settings.Announced, StringComparer.Ordinal);
                settings.Content = new Dictionary<string, bool>(settings.Content ?? [], StringComparer.Ordinal);
                settings.ContentOrder = Orders(settings.ContentOrder);
                settings.ModOrder = Orders(settings.ModOrder);
                settings.ModsOff = [.. (settings.ModsOff ?? []).Where(static id => id is { Length: > 0 })];
                settings.TrustedMods = [.. (settings.TrustedMods ?? []).Where(static id => id is { Length: > 0 })];
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file costs the player their choices, not the launcher.
        }

        return new LauncherSettings();
    }

    // Each game's order as written, without the entries a damaged or hand-edited file left empty.
    private static Dictionary<string, List<ContentOrderEntry>> Orders(Dictionary<string, List<ContentOrderEntry>>? orders) =>
        new(
            (orders ?? []).Where(static pair => pair.Value is not null)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value.Where(static entry => entry?.Id is { Length: > 0 }).ToList()),
            StringComparer.Ordinal);

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

/// <summary>What the launcher's close button does.</summary>
internal enum CloseAction
{
    /// <summary>Ask each time: go to the tray, or exit.</summary>
    Ask,

    /// <summary>Hide in the notification area (the Dock or the taskbar where there is none), still checking for updates.</summary>
    MinimizeToTray,

    /// <summary>Exit the launcher.</summary>
    Exit,
}

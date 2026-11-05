using System.Globalization;
using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// Per-mission atmosphere, loaded from <c>Assets/fog-presets.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A level's own <c>FlatSky</c> task already says how thick its fog should be, and deriving from it
/// gives every mission a distinct mood for free. What it cannot say is anything the original had no
/// way to express — how the fog is shaped, how far it reaches, how it scatters — so this file holds
/// the exceptions and the derived value is the fallback.
/// </para>
/// <para>
/// It is data rather than code so that a level's atmosphere can be re-tuned without a rebuild, which
/// matters because tuning this is done by looking rather than by reasoning.
/// </para>
/// </remarks>
public sealed class FogPresets
{
    private const string LogCategory = "Fog";

    /// <summary>The game a mission number belongs to when its key does not name one.</summary>
    /// <remarks>
    /// The file was written for Project I.G.I. alone and every bare key in it still means that game. The
    /// sequel numbers its missions from one too, so a bare key read for it handed its third mission the
    /// first game's storm; a sequel entry is keyed <c>igi2:3</c> instead.
    /// </remarks>
    public const string DefaultTitle = "igi1";

    private readonly Dictionary<string, FogPresetEntry> _missions = [];
    private readonly Dictionary<string, MissionLook> _looks = [];
    private readonly Dictionary<string, LocalFogVolume[]> _volumes = [];
    private readonly FogPresetEntry _default;
    private readonly MissionLook _defaultLook;

    private FogPresets(FogPresetEntry defaults, MissionLook defaultLook = default)
    {
        _default = defaults;
        _defaultLook = defaultLook;
    }

    /// <summary>Gets presets with nothing authored, so every mission derives from its own sky.</summary>
    public static FogPresets Empty { get; } = new(FogPresetEntry.Empty);

    /// <summary>Reads the preset file.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="logger">Where to report a file that could not be read.</param>
    /// <returns>The presets, or <see cref="Empty"/> when the file is missing or malformed.</returns>
    /// <remarks>
    /// A broken preset file never stops the game: the levels fall back to their own authored fog,
    /// which is the behaviour before this file existed.
    /// </remarks>
    public static FogPresets Load(string path, IOpenIgiLogger? logger = null)
    {
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (!File.Exists(path))
        {
            log.Information(LogCategory, $"No fog presets at {path}; levels derive their fog from their own sky.");
            return Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            JsonElement root = document.RootElement;
            bool hasDefaults = root.TryGetProperty("default", out JsonElement defaults);
            FogPresets presets = new(
                hasDefaults ? FogPresetEntry.Read(defaults) : FogPresetEntry.Empty,
                hasDefaults ? MissionLook.Read(defaults) : MissionLook.Empty);

            if (root.TryGetProperty("missions", out JsonElement missions))
            {
                foreach (JsonProperty mission in missions.EnumerateObject())
                {
                    if (KeyOf(mission.Name) is { } key)
                    {
                        presets._missions[key] = FogPresetEntry.Read(mission.Value);
                        presets._looks[key] = MissionLook.Read(mission.Value);
                    }
                }
            }

            if (root.TryGetProperty("volumes", out JsonElement volumes))
            {
                foreach (JsonProperty mission in volumes.EnumerateObject())
                {
                    if (KeyOf(mission.Name) is { } key
                        && mission.Value.ValueKind == JsonValueKind.Array)
                    {
                        presets._volumes[key] = ReadVolumes(mission.Value);
                    }
                }
            }

            log.Information(
                LogCategory,
                $"Fog presets: {presets._missions.Count} mission(s), {presets._volumes.Values.Sum(v => v.Length)} volume(s).");

            return presets;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warning(LogCategory, $"Could not read {path}: {ex.Message}. Levels derive their fog from their own sky.");
            return Empty;
        }
    }

    /// <summary>Builds a mission's medium.</summary>
    /// <param name="mission">The mission id, or zero when none is loaded.</param>
    /// <param name="derivedDensity">The density derived from the level's own <c>FlatSky</c> task.</param>
    /// <param name="baseHeight">The level's sky-plane height in metres.</param>
    /// <param name="steps">How many samples a ray takes, from the quality setting.</param>
    /// <param name="title">The game the mission belongs to: <c>igi1</c> or <c>igi2</c>.</param>
    /// <returns>The medium to render with.</returns>
    public VolumetricFogSettings Resolve(int mission, float derivedDensity, float baseHeight, int steps, string title = DefaultTitle)
    {
        FogPresetEntry entry = _missions.TryGetValue(Key(title, mission), out FogPresetEntry authored)
            ? authored.Over(_default)
            : _default;

        return new VolumetricFogSettings(
            entry.Density is > 0f ? entry.Density.Value : derivedDensity,
            entry.HeightFalloff ?? 24f,
            baseHeight,
            entry.Albedo ?? default,
            entry.Anisotropy ?? 0.7f,
            entry.Distance ?? 500f,
            steps,
            entry.MultipleScattering ?? 0.35f,
            entry.UniformFraction ?? 0.12f,
            entry.SunIntensity ?? 1f,
            entry.AmbientIntensity ?? 1f,
            entry.Dust ?? 0f,
            entry.DustIndoor ?? 0f);
    }

    /// <summary>Gets the volumes a mission declares.</summary>
    /// <param name="mission">The mission id.</param>
    /// <param name="title">The game the mission belongs to.</param>
    /// <returns>The volumes, empty when the mission declares none.</returns>
    public ReadOnlySpan<LocalFogVolume> VolumesFor(int mission, string title = DefaultTitle) =>
        _volumes.TryGetValue(Key(title, mission), out LocalFogVolume[]? volumes) ? volumes : ReadOnlySpan<LocalFogVolume>.Empty;

    /// <summary>Gets a mission's look: its painted sky and its brightness, over the file's default.</summary>
    /// <param name="mission">The mission id.</param>
    /// <param name="title">The game the mission belongs to.</param>
    /// <returns>The look; <see cref="MissionLook.Empty"/> fields where nothing is authored.</returns>
    public MissionLook LookFor(int mission, string title = DefaultTitle) =>
        _looks.TryGetValue(Key(title, mission), out MissionLook look) ? look.Over(_defaultLook) : _defaultLook;

    // A bare number is the first game's; "igi2:3" names the game. Anything else is not a mission key.
    private static string? KeyOf(string name)
    {
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        string title = colon < 0 ? DefaultTitle : name[..colon].Trim().ToLowerInvariant();
        return int.TryParse(colon < 0 ? name : name[(colon + 1)..], CultureInfo.InvariantCulture, out int id)
            ? Key(title, id)
            : null;
    }

    private static string Key(string title, int mission) =>
        string.Create(CultureInfo.InvariantCulture, $"{title.ToLowerInvariant()}:{mission}");

    private static LocalFogVolume[] ReadVolumes(JsonElement array)
    {
        List<LocalFogVolume> volumes = [];

        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("centre", out JsonElement centre)
                || !element.TryGetProperty("extent", out JsonElement extent))
            {
                continue;
            }

            FogVolumeShape shape =
                element.TryGetProperty("shape", out JsonElement shapeText)
                && string.Equals(shapeText.GetString(), "sphere", StringComparison.OrdinalIgnoreCase)
                    ? FogVolumeShape.Sphere
                    : FogVolumeShape.Box;

            volumes.Add(new LocalFogVolume(
                ReadVector(centre),
                ReadVector(extent),
                element.TryGetProperty("densityScale", out JsonElement scale) ? (float)scale.GetDouble() : 1f,
                shape));

            if (volumes.Count == LocalFogVolume.MaxPerLevel)
            {
                break;
            }
        }

        return [.. volumes];
    }

    private static Vector3f ReadVector(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3)
        {
            return default;
        }

        return new Vector3f(
            (float)element[0].GetDouble(),
            (float)element[1].GetDouble(),
            (float)element[2].GetDouble());
    }
}

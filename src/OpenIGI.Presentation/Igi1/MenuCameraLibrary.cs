using OpenIGI.Core.Diagnostics;
using OpenIGI.Presentation.Cameras;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The orbit parameters for one mission's menu showcase camera.</summary>
/// <param name="Radius">The orbit radius in metres.</param>
/// <param name="Height">The camera height above the centre in metres.</param>
/// <param name="SpeedRadiansPerSecond">How fast the orbit turns; small, for a slow drift.</param>
/// <param name="StartAngleRadians">Where on the circle the orbit begins.</param>
/// <param name="PitchBiasRadians">An extra downward (negative) or upward tilt on top of the look-at.</param>
public readonly record struct MenuCameraSpec(
    double Radius,
    double Height,
    double SpeedRadiansPerSecond,
    double StartAngleRadians,
    float PitchBiasRadians)
{
    /// <summary>Gets a sensible default orbit for a level with no authored entry.</summary>
    /// <remarks>
    /// The speed is what makes it read as an orbit at all. At 0.06 rad/s a shot held for
    /// <see cref="MenuShowcaseCamera.DwellSeconds"/> swept sixty-nine degrees before cutting away, which
    /// looks like the camera sliding sideways past a building rather than going round it; 0.12 carries it
    /// through about a hundred and forty, far enough that two different faces of the subject come into view
    /// and the motion is legible as a circle.
    /// </remarks>
    public static MenuCameraSpec Default => new(140.0, 55.0, 0.12, 0.0, 0f);
}

/// <summary>
/// Loads the per-mission menu-camera orbits from <c>Assets/menu-cameras.json</c>.
/// </summary>
/// <remarks>
/// The file maps a mission id to a <see cref="MenuCameraSpec"/>; a mission with no entry, or a missing
/// file, falls back to <see cref="MenuCameraSpec.Default"/>. Keeping the numbers in data means the
/// showcase camera for a level can be re-tuned by flying the free camera with <c>-camlog</c> and editing
/// the JSON, with no rebuild — the same "data, not code" reasoning the fog presets and LUTs follow.
/// </remarks>
public sealed partial class MenuCameraLibrary
{
    private readonly Dictionary<int, MenuCameraSpec> _byMission;
    private readonly Dictionary<int, IReadOnlyList<ShowcasePoint>> _pointsByMission;

    private MenuCameraLibrary(
        Dictionary<int, MenuCameraSpec> byMission,
        Dictionary<int, IReadOnlyList<ShowcasePoint>> pointsByMission)
    {
        _byMission = byMission;
        _pointsByMission = pointsByMission;
    }

    /// <summary>Gets a library with no authored entries; every mission uses the default orbit.</summary>
    public static MenuCameraLibrary Empty { get; } = new([], []);

    /// <summary>
    /// How the file is read. Case-insensitive because the file is authored in the JSON convention —
    /// <c>"mission"</c>, <c>"pitchBias"</c> — while the record that receives it is named in the C# one.
    /// </summary>
    /// <remarks>
    /// Without this the file parsed without error and bound <em>nothing</em>: every entry came back with
    /// its members at their defaults, so the whole library keyed itself under mission 0 and every real
    /// mission silently fell back to the built-in orbit. An authored camera that quietly does nothing is
    /// worse than one that fails loudly, which is what this being an explicit option rather than an
    /// implicit default is meant to keep visible.
    /// </remarks>
    /// <summary>Loads the library from a JSON file, falling back to an empty one on any problem.</summary>
    /// <param name="path">The JSON file path.</param>
    /// <param name="logger">Where to report a load failure.</param>
    /// <returns>The library.</returns>
    public static MenuCameraLibrary Load(string path, IIgiLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            Entry[]? entries = JsonSerializer.Deserialize(
                File.ReadAllText(path), MenuCameraJsonContext.Default.EntryArray);
            var map = new Dictionary<int, MenuCameraSpec>();
            var points = new Dictionary<int, IReadOnlyList<ShowcasePoint>>();
            foreach (Entry entry in entries ?? [])
            {
                var spec = new MenuCameraSpec(
                    entry.Radius,
                    entry.Height,
                    entry.Speed,
                    entry.StartAngle,
                    entry.PitchBias);
                map[entry.Mission] = spec;

                if (entry.Points is { Length: > 0 } authored)
                {
                    points[entry.Mission] =
                    [
                        .. authored.Select(p => new ShowcasePoint(
                            new OpenIGI.Core.Numerics.Vector3d(p.X, p.Y, p.Z),
                            spec with
                            {
                                Radius = p.Radius > 0.0 ? p.Radius : spec.Radius,
                                Height = p.Height > 0.0 ? p.Height : spec.Height,
                            })),
                    ];
                }
            }

            return new MenuCameraLibrary(map, points);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger?.Warning("Menu", $"Could not read menu cameras from {path}: {ex.Message}");
            return Empty;
        }
    }

    /// <summary>Gets the orbit for a mission, or the default when none is authored.</summary>
    /// <param name="missionId">The mission id.</param>
    /// <returns>The orbit parameters.</returns>
    public MenuCameraSpec For(int missionId) =>
        _byMission.TryGetValue(missionId, out MenuCameraSpec spec) ? spec : MenuCameraSpec.Default;

    /// <summary>Gets the hand-authored places a mission's backdrop cycles between, or none.</summary>
    /// <param name="missionId">The mission id.</param>
    /// <returns>The authored places, or an empty list to let them be derived from the level.</returns>
    /// <remarks>
    /// An escape hatch, not the normal path: the places are derived from where a level put its geometry
    /// (<see cref="MissionShowcasePoints"/>) so that every mission has them and a mod's levels get them
    /// too. Authoring a <c>points</c> array for a mission overrides that entirely, which is what to reach
    /// for when a particular level's automatic pick is dull — the same "data, not code" escape the orbit
    /// numbers themselves are.
    /// </remarks>
    public IReadOnlyList<ShowcasePoint> PointsFor(int missionId) =>
        _pointsByMission.TryGetValue(missionId, out IReadOnlyList<ShowcasePoint>? points) ? points : [];

    private sealed record Entry(
        int Mission,
        double Radius,
        double Height,
        double Speed,
        double StartAngle,
        float PitchBias,
        PointEntry[]? Points = null);

    /// <summary>One hand-authored place, in metres, with an optional orbit of its own.</summary>
    private sealed record PointEntry(
        double X,
        double Y,
        double Z,
        double Radius = 0.0,
        double Height = 0.0);

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(Entry[]))]
    private sealed partial class MenuCameraJsonContext : JsonSerializerContext;
}

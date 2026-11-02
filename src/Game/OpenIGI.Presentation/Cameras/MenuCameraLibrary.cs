using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace OpenIGI.Presentation.Cameras;

/// <summary>The orbit parameters for one mission's menu showcase camera.</summary>
/// <param name="Radius">The orbit radius in metres.</param>
/// <param name="Height">The camera height above the centre in metres.</param>
/// <param name="SpeedRadiansPerSecond">How fast the orbit turns; small, for a slow drift.</param>
/// <param name="StartAngleRadians">Where on the circle the orbit begins.</param>
/// <param name="PitchBiasRadians">An extra downward (negative) or upward tilt on top of the look-at.</param>
/// <param name="WallAware">
/// Whether the orbit stays inside whatever encloses its centre: for a place inside a building, where a
/// fixed circle would carry the camera through the walls and out into whatever is beyond. See
/// <see cref="MenuShowcaseCamera"/>. Off for the usual orbit around the outside of a building, whose centre
/// is inside the building it is looking at.
/// </param>
public readonly record struct MenuCameraSpec(
    double Radius,
    double Height,
    double SpeedRadiansPerSecond,
    double StartAngleRadians,
    float PitchBiasRadians,
    bool WallAware = false)
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
    /// <summary>The game an entry belongs to when it does not say: Project I.G.I.</summary>
    /// <remarks>
    /// Every entry authored before the sequel existed is one of its, and none of them carry a title, so
    /// the unmarked default has to be that game rather than "any".
    /// </remarks>
    public const string DefaultTitle = "igi1";

    // Keyed by the title AND the mission, because the two games number their missions over each other:
    // I.G.I. 2's campaign runs 11-17, 21-26 and 31-36, and Project I.G.I.'s runs 1-14. Missions 11 to 14
    // exist in both. Keyed by the number alone, browsing the sequel's first four missions in the menu
    // showed the first game's trainyard, SAM base and harbour -- real places, on the wrong map.
    private readonly Dictionary<(string Title, int Mission), MenuCameraSpec> _byMission;
    private readonly Dictionary<(string Title, int Mission), IReadOnlyList<ShowcasePoint>> _pointsByMission;

    private MenuCameraLibrary(
        Dictionary<(string Title, int Mission), MenuCameraSpec> byMission,
        Dictionary<(string Title, int Mission), IReadOnlyList<ShowcasePoint>> pointsByMission)
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
    public static MenuCameraLibrary Load(string path, IOpenIgiLogger? logger = null)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            Entry[]? entries = JsonSerializer.Deserialize(
                File.ReadAllText(path), MenuCameraJsonContext.Default.EntryArray);
            var map = new Dictionary<(string Title, int Mission), MenuCameraSpec>();
            var points = new Dictionary<(string Title, int Mission), IReadOnlyList<ShowcasePoint>>();
            foreach (Entry entry in entries ?? [])
            {
                var spec = new MenuCameraSpec(
                    entry.Radius,
                    entry.Height,
                    entry.Speed,
                    entry.StartAngle,
                    entry.PitchBias);
                map[Key(entry.Mission, entry.Title)] = spec;

                // The places in the order authored, rails after orbits unless a rail says where it goes.
                var places = new List<ShowcasePoint>();
                foreach (PointEntry p in entry.Points ?? [])
                {
                    places.Add(new ShowcasePoint(
                        new OpenIGI.Core.Numerics.Vector3d(p.X, p.Y, p.Z),
                        spec with
                        {
                            Radius = p.Radius > 0.0 ? p.Radius : spec.Radius,
                            Height = p.Height > 0.0 ? p.Height : spec.Height,
                            WallAware = p.WallAware,
                        }));
                }

                foreach (RailEntry rail in entry.Rails ?? [])
                {
                    if (rail.Points is not { Length: >= 2 } waypoints)
                    {
                        logger?.Warning("Menu", $"A rail for mission {entry.Mission} has fewer than two points; skipped.");
                        continue;
                    }

                    var travel = new CameraRail(
                        [.. waypoints.Select(p => new OpenIGI.Core.Numerics.Vector3d(p.X, p.Y, p.Z))],
                        rail.Speed);
                    var place = new ShowcasePoint(travel.Waypoints[0], spec, travel);
                    places.Insert(Math.Clamp(rail.Order < 0 ? places.Count : rail.Order, 0, places.Count), place);
                }

                if (places.Count > 0)
                {
                    points[Key(entry.Mission, entry.Title)] = places;
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

    /// <summary>Gets the orbit for one game's mission, or the default when none is authored.</summary>
    /// <param name="missionId">The mission id, as the title numbers it.</param>
    /// <param name="title">The game's id, as <c>GameTitle.Id</c> gives it.</param>
    /// <returns>The orbit parameters.</returns>
    /// <remarks>
    /// The title is not optional and deliberately so. Both games have a mission 11, and an overload that
    /// let a caller forget which game it was asking about is what put the first game's cameras on the
    /// sequel's maps.
    /// </remarks>
    public MenuCameraSpec For(int missionId, string title) =>
        _byMission.TryGetValue(Key(missionId, title), out MenuCameraSpec spec) ? spec : MenuCameraSpec.Default;

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
    /// <param name="title">The game's id, as <c>GameTitle.Id</c> gives it.</param>
    public IReadOnlyList<ShowcasePoint> PointsFor(int missionId, string title) =>
        _pointsByMission.TryGetValue(Key(missionId, title), out IReadOnlyList<ShowcasePoint>? points)
            ? points
            : [];

    /// <summary>The key a mission is stored under, with the title folded to one casing.</summary>
    private static (string Title, int Mission) Key(int missionId, string? title) =>
        ((title is { Length: > 0 } named ? named : DefaultTitle).ToLowerInvariant(), missionId);

    /// <summary>One authored backdrop camera.</summary>
    /// <param name="Mission">The mission id, as its own game numbers it.</param>
    /// <param name="Radius">The orbit radius in metres.</param>
    /// <param name="Height">The orbit height in metres.</param>
    /// <param name="Speed">How fast the orbit turns.</param>
    /// <param name="StartAngle">Where on the orbit it opens.</param>
    /// <param name="PitchBias">How far the view tilts off level.</param>
    /// <param name="Points">The hand-authored places, or null to derive them from the level.</param>
    /// <param name="Title">
    /// Which game the entry is about: <c>igi1</c> or <c>igi2</c>. Unmarked means Project I.G.I., because
    /// every entry authored before the sequel existed is one of its. It is not optional information --
    /// both games have a mission 11 -- so an entry for the sequel MUST carry it.
    /// </param>
    /// <param name="Rails">Paths the camera travels through the level, for places an orbit cannot show.</param>
    private sealed record Entry(
        int Mission,
        double Radius,
        double Height,
        double Speed,
        double StartAngle,
        float PitchBias,
        PointEntry[]? Points = null,
        string Title = DefaultTitle,
        RailEntry[]? Rails = null);

    /// <summary>A path the camera travels, in metres, at an optional pace; see <see cref="CameraRail"/>.</summary>
    /// <remarks><c>order</c> places it among the mission's places; without one it goes after them.</remarks>
    private sealed record RailEntry(
        PointEntry[]? Points = null,
        double Speed = CameraRail.DefaultSpeed,
        int Order = -1);

    /// <summary>One hand-authored place, in metres, with an optional orbit of its own.</summary>
    /// <remarks><c>wallAware</c> keeps the orbit inside the room the place stands in; see <see cref="MenuCameraSpec.WallAware"/>.</remarks>
    private sealed record PointEntry(
        double X,
        double Y,
        double Z,
        double Radius = 0.0,
        double Height = 0.0,
        bool WallAware = false);

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(Entry[]))]
    private sealed partial class MenuCameraJsonContext : JsonSerializerContext;
}

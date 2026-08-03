using System.Text.Json;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Desktop;

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
    public static MenuCameraSpec Default => new(140.0, 55.0, 0.06, 0.0, 0f);
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
public sealed class MenuCameraLibrary
{
    private readonly Dictionary<int, MenuCameraSpec> _byMission;

    private MenuCameraLibrary(Dictionary<int, MenuCameraSpec> byMission) => _byMission = byMission;

    /// <summary>Gets a library with no authored entries; every mission uses the default orbit.</summary>
    public static MenuCameraLibrary Empty { get; } = new([]);

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
            Entry[]? entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(path));
            var map = new Dictionary<int, MenuCameraSpec>();
            foreach (Entry entry in entries ?? [])
            {
                map[entry.Mission] = new MenuCameraSpec(
                    entry.Radius,
                    entry.Height,
                    entry.Speed,
                    entry.StartAngle,
                    entry.PitchBias);
            }

            return new MenuCameraLibrary(map);
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

    private sealed record Entry(
        int Mission,
        double Radius,
        double Height,
        double Speed,
        double StartAngle,
        float PitchBias);
}

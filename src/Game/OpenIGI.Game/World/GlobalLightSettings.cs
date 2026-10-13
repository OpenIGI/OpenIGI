using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// The level's own air and its flat light on models — the <c>GlobalLight</c> task and the keyframe it is
/// showing.
/// </summary>
/// <param name="TerrainFog">The fog the ground is drawn into.</param>
/// <param name="ObjectFog">The fog models are drawn into.</param>
/// <param name="ObjectAmbient">The flat colour a model with no lightmap is multiplied by.</param>
/// <param name="Time">The keyframe's own time key, which is what selected it.</param>
/// <remarks>
/// <para>
/// This is where the original's distance fog and its model lighting actually come from, and both were
/// attributed to the wrong task at first: <c>FlatSky</c>'s <c>Fog Amount</c> and <c>Fog Color</c> are the
/// cloud plane's fade and the clear colour, not the air. <c>sub_527520</c> — the <c>GlobalLight</c>
/// update — finds the two <c>GlobalLightKeyframe</c> children bracketing the task's current time,
/// interpolates all of their bands, and publishes the first two:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>terrain</b> → <c>sub_50FE40</c> (<c>0x50FE40</c>) takes its ambient and <c>sub_50FD20</c>
/// (<c>0x50FD20</c>) its fog colour and its <c>density × 0.0005</c> (<c>0x527B41</c>), which is what
/// <c>sub_512C30</c> fogs every terrain vertex with.
/// </description></item>
/// <item><description>
/// <b>object category 1</b> → the model state block <c>sub_49D480</c> reads its fog colour
/// (<c>flt_B818EC</c>), its density (<c>dword_B46D04</c>) and its ambient (<c>dword_B818AC</c>).
/// </description></item>
/// </list>
/// <para>
/// The keyframes are not times of day. Every shipping level declares exactly two — <b>12</b>, the
/// mission's own air, and <b>100</b>, which is green in all fourteen and carries no fog at all: the
/// night-vision state. With the task's time at its start-up zero, <c>sub_527520</c> finds no keyframe
/// below it and copies the lowest one whole, so 12 is what a mission opens with.
/// </para>
/// <para>
/// The bands the levels ask for are far more varied than the one figure <c>FlatSky</c> suggested: half a
/// surface gone by 71 m on the foggy third mission and by 2.9 km on the alpine eleventh, and a colour
/// per level from warm sand to sky blue. That variety is the point — it is why mission 5's range is
/// meant to be seen and mission 1's horizon is not.
/// </para>
/// </remarks>
public readonly record struct GlobalLightSettings(
    SceneFog TerrainFog,
    SceneFog ObjectFog,
    Vector3f ObjectAmbient,
    float Time)
{
    /// <summary>The task type that carries the bands.</summary>
    public const string KeyframeType = "GlobalLightKeyframe";

    /// <summary>The ambient a model wears where a level declares none: unmodified texture.</summary>
    public static Vector3f DefaultAmbient => Vector3f.One;

    /// <summary>Gets the settings a level without a <c>GlobalLight</c> gets: no fog, no tint.</summary>
    public static GlobalLightSettings None => new(SceneFog.None, SceneFog.None, Vector3f.One, 0f);

    /// <summary>Reads the keyframe a level opens on.</summary>
    /// <param name="levelRoot">The loaded level's task tree.</param>
    /// <returns>The settings, or <see cref="None"/> when the level declares no keyframe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The lowest time wins, which is what the original's own search arrives at from a time of zero: it
    /// looks for a keyframe below the current time, finds none, and takes the nearest one above.
    /// </remarks>
    public static GlobalLightSettings Read(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        GameTask? lowest = null;
        float lowestTime = float.MaxValue;
        Find(levelRoot);

        return lowest is null ? None : From(lowest, lowestTime);

        void Find(GameTask task)
        {
            if (task is { } candidate
                && string.Equals(candidate.Type.Name, KeyframeType, StringComparison.Ordinal)
                && Number(candidate, "Time") is { } time
                && time < lowestTime)
            {
                lowest = candidate;
                lowestTime = time;
            }

            foreach (GameTask child in task.Children)
            {
                Find(child);
            }
        }
    }

    /// <summary>Builds the settings from one keyframe.</summary>
    /// <param name="keyframe">A <c>GlobalLightKeyframe</c> task.</param>
    /// <param name="time">The keyframe's time key.</param>
    /// <returns>The settings its bands describe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyframe"/> is <see langword="null"/>.</exception>
    public static GlobalLightSettings From(GameTask keyframe, float time)
    {
        ArgumentNullException.ThrowIfNull(keyframe);

        return new GlobalLightSettings(
            Band(keyframe, "terrain"),
            Band(keyframe, "object category 1"),
            Colour(keyframe, "Ambient color object category 1") ?? Vector3f.One,
            time);

        static SceneFog Band(GameTask keyframe, string band) => SceneFog.FromGlobalLight(
            Colour(keyframe, $"Fog color {band}") ?? default,
            Number(keyframe, $"Fog density {band}") ?? 0f);

        static Vector3f? Colour(GameTask keyframe, string name) =>
            keyframe.TryGetScriptValue(name, out object? value) && value is Vector3f colour ? colour : null;
    }

    private static float? Number(GameTask task, string name) =>
        task.TryGetScriptValue(name, out object? value) && value is float number ? number : null;
}

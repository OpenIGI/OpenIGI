using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The five colours the level's <c>FlatSky</c> paints its dome with: the zenith, and a middle and a bottom
/// pair that the dome blends between around each ring.
/// </summary>
/// <param name="Top">The zenith, <c>SkyDome Top Colour</c>.</param>
/// <param name="Middle1">The first of the middle pair.</param>
/// <param name="Middle2">The second of the middle pair.</param>
/// <param name="Bottom1">The first of the horizon pair.</param>
/// <param name="Bottom2">The second of the horizon pair.</param>
/// <remarks>
/// <para>
/// <b>This is the only place a Project I.G.I. level says what hour it is.</b> Its <c>Dirlight</c> cannot:
/// every mission authors that sun at a <c>Time</c> of 12, and the retail handlers registered by
/// <c>sub_526EE0</c> (<c>0x526EE0</c>) never read <c>Time</c> as a clock -- the sun is a bake-time light and
/// a direction for the models with no lightmap. A night mission such as the third authors the same
/// "noon" sun, 24° up and nearly white, as a daylit one. What the original DRAWS as night is this dome.
/// </para>
/// <para>
/// Snapping is applied as the dome builder applies it -- <c>sub_51EB40</c> copies the second colour of
/// each pair over the first -- so what is read here is what is painted.
/// </para>
/// </remarks>
public readonly record struct SkyDomeColours(
    Vector3f Top,
    Vector3f Middle1,
    Vector3f Middle2,
    Vector3f Bottom1,
    Vector3f Bottom2)
{
    /// <summary>The task type the level declares its sky as.</summary>
    public const string TaskTypeName = "FlatSky";

    /// <summary>
    /// Gets the dome's mean luminance: the zenith, the middle pair and the horizon pair weighted alike.
    /// </summary>
    /// <remarks>
    /// Each pair is averaged first because the dome blends within it by azimuth, over a parameter that runs
    /// 1 → 0 → 0.9 around the ring and so gives each member about half of the ring. Rec. 709 weights.
    /// </remarks>
    public float MeanLuminance =>
        (Luminance(Top)
            + ((Luminance(Middle1) + Luminance(Middle2)) * 0.5f)
            + ((Luminance(Bottom1) + Luminance(Bottom2)) * 0.5f)) / 3f;

    /// <summary>Reads the level's dome.</summary>
    /// <param name="levelRoot">The root of the level's task tree.</param>
    /// <param name="colours">Receives the dome, snapped as the original draws it.</param>
    /// <returns><see langword="false"/> when the level declares no <c>FlatSky</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static bool TryRead(GameTask levelRoot, out SkyDomeColours colours)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        colours = default;
        if (Find(levelRoot) is not { } sky)
        {
            return false;
        }

        Vector3f middle2 = Colour(sky, "SkyDome Middle Colour 2");
        Vector3f bottom2 = Colour(sky, "SkyDome Bottom Colour 2");
        bool snap = sky.TryGetValue("SkyDome Snap Colours", out object? value) && value is true;

        colours = new SkyDomeColours(
            Colour(sky, "SkyDome Top Colour"),
            snap ? middle2 : Colour(sky, "SkyDome Middle Colour 1"),
            middle2,
            snap ? bottom2 : Colour(sky, "SkyDome Bottom Colour 1"),
            bottom2);
        return true;
    }

    private static float Luminance(Vector3f c) => (c.X * 0.2126f) + (c.Y * 0.7152f) + (c.Z * 0.0722f);

    private static Vector3f Colour(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3f colour ? colour : default;

    private static DeclaredTask? Find(GameTask task)
    {
        if (task is DeclaredTask declared && string.Equals(declared.Type.Name, TaskTypeName, StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (Find(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

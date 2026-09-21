using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// The level's sun, read from its <c>Dirlight</c> task.
/// </summary>
/// <remarks>
/// <para>
/// Every reading here is measured rather than assumed — see the original,
/// which was written from a probe over the retail data because no re-note recorded these parameters'
/// names and the only statement of what a script <c>Angle</c> means was a doc comment.
/// </para>
/// <para>
/// This closes a standing item carried out of M6: the "residual dimness" left after the diffuse ×
/// lightmap composite was matched is the missing directional term, and this is the data behind it.
/// </para>
/// </remarks>
/// <param name="Direction">The world-space unit vector pointing <em>towards</em> the sun. World Z is up.</param>
/// <param name="Colour">The sun's colour, from the keyframe's <c>Front Color</c>.</param>
/// <param name="Ambient">
/// The sky's contribution, from the keyframe's <c>Back Color</c>. Not a guess from the parameter's
/// name: it matches the sibling <c>GlobalLightKeyframe</c>'s <c>Ambient color terrain</c> exactly.
/// </param>
/// <param name="Beta">The keyframe's zenith angle in radians, as authored.</param>
/// <param name="Gamma">The keyframe's azimuth in radians, as authored and <b>not</b> wrapped.</param>
/// <param name="TimeOfDay">The keyframe's <c>Time</c>, an hour of the day.</param>
/// <param name="Intensity">The <c>Dirlight</c>'s radiosity intensity. 18 in every retail mission.</param>
/// <param name="AffectsTerrain">Whether the light was baked into the terrain. True everywhere in retail.</param>
/// <param name="AffectsObjects">Whether the light was baked into objects. True everywhere in retail.</param>
public readonly record struct DirectionalLightSettings(
    Vector3f Direction,
    Vector3f Colour,
    Vector3f Ambient,
    float Beta,
    float Gamma,
    float TimeOfDay,
    float Intensity,
    bool AffectsTerrain,
    bool AffectsObjects)
{
    /// <summary>The task type the level declares its sun as.</summary>
    public const string TaskTypeName = "Dirlight";

    /// <summary>The task type each of the sun's keyframes is declared as.</summary>
    public const string KeyframeTypeName = "DirlightKeyframe";

    /// <summary>
    /// The highest <c>Time</c> treated as an hour of the day rather than as an unused slot.
    /// </summary>
    /// <remarks>
    /// Every retail mission ships two keyframes, at 12 and at 100, and <b>the one at 100 is identical
    /// in all fourteen</b> — a green sun repeated verbatim, which is an editor template row rather
    /// than authored content. A reader that blended the two by time would pull every level's noon
    /// sunlight towards that green.
    /// </remarks>
    public const float LatestHour = 24f;

    /// <summary>Gets a value indicating whether the sun sits below the horizon.</summary>
    /// <remarks>
    /// True for mission 8, whose <c>Beta</c> of 144.6° puts its sun 55° underground while it carries a
    /// bright near-white colour. Reported rather than corrected: clamping it would hide the one piece
    /// of evidence for whatever explains it.
    /// </remarks>
    public bool IsBelowHorizon => Direction.Z < 0f;

    /// <summary>Reads the level's sun.</summary>
    /// <param name="levelRoot">The root of the level's task tree.</param>
    /// <param name="settings">Receives the sun.</param>
    /// <returns><see langword="false"/> when the level declares no usable <c>Dirlight</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static bool TryRead(GameTask levelRoot, out DirectionalLightSettings settings)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        settings = default;

        if (Find(levelRoot, TaskTypeName) is not { } light)
        {
            return false;
        }

        if (SelectKeyframe(light) is not { } keyframe)
        {
            return false;
        }

        float beta = GetFloat(keyframe, "Beta", 0f);
        float gamma = GetFloat(keyframe, "Gamma", 0f);

        settings = new DirectionalLightSettings(
            DirectionFrom(beta, gamma),
            GetColour(keyframe, "Front Color", new Vector3f(1f, 1f, 1f)),
            GetColour(keyframe, "Back Color", new Vector3f(0.25f, 0.27f, 0.31f)),
            beta,
            gamma,
            GetFloat(keyframe, "Time", 12f),
            GetFloat(light, "Radiosity intensity", 18f),
            GetBool(light, "Affects terrain"),
            GetBool(light, "Affects objects"));

        return true;
    }

    /// <summary>Turns the keyframe's two angles into a world-space direction towards the sun.</summary>
    /// <param name="beta">The ELEVATION above the horizon, in radians.</param>
    /// <param name="gamma">The azimuth, in radians.</param>
    /// <returns>The unit direction towards the sun, in the world's Z-up frame.</returns>
    /// <remarks>
    /// <para>
    /// As both binaries compute it: <c>IGI.exe sub_527320</c> and <c>igi2ed 0x6573C0</c> build
    /// <c>(cos β cos γ, −cos β sin γ, sin β)</c>. So <c>Beta</c> is an ELEVATION and <c>Gamma</c> is the
    /// compass bearing, which is the plain reading of a light that is placed by pointing it.
    /// </para>
    /// <para>
    /// <b>What was here before took the forward row of the engine's Euler composition</b>, and that is
    /// <c>Row3</c> of <c>RotationZ(γ) · RotationY(β)</c> — but <c>Row3</c> of a Z rotation is
    /// <c>(0, 0, 1)</c>, so the product's third row is the Y rotation's alone: <c>(−sin β, 0, cos β)</c>.
    /// Gamma fell out of the arithmetic entirely. Every level's sun stood in the same vertical plane
    /// whatever bearing its authors gave it, and elevation and azimuth were swapped besides — which is
    /// why <c>Beta</c> had to be described as a zenith angle to make the numbers look plausible.
    /// </para>
    /// <para>
    /// The old note reasoned from the elevations the wrong formula produced looking reasonable at noon.
    /// They did; a sun that ignores its bearing still rises to a believable height. Reading the two
    /// binaries settles it in a way that squinting at outputs cannot.
    /// </para>
    /// </remarks>
    public static Vector3f DirectionFrom(float beta, float gamma)
    {
        float cosBeta = MathF.Cos(beta);
        return new Vector3f(
            cosBeta * MathF.Cos(gamma),
            -cosBeta * MathF.Sin(gamma),
            MathF.Sin(beta));
    }

    private static DeclaredTask? SelectKeyframe(DeclaredTask light)
    {
        DeclaredTask? best = null;
        float bestTime = float.MaxValue;

        foreach (GameTask child in light.Children)
        {
            if (child is not DeclaredTask declared
                || !string.Equals(declared.Type.Name, KeyframeTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            float time = GetFloat(declared, "Time", float.MaxValue);

            // Earliest hour that is actually an hour. The out-of-range slot is the template.
            if (time >= 0f && time <= LatestHour && time < bestTime)
            {
                best = declared;
                bestTime = time;
            }
        }

        return best;
    }

    private static DeclaredTask? Find(GameTask task, string typeName)
    {
        if (task is DeclaredTask declared && string.Equals(declared.Type.Name, typeName, StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (Find(child, typeName) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static float GetFloat(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float f ? f : fallback;

    private static bool GetBool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool b && b;

    private static Vector3f GetColour(DeclaredTask task, string name, Vector3f fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3f v ? v : fallback;
}

namespace OpenIGI.Rendering;

/// <summary>
/// What surfaces reflect, and how far the frame is searched to find it.
/// </summary>
/// <remarks>
/// <para>
/// The original has no reflections of any kind: its shiniest surfaces are a brighter texture. That is
/// most obviously wrong on the things a player looks at while standing still — wet concrete, ice, the
/// water in the level's floor plane, the metal of a weapon — because a surface that returns the same
/// colour from every angle reads as painted rather than as polished.
/// </para>
/// <para>
/// <b>Screen space is a real limitation, not an implementation detail.</b> A reflection can only show
/// what is already in the frame, so anything behind the camera, off the edges, or hidden behind
/// something nearer simply is not there to be found. That failure has to be handled rather than
/// hidden: a ray that finds nothing must fall back to a plausible answer and fade into it, because the
/// alternative — a black hole where the reflection should be — is far more distracting than a
/// reflection that is merely approximate.
/// </para>
/// </remarks>
/// <param name="Quality">
/// How many steps a reflected ray takes, or 0 for no screen-space search at all. With none, surfaces
/// still take the fallback, so switching this off makes reflections coarse rather than absent.
/// </param>
/// <param name="Intensity">How strongly the reflection is mixed in, where 1 is the physical amount.</param>
/// <param name="MaxRoughness">
/// The roughness past which a surface stops reflecting sharply enough to be worth marching for. A rough
/// surface scatters its reflection over a wide cone, and a single ray through the middle of that cone
/// is not an approximation of it — it is a mirror image with the wrong shape, which reads as a smear.
/// </param>
/// <param name="Thickness">
/// How thick, in metres, a surface in the depth buffer is assumed to be. A depth buffer records where
/// surfaces start and never where they end, so a ray passing behind something has to be told how far
/// behind counts as "through it" rather than "hit it". Too small and reflections tear where a ray slips
/// behind a thin object; too large and they smear against everything they pass near.
/// </param>
public readonly record struct ReflectionSettings(
    int Quality = 0,
    float Intensity = 1f,
    float MaxRoughness = 0.6f,
    float Thickness = 0.5f)
{
    /// <summary>Gets settings under which nothing reflects.</summary>
    public static ReflectionSettings Off { get; }

    /// <summary>Gets a value indicating whether the pass would do anything.</summary>
    /// <remarks>
    /// True even with no marching steps, because the fallback alone is worth applying: a smooth surface
    /// reflecting the sky roughly is closer to right than one reflecting nothing.
    /// </remarks>
    public bool Enabled => Intensity > 0f && MaxRoughness > 0f;

    /// <summary>Gets a value indicating whether rays are marched through the depth buffer.</summary>
    public bool Marches => Enabled && Quality > 0;
}

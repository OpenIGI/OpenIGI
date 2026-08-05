using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// What a footstep sounds like, and how far it is heard — the rules of <c>0x463920</c>.
/// </summary>
/// <remarks>
/// <para>
/// Animation event <b>1010</b> is by far the commonest event in the retail animation data — 117 of
/// 245 — and it is deliberately absent from the <c>animtrigger</c> table, because a table entry names
/// one fixed sound and a footstep depends on what is underfoot. This is where that choice is made
/// instead.
/// </para>
/// </remarks>
public static class FootstepSounds
{
    /// <summary>The animation event a footstep is.</summary>
    public const int EventId = 1010;

    /// <summary>How far above the ground the material probe starts, in engine units.</summary>
    public const double ProbeAboveInUnits = 204.8;

    /// <summary>How far below its origin the material probe reaches, in engine units.</summary>
    public const double ProbeDepthInUnits = 2048.0;

    /// <summary>Engine units in a metre, the factor a material's hearing radius is authored against.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>
    /// The order the six walk sounds of a material are used in — a fixed twelve-step cycle, not a draw.
    /// </summary>
    /// <remarks>
    /// Transcribed from the twelve dwords at <c>0x53F318</c>, which the handler walks with a per-human
    /// cursor advanced <c>(i + 1) % 12</c>. It is <b>not</b> a permutation: the first six use each
    /// sound once, the second six are an irregular tail in which <c>3</c> appears twice and <c>0</c> not
    /// at all. Authored by hand, so it is copied rather than generated.
    /// </remarks>
    public static ReadOnlySpan<int> Cycle => [0, 1, 2, 3, 4, 5, 3, 1, 4, 2, 3, 5];

    /// <summary>Takes the next walk sound for a material and advances the walker's cursor.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <param name="cursor">The walker's own position in <see cref="Cycle"/>; advanced by one.</param>
    /// <returns>The sound definition's name, or an empty string when the material names none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    public static string Next(GameMaterial material, ref int cursor)
    {
        ArgumentNullException.ThrowIfNull(material);

        int slot = Cycle[((cursor % Cycle.Length) + Cycle.Length) % Cycle.Length];
        cursor = (cursor + 1) % Cycle.Length;

        return slot < material.WalkSounds.Count ? material.WalkSounds[slot] : string.Empty;
    }

    /// <summary>The pitch a footstep plays at.</summary>
    /// <param name="random">The engine's generator.</param>
    /// <returns>A ratio between 0.8 and 1.3.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original computes <c>rand() * 0.5 + 1.0 - 0.2</c>, which is the same thing written the way
    /// the compiler folded it. Two steps on the same surface are never quite the same sound, and this
    /// is the whole of why.
    /// </remarks>
    public static float Pitch(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return (float)((random.NextDouble() * 0.5) + 0.8);
    }

    /// <summary>How far away a step on this material is heard by the AI, in engine units.</summary>
    /// <param name="material">The material under the feet.</param>
    /// <returns>The radius in engine units.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The handler posts its hearing event with the material's own argument-5 number times 4096, so the
    /// ground decides how far a footstep carries as well as what it sounds like — gravel gives a
    /// sneaking player away where carpet does not.
    /// </remarks>
    public static double HearingRadiusInUnits(GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.Value5 * UnitsPerMetre;
    }

    /// <summary>Where to start and end the downward probe that finds the ground material.</summary>
    /// <param name="groundPoint">The point the feet are standing on, in engine units.</param>
    /// <returns>The probe's start and end, in engine units.</returns>
    /// <remarks>
    /// A separate cast from the one that found the ground height: the original probes from 0.05 m above
    /// the ground point down to 0.5 m below it, which is short enough not to punch through a floor into
    /// whatever is under it.
    /// </remarks>
    public static (Vector3d From, Vector3d To) Probe(Vector3d groundPoint) => (
        new Vector3d(groundPoint.X, groundPoint.Y, groundPoint.Z + ProbeAboveInUnits),
        new Vector3d(groundPoint.X, groundPoint.Y, groundPoint.Z - ProbeDepthInUnits));
}

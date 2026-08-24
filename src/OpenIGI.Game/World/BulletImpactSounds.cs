using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// What a bullet strike sounds like — the material's own six sounds, rate-limited as retail does.
/// </summary>
/// <remarks>
/// <para>
/// The material table already carries the answer: every row declares six bullet-impact sounds
/// (<see cref="GameMaterial.BulletSounds"/>), the shot resolution picks one <b>at random</b>, and the
/// whole set is rate-limited <b>globally</b> — a timestamp plus <c>rand(6)+6</c> ticks — so a burst
/// into a wall does not stack six samples. Those are the retail rules; this class is where the
/// ballistic option reuses them.
/// </para>
/// <para>
/// A hit on a human takes the <see cref="FleshMaterialName"/> row, which is exactly what that row is
/// in the table for — like <c>Ground</c>, nothing geometric ever names it.
/// </para>
/// </remarks>
public static class BulletImpactSounds
{
    /// <summary>The material a round striking a body resolves to.</summary>
    public const string FleshMaterialName = "Flesh";

    /// <summary>The fewest ticks between two impact sounds.</summary>
    public const int MinimumQuietTicks = 6;

    /// <summary>The random extra ticks on top, exclusive.</summary>
    public const int RandomQuietTicks = 6;

    /// <summary>Picks one of the material's six impact sounds.</summary>
    /// <param name="material">The material struck.</param>
    /// <param name="random">The stream the pick is drawn from.</param>
    /// <returns>A sound definition's name, or an empty string when the material names none.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static string Pick(GameMaterial material, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(random);

        int count = material.BulletSounds.Count;
        return count == 0 ? string.Empty : material.BulletSounds[(int)random.NextBounded((uint)count)];
    }

    /// <summary>Resolves a contact to the retail material row and picks its impact sound.</summary>
    /// <param name="materials">The loaded material table.</param>
    /// <param name="struckHuman">Whether the contact was a human rather than a surface.</param>
    /// <param name="surfaceMaterial">The world material index for a surface contact.</param>
    /// <param name="random">The stream the pick is drawn from.</param>
    /// <returns>A sound definition's name, or an empty string when no row can provide one.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static string PickForImpact(
        GameMaterialRegistry materials,
        bool struckHuman,
        int surfaceMaterial,
        IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(random);

        if (struckHuman)
        {
            return materials.TryGetByName(FleshMaterialName, out GameMaterial flesh)
                ? Pick(flesh, random)
                : string.Empty;
        }

        // An authored row with no sounds is intentionally silent (Air/Water are examples). Only an
        // unknown collision index uses the terrain row as the defensive fallback; turning a known
        // silent row into Ground audio would make non-solid contacts sound like dirt.
        if (materials.TryGet(surfaceMaterial, out GameMaterial struck))
        {
            return Pick(struck, random);
        }

        return materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground)
            ? Pick(ground, random)
            : string.Empty;
    }

    /// <summary>How long the whole set stays quiet after one plays.</summary>
    /// <param name="random">The stream the extra is drawn from.</param>
    /// <returns>Between six and eleven ticks — a fifth to a third of a second.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    public static int QuietTicks(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return MinimumQuietTicks + (int)random.NextBounded(RandomQuietTicks);
    }
}

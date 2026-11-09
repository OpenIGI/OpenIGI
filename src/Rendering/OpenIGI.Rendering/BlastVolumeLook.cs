using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>One bomb blast as its volume draw reads it at one frame (<c>World/BlastVolume.slang</c>).</summary>
/// <param name="CentreView">The blast's centre in view space, metres.</param>
/// <param name="RadiusMetres">How far it kills, the size the rest is scaled by.</param>
/// <param name="AgeSeconds">How long ago it went off.</param>
/// <param name="Seed">The seed its noise is placed by, the same on every machine that draws it.</param>
/// <param name="FireballRadiusMetres">The fireball's radius.</param>
/// <param name="Heat">How hot the fireball still is, one at the flash.</param>
/// <param name="ColumnHeightMetres">How high the smoke column has climbed above the centre.</param>
/// <param name="SmokeDensity">How thick the smoke is, one at its thickest.</param>
/// <param name="ShockRadiusMetres">Where the shock front is, from the centre.</param>
/// <param name="ShockStrength">How strongly the front bends the picture, nought once it has gone.</param>
/// <param name="FireLight">The fire's light colour, linear.</param>
/// <param name="FireGlow">How brightly the fire glows.</param>
/// <param name="Steps">How many steps the march takes: the quality's.</param>
/// <param name="Kind">What is marched: a bomb's fireball, or a building's collapse dust -- for which
/// <paramref name="CentreView"/> is the footprint's middle on the ground, <paramref name="FireballRadiusMetres"/> the cloud's
/// reach, <paramref name="ColumnHeightMetres"/> its height and <paramref name="SmokeDensity"/> its thickness.</param>
public readonly record struct BlastVolumeLook(
    Vector3f CentreView,
    float RadiusMetres,
    float AgeSeconds,
    uint Seed,
    float FireballRadiusMetres,
    float Heat,
    float ColumnHeightMetres,
    float SmokeDensity,
    float ShockRadiusMetres,
    float ShockStrength,
    Vector3f FireLight,
    float FireGlow,
    int Steps,
    BlastVolumeKind Kind = BlastVolumeKind.Fireball)
{
    /// <summary>Gets the half-size of the cube it is drawn on: its shock front's reach while it lasts, its column's after.</summary>
    /// <remarks>The box the caller draws and the box the shader keeps the far face of are both this one.</remarks>
    public float BoxHalfSize
    {
        get
        {
            if (Kind == BlastVolumeKind.CollapseDust)
            {
                return MathF.Max(MathF.Max(FireballRadiusMetres * 1.2f, (ColumnHeightMetres * 1.2f) + 2f), 1f);
            }

            float column = MathF.Max(FireballRadiusMetres * 1.8f, ColumnHeightMetres + (FireballRadiusMetres * 1.7f));
            float shock = ShockStrength > 0.001f ? ShockRadiusMetres * 1.1f : 0f;
            return MathF.Max(MathF.Max(column, shock), 1f);
        }
    }
}

/// <summary>What a blast volume draw marches.</summary>
public enum BlastVolumeKind
{
    /// <summary>A bomb's fireball, its column and its shock front.</summary>
    Fireball,

    /// <summary>The dust a building throws up as it comes down: no fire, no front, a cloud rolling off its footprint.</summary>
    CollapseDust,
}

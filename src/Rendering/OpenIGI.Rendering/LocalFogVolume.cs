using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// A region of the world where the medium is thicker than the level's base fog.
/// </summary>
/// <remarks>
/// <para>
/// A single height-exponential medium describes outdoor air well and describes nothing else at all:
/// it cannot put mist in a basement that sits above the fog line, or smoke in one room and not the
/// next. These are the exceptions, authored per mission.
/// </para>
/// <para>
/// The retail data has no such entities — there is no fog-volume task in <c>objects.qvm</c> — so these
/// are a deliberate addition of the port's, and only the modern path reads them.
/// </para>
/// </remarks>
/// <param name="Centre">The volume's centre in world metres.</param>
/// <param name="Extent">
/// Half-extents in metres for a box, or the radius in <see cref="Vector3f.X"/> for a sphere.
/// </param>
/// <param name="DensityScale">
/// How much thicker the medium is inside, as a multiple of the level's base density. Values below one
/// thin it instead, which is how an interior is kept clear of an outdoor fog bank.
/// </param>
/// <param name="Shape">Whether the region is a box or a sphere.</param>
public readonly record struct LocalFogVolume(
    Vector3f Centre,
    Vector3f Extent,
    float DensityScale,
    FogVolumeShape Shape = FogVolumeShape.Box)
{
    /// <summary>The most volumes one level may declare.</summary>
    /// <remarks>
    /// Every sample of every ray tests every volume, so this is a per-sample cost multiplier rather
    /// than a per-frame one. Eight is enough for the rooms that need it and cheap enough not to
    /// notice; a level wanting more probably wants a different tool.
    /// </remarks>
    public const int MaxPerLevel = 8;

    /// <summary>Gets the fraction of the volume's edge over which its density fades in.</summary>
    /// <remarks>
    /// A hard boundary is visible as a plane hanging in the air the moment the camera moves through
    /// it, which is worse than having no volume at all.
    /// </remarks>
    public const float EdgeSoftness = 0.7f;

    /// <summary>Gets a value indicating whether the volume would change anything.</summary>
    public bool IsMeaningful => DensityScale != 1f && Extent.X > 0f;
}

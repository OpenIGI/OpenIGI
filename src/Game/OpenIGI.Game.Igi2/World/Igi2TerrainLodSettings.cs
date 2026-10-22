using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The two numbers a level authors to control how finely its ground is tessellated.</summary>
/// <param name="Detail">
/// <c>Terrain_t::vDetail</c>. The angular size an edge has to reach before it is worth splitting: an
/// edge splits when its length over its distance to the eye exceeds this, so a <em>larger</em> value
/// means a coarser ground.
/// </param>
/// <param name="Adaption">
/// <c>Terrain_t::vAdapt</c>. How much local roughness lowers that threshold — the edge test divides
/// <see cref="Detail"/> by <c>1 + 0.5 * roughness * Adaption</c>, so a bumpy patch is refined sooner
/// than a flat one at the same distance.
/// </param>
/// <remarks>
/// <para>
/// Both are read straight out of <c>TerrainRender_Draw</c> (<c>igi2ed 0x6691D3</c>), which is the only
/// place either is consulted:
/// </para>
/// <code>
/// fmul [ctx+1Ch]          ; roughness * vAdapt
/// fmul 0.5
/// fadd 1.0
/// fdivr [ctx+24h]         ; vDetail / (1 + 0.5 * roughness * vAdapt)
/// </code>
/// <para>
/// <c>ctx+0x1C</c> and <c>ctx+0x24</c> are <c>TerrainContext_t::vAdapt</c> and <c>::vDetail</c>, named
/// by the dedicated server's own type information, and the level copies them from the <c>Terrain</c>
/// task's <c>Adaption</c> and <c>Detail</c> parameters.
/// </para>
/// </remarks>
public readonly record struct Igi2TerrainLodSettings(float Detail, float Adaption)
{
    /// <summary>How fast a newly split vertex rises to its true height — <c>TerrainRender_vMorphSpeed</c>.</summary>
    /// <remarks>
    /// Written unconditionally at the top of every frame's <c>TerrainRender_Draw</c>
    /// (<c>igi2ed 0x66873B</c>: <c>mov TerrainRender_vMorphSpeed, 3F000000h</c>). It is a constant in
    /// the shipped build, not a tunable, so it is one here too.
    /// </remarks>
    public const float MorphSpeed = 0.5f;

    /// <summary>How fast a split vertex takes on its own material — <c>TerrainRender_vMaterialMorphSpeed</c>.</summary>
    /// <remarks>
    /// <c>igi2ed 0x668745</c>: <c>mov TerrainRender_vMaterialMorphSpeed, 40000000h</c>. Four times
    /// <see cref="MorphSpeed"/>: the ground's <em>surface</em> reaches its final blend when the geometry
    /// is only halfway up, which hides the material change inside the geometric one.
    /// </remarks>
    public const float MaterialMorphSpeed = 2f;

    /// <summary>The shortest edge the quadtree will ever split, squared, in world units.</summary>
    /// <remarks>
    /// <c>igi2ed 0x669291</c> compares the squared edge length against <c>0x4A800000</c>, which is
    /// exactly 2²² — so an edge shorter than 2,048 world units is never split no matter how close the
    /// eye gets. At 4,096 units to the metre that is a hard floor of half a metre on tessellation, and
    /// it is what stops a camera lying on the ground from subdividing to the sample grid and beyond.
    /// </remarks>
    public const float MinimumSplitEdgeLengthSquared = 4194304f;

    /// <summary>How much steeper than horizontal an edge may run before it stops being refined.</summary>
    /// <remarks>
    /// <c>igi2ed 0x66921F</c> and <c>0x669260</c> each compare <c>|dz|</c> against <c>10 * |dx|</c> and
    /// <c>10 * |dy|</c>; an edge that fails both — a near-vertical one, the wall of a cliff — is refused
    /// a split outright, before the distance test is even reached. Splitting such an edge buys nothing:
    /// its midpoint is already where the height field puts it.
    /// </remarks>
    public const float SteepEdgeRatio = 10f;

    /// <summary>The resolution a split fraction is stored at — <c>TerrainRenderEdge_t::nMorph</c>.</summary>
    /// <remarks>
    /// The original keeps the fraction as a <c>unsigned short</c>: <c>0x66930C</c> multiplies by
    /// 65,535 and <c>fistp</c>s it, and <c>0x669583</c> multiplies by <c>0x37800080</c> — which is
    /// exactly <c>1 / 65535</c> — on the way back out. Quantising here keeps a port's morph on the same
    /// 1/65,535 lattice rather than a slightly different curve.
    /// </remarks>
    public const int MorphResolution = 65535;

    /// <summary>Reads a level's controls off its <c>Terrain</c> task.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The settings, or <see langword="null"/> when the level declares no <c>Terrain</c> task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Two of the 24 shipped levels declare no <c>Terrain</c> at all, which is why this can answer
    /// nothing rather than a default: a level with no terrain task has no ground either, and inventing
    /// numbers for it would hide that.
    /// </remarks>
    public static Igi2TerrainLodSettings? Read(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        return Find(levelRoot) is { } terrain
            ? new Igi2TerrainLodSettings(terrain.Detail, terrain.Adaption)
            : null;
    }

    /// <summary>Gets the threshold an edge's length is compared against, for one sample of roughness.</summary>
    /// <param name="roughness">
    /// How far the height field departs from the straight edge at its midpoint, divided by the square's
    /// own width — a dimensionless slope.
    /// </param>
    /// <returns>The largest length-over-distance ratio an edge may have and still not be split.</returns>
    public readonly float ThresholdFor(float roughness) => Detail / (1f + (0.5f * roughness * Adaption));

    /// <summary>Gets how far a vertex has travelled from its parents' midpoint toward its own height.</summary>
    /// <param name="morph">The edge's split fraction, from zero to one.</param>
    /// <returns>The blend, from zero at the midpoint to one at the height field.</returns>
    /// <remarks>
    /// <c>TerrainRender_CalculateRenderVertex</c> (<c>igi2ed 0x66A76D</c>) writes
    /// <c>(1 - cos(m * pi)) * 0.5 * (z - mid) + mid</c> — a raised cosine, applied to Z alone. It is
    /// flat at both ends, which is the whole point: a vertex appears and disappears with zero velocity,
    /// so the ground does not visibly pop as a square crosses a detail boundary. A straight lerp would
    /// have the same endpoints and a visible kink at each.
    /// </remarks>
    public static float MorphFraction(float morph) =>
        morph >= 1f ? 1f : (float)((1.0 - Math.Cos(morph * Math.PI)) * 0.5);

    private static TerrainTask? Find(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is TerrainTask terrain)
            {
                return terrain;
            }

            if (Find(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

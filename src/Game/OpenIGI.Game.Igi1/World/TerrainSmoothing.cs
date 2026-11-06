using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The smooth ground for each level of detail that is refined: one <see cref="TerrainSmoothField"/> per level.</summary>
/// <remarks>
/// <para>
/// Refining only the finest level reached almost nothing a player looks at. How far that level extends
/// is set by the detail setting, and at Enhanced it ends thirty metres from the camera: everything beyond
/// was the stored eight- and sixteen-metre triangles. So the three finest levels are refined, each onto
/// a surface laid on its own lattice (four, eight and sixteen metres), which at Enhanced carries the
/// smoothing out to about a hundred and twenty-five metres and at Ultra to nearly three hundred.
/// </para>
/// <para>
/// Each level's limit doubles with its lattice. The finest is the ground underfoot and keeps the
/// configured limit; the coarser ones are never stood on at their own detail, and a sixteen-metre
/// triangle needs more room to round off than a four-metre one.
/// </para>
/// <para>
/// Where two levels meet, the finer one slides onto the coarser one as it recedes. Its target is the
/// coarser level's <em>smoothed</em> ground (see <c>TerrainChunkMesh.Smooth</c>), not the stored one, so
/// the seam closes onto what the coarser chunk actually draws.
/// </para>
/// </remarks>
public sealed class TerrainSmoothing
{
    /// <summary>The coarsest level of detail that is refined.</summary>
    /// <remarks>
    /// Also the level the heightmap starts at (<see cref="TerrainTraversal.HeightmapExponent"/>); nothing
    /// coarser is drawn close enough for its facets to read as anything but distant hills.
    /// </remarks>
    public const int CoarsestExponent = TerrainTraversal.HeightmapExponent;

    private readonly TerrainSmoothField[] _fields;

    /// <summary>Initialises a new instance of the <see cref="TerrainSmoothing"/> class.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="subdivisions">How many pieces each edge of a triangle is cut into.</param>
    /// <param name="relax">0 interpolates the ground exactly; 1 is the B-spline that rounds spikes off.</param>
    /// <param name="limitUnits">The finest level's limit, in engine units; each coarser level's is twice the last.</param>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public TerrainSmoothing(TerrainData terrain, int subdivisions, double relax, double limitUnits)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        int finest = TerrainSmoothField.FinestExponent;
        _fields = new TerrainSmoothField[finest - CoarsestExponent + 1];
        for (int exponent = CoarsestExponent; exponent <= finest; exponent++)
        {
            _fields[exponent - CoarsestExponent] = new TerrainSmoothField(
                terrain,
                subdivisions,
                relax,
                limitUnits * (1 << (finest - exponent)),
                exponent);
        }
    }

    /// <summary>Gets whether chunks at a level of detail are refined.</summary>
    /// <param name="exponent">The chunk's level-of-detail exponent.</param>
    /// <returns><see langword="true"/> from <see cref="CoarsestExponent"/> to the finest level.</returns>
    public static bool Refines(int exponent) =>
        exponent >= CoarsestExponent && exponent <= TerrainSmoothField.FinestExponent;

    /// <summary>Gets the field that refines a level of detail, or <see langword="null"/> for a level that is drawn as stored.</summary>
    /// <param name="exponent">The level-of-detail exponent.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public TerrainSmoothField? For(int exponent) =>
        Refines(exponent) ? _fields[exponent - CoarsestExponent] : null;

    /// <summary>Forgets every level's ground over a rectangle, after the ground there changed shape.</summary>
    /// <param name="minX">The rectangle's least X, in engine units.</param>
    /// <param name="minY">The rectangle's least Y.</param>
    /// <param name="maxX">The rectangle's greatest X.</param>
    /// <param name="maxY">The rectangle's greatest Y.</param>
    public void Forget(double minX, double minY, double maxX, double maxY)
    {
        foreach (TerrainSmoothField field in _fields)
        {
            field.Forget(minX, minY, maxX, maxY);
        }
    }
}

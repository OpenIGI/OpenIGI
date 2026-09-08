using System.Collections.ObjectModel;

namespace OpenIGI.Formats.World;

/// <summary>
/// One terrain material, as the <c>CreateTerrainMaterial</c> script native declares it: a flag word and
/// thirty-two bands, one per level-of-detail exponent.
/// </summary>
/// <remarks>
/// <para>
/// The native takes 226 arguments and that number is the layout: <c>1</c> id, <c>1</c> flag word, and
/// <c>32 × 7</c> band values. The original stores the record as 225 consecutive dwords at
/// <c>0xC28FE0 + 900 * id</c> and indexes a band by the drawn chunk's exponent, so a material describes
/// how a ground type is textured at every distance rather than at one.
/// </para>
/// <para>
/// Every retail level registers materials <c>0</c> to <c>6</c>. In level 1's material 0 only the two
/// tile-map ids vary across the bands — <c>(0,0)</c> eight times, then <c>(0,1)</c>, <c>(1,1)</c> twice,
/// <c>(1,2)</c>, and <c>(2,2)</c> for the remaining twenty — which is a coarse-to-fine progression.
/// </para>
/// </remarks>
public sealed class TerrainMaterial
{
    /// <summary>How many bands a material declares.</summary>
    public const int BandCount = 32;

    /// <summary>How many values a band is built from.</summary>
    public const int ValuesPerBand = 7;

    /// <summary>The argument count the original registers the native with.</summary>
    public const int ArgumentCount = 2 + (BandCount * ValuesPerBand);

    private readonly TerrainMaterialBand[] _bands;

    /// <summary>Initializes a new instance of the <see cref="TerrainMaterial"/> class.</summary>
    /// <param name="id">The material id.</param>
    /// <param name="flags">The flag word; <c>1</c> in every retail material.</param>
    /// <param name="bands">Exactly <see cref="BandCount"/> bands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bands"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="bands"/> is not <see cref="BandCount"/> long.</exception>
    public TerrainMaterial(int id, int flags, TerrainMaterialBand[] bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Length != BandCount)
        {
            throw new ArgumentException($"A terrain material has {BandCount} bands, got {bands.Length}.", nameof(bands));
        }

        Id = id;
        Flags = flags;
        _bands = bands;
        Bands = new ReadOnlyCollection<TerrainMaterialBand>(bands);
    }

    /// <summary>Gets the material id.</summary>
    public int Id { get; }

    /// <summary>Gets the flag word, which is really the game material this ground is made of.</summary>
    /// <remarks>
    /// <para>
    /// <b>Argument 1 is not a flag word — it is an index into the game material table.</b>
    /// <c>0x4CB810</c> answers "what is underfoot here" by taking the low byte of this very field
    /// (<c>LOBYTE(materials[225 * id])</c>, the row's first dword) and using it to select
    /// <c>scene+3388+4·index</c> — the same per-level array a mesh face's own 16-bit material index
    /// selects in <c>0x4C1840</c>. The terrain leaf query then writes the result to offset 76 of its
    /// hit block, which becomes <c>Human+1772</c>: the field the footstep handler and both landings
    /// read.
    /// </para>
    /// <para>
    /// The retail values say the same thing plainly. Every terrain material in a level declares one
    /// value, and there are only two across the campaign: <b>1</b> in levels 1–6, 9, 10, 13 and 14, and
    /// <b>20</b> in the four snow missions, 7, 8, 11 and 12. Against <c>material.qvm</c>'s row order
    /// those are <c>Ground</c> and <c>Snow</c> — so walking on the ground outdoors is meant to sound
    /// like earth in most of the game and like snow in the Arctic ones.
    /// </para>
    /// </remarks>
    public int Flags { get; }

    /// <summary>Gets the game material row this ground type is made of — <see cref="Flags"/>, named.</summary>
    public int GameMaterialIndex => Flags;

    /// <summary>Gets the bands, indexed by level-of-detail exponent.</summary>
    public IReadOnlyList<TerrainMaterialBand> Bands { get; }

    /// <summary>Gets the band a chunk's exponent selects.</summary>
    /// <param name="exponent">The chunk's level-of-detail exponent.</param>
    /// <returns>The band.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is outside <c>0</c> to <c>31</c>.</exception>
    public TerrainMaterialBand Band(int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(exponent, BandCount);
        return _bands[exponent];
    }

    /// <inheritdoc/>
    public override string ToString() => $"terrain material {Id} (flags {Flags})";
}

/// <summary>One level-of-detail band of a <see cref="TerrainMaterial"/>: seven consecutive native arguments.</summary>
/// <remarks>
/// <see cref="Scale0"/> and <see cref="Scale1"/> are halved on the way in, as the original stores them.
/// The renderer uses each with its own tile map and its own shift — <c>23 - e</c> for the first pair and
/// <c>22 - e</c> for the second — so the second lays down tiles at twice the density of the first.
/// <see cref="Enabled"/> is <c>TRUE</c> in every retail band and the two middle values are constant at
/// <c>2</c> and <c>0</c>; their meaning is still open.
/// </remarks>
/// <param name="Enabled">The leading flag.</param>
/// <param name="Unknown1">The second value, <c>2</c> throughout retail data.</param>
/// <param name="Unknown2">The third value, <c>0</c> throughout retail data.</param>
/// <param name="Scale0">The first texture scale, already halved.</param>
/// <param name="TileMap0">The tile map the first scale selects tiles from.</param>
/// <param name="Scale1">The second texture scale, already halved.</param>
/// <param name="TileMap1">The tile map the second scale selects tiles from.</param>
public readonly record struct TerrainMaterialBand(
    int Enabled,
    float Unknown1,
    float Unknown2,
    float Scale0,
    int TileMap0,
    float Scale1,
    int TileMap1);

namespace OpenIGI.Formats.World;

/// <summary>
/// One tile map, as the <c>CreateTerrainTileMap</c> script native declares it: an 8×8 grid of tile
/// indices into the level's <c>terrain.tex</c> atlas.
/// </summary>
/// <remarks>
/// <para>
/// The native takes 65 arguments — an id and 64 tiles — and the original writes them straight to
/// <c>0xC30080 + 64 * id</c>. The renderer indexes the grid with three bits of the tiled texture
/// coordinate in each axis, which is what makes it 8×8 rather than a flat list.
/// </para>
/// <para>
/// Every retail level registers maps <c>0</c> to <c>7</c>, except level 8 which stops at <c>6</c>. They
/// are visibly patterns rather than data: level 1's map 0 is all zeros, map 2 alternates <c>3,4</c> over
/// <c>5,6</c> in 2×2 blocks, and map 7 alternates <c>16,17</c> per column.
/// </para>
/// </remarks>
public sealed class TerrainTileMap
{
    /// <summary>The grid's edge length in tiles.</summary>
    public const int Size = 8;

    /// <summary>How many tiles a map holds.</summary>
    public const int TileCount = Size * Size;

    /// <summary>The argument count the original registers the native with.</summary>
    public const int ArgumentCount = 1 + TileCount;

    private readonly byte[] _tiles;

    /// <summary>Initializes a new instance of the <see cref="TerrainTileMap"/> class.</summary>
    /// <param name="id">The tile-map id.</param>
    /// <param name="tiles">Exactly <see cref="TileCount"/> tile indices, row-major.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tiles"/> is not <see cref="TileCount"/> long.</exception>
    public TerrainTileMap(int id, byte[] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Length != TileCount)
        {
            throw new ArgumentException($"A tile map holds {TileCount} tiles, got {tiles.Length}.", nameof(tiles));
        }

        Id = id;
        _tiles = tiles;
    }

    /// <summary>Gets the tile-map id.</summary>
    public int Id { get; }

    /// <summary>Gets the tiles, row-major.</summary>
    public ReadOnlySpan<byte> Tiles => _tiles;

    /// <summary>Gets the tile at a grid cell.</summary>
    /// <param name="x">The column, <c>0</c> to <c>7</c>.</param>
    /// <param name="y">The row, <c>0</c> to <c>7</c>.</param>
    /// <returns>The tile index into the level's terrain texture atlas.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is outside the grid.</exception>
    public byte Tile(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Size);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Size);
        return _tiles[(y * Size) + x];
    }

    /// <inheritdoc/>
    public override string ToString() => $"terrain tile map {Id}";
}

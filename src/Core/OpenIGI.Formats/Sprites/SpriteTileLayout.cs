namespace OpenIGI.Formats.Sprites;

/// <summary>
/// The tile grid that reassembles a large picture from a sprite's frames.
/// </summary>
/// <remarks>
/// <para>
/// Pictures larger than a texture the hardware of the day could hold are stored as a grid of tiles —
/// <c>mainmenu.pic</c> is 150 frames of 64×32 laid out ten across and fifteen down, which is exactly
/// 640×480. The grid lives in a trailing <c>"LOOP"</c> version 6 block that the sprite header points
/// at, and the original parses it in <c>TileGrid_Load</c> (<c>0x4B75C0</c>) into the same cell table a
/// font's character map uses.
/// </para>
/// <para>
/// <b>Cell values are one-based.</b> A cell holds <c>frameIndex + 1</c>, and zero means "no tile".
/// The same convention governs a font's character map; see <see cref="SpriteAsset.GetGlyph"/>.
/// </para>
/// <para>
/// The block also carries two words that look like a picture width and height, and for the large
/// pictures they even hold plausible values — but the original never reads them, and in the small
/// sprites they hold stale pointers left over from whatever wrote the file. They are not exposed here
/// for that reason; the assembled size is <c>Columns × tileWidth</c> by <c>Rows × tileHeight</c>.
/// </para>
/// </remarks>
public sealed class SpriteTileLayout
{
    private readonly byte[] _cells;

    internal SpriteTileLayout(int columns, int rows, byte[] cells)
    {
        Columns = columns;
        Rows = rows;
        _cells = cells;
    }

    /// <summary>Gets the number of tile columns.</summary>
    public int Columns { get; }

    /// <summary>Gets the number of tile rows.</summary>
    public int Rows { get; }

    /// <summary>Gets the raw cell table, row-major, holding one-based frame numbers.</summary>
    public ReadOnlySpan<byte> Cells => _cells;

    /// <summary>Resolves a grid cell to a frame index.</summary>
    /// <param name="column">The zero-based column.</param>
    /// <param name="row">The zero-based row.</param>
    /// <returns>The zero-based frame index, or <c>-1</c> when the cell is empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The column or row is outside the grid.</exception>
    public int GetFrameIndex(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);

        return _cells[(row * Columns) + column] - 1;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Columns}x{Rows} tiles";
}

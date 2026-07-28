namespace OpenIGI.Formats.World;

/// <summary>
/// The eight orientations a terrain octree node can be authored in — the symmetry group of the square,
/// acting on the X and Y octant axes only.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.ctr</c> record stores, per child, both a record index and an orientation byte (<c>+16</c>). The
/// orientation lets one authored subtree be reused at eight different rotations, which is why the eight
/// <c>u16</c>s reference every record in the file several times over: the octree is a DAG, not a tree.
/// </para>
/// <para>
/// The group shows up three times in the original and this type is where all three meet:
/// </para>
/// <list type="bullet">
/// <item>
/// the 64-byte slot table at <c>0x548060</c> that the traversal (<c>sub_4D14E0</c>) copies onto its stack
/// and indexes as <c>table[8 * orientation + octant]</c> to turn a world-space octant into the child slot
/// that holds it — see <see cref="ChildSlot"/>;
/// </item>
/// <item>
/// the composition table at <c>0xB12BE0</c>, filled at run time by <c>sub_4D0EF0</c>, which the descent
/// uses to fold a child's orientation into its parent's — see <see cref="Compose"/>;
/// </item>
/// <item>
/// the flag bits <c>sub_510010</c> applies to a chunk's packed vertices — see <see cref="Transform"/>.
/// </item>
/// </list>
/// <para>
/// All three agree, which is the check <c>RetailTerrainGoldenTests</c> makes: the index is
/// <c>rotation | (reflection &lt;&lt; 2)</c>, quarter turns counted the same way in each.
/// </para>
/// </remarks>
public static class TerrainOrientation
{
    /// <summary>How many orientations exist, and how many children an octree node has.</summary>
    public const int Count = 8;

    /// <summary>The bit of an orientation index that swaps the X and Y axes.</summary>
    public const int SwapAxes = 1;

    /// <summary>The bit of an orientation index that negates.</summary>
    public const int Negate = 2;

    /// <summary>The bit of an orientation index that mirrors X.</summary>
    public const int Mirror = 4;

    /// <summary>The identity orientation, which every descent starts from.</summary>
    public const int Identity = 0;

    // 0x548060, as the traversal reads it: 64 dwords, of which only the low byte of each is used.
    // Row r maps a world-space octant to the child slot that covers it under orientation r. Bit 2 —
    // the Z axis — is fixed in every row, which is what makes this the square's symmetry group rather
    // than the cube's.
    private static readonly byte[] SlotTable =
    [
        0, 1, 2, 3, 4, 5, 6, 7,
        2, 0, 3, 1, 6, 4, 7, 5,
        3, 2, 1, 0, 7, 6, 5, 4,
        1, 3, 0, 2, 5, 7, 4, 6,
        1, 0, 3, 2, 5, 4, 7, 6,
        3, 1, 2, 0, 7, 5, 6, 4,
        2, 3, 0, 1, 6, 7, 4, 5,
        0, 2, 1, 3, 4, 6, 5, 7,
    ];

    /// <summary>Gets the slot table, one row of eight octants per orientation.</summary>
    /// <remarks>Exposed so a test can check it against <see cref="Compose"/> and <see cref="Transform"/>.</remarks>
    public static ReadOnlySpan<byte> Slots => SlotTable;

    /// <summary>Maps a world-space octant to the child slot of a node that holds it.</summary>
    /// <param name="orientation">The node's orientation, <c>0</c> to <c>7</c>.</param>
    /// <param name="octant">The octant, with bit 0 for +X, bit 1 for +Y and bit 2 for +Z.</param>
    /// <returns>The index into the record's eight children and eight orientation bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside <c>0</c> to <c>7</c>.</exception>
    public static int ChildSlot(int orientation, int octant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orientation);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(orientation, Count);
        ArgumentOutOfRangeException.ThrowIfNegative(octant);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(octant, Count);

        return SlotTable[(orientation * Count) + octant];
    }

    /// <summary>Folds a child's stored orientation into its parent's, giving the child's orientation.</summary>
    /// <param name="parent">The orientation the descent reached the parent with.</param>
    /// <param name="child">The byte at <c>.ctr + 16 + slot</c>.</param>
    /// <returns>The orientation to descend into the child with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside <c>0</c> to <c>7</c>.</exception>
    /// <remarks>
    /// This is the closed form of the table <c>sub_4D0EF0</c> builds at <c>0xB12BE0</c>, and it composes
    /// the two the way the descent needs: the parent's mapping runs first, so
    /// <c>Slots[Compose(a, b)][i] == Slots[b][Slots[a][i]]</c>. A reflection reverses the sense of the
    /// rotation that follows it, which is where the subtraction comes from.
    /// </remarks>
    public static int Compose(int parent, int child)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parent);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(parent, Count);
        ArgumentOutOfRangeException.ThrowIfNegative(child);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(child, Count);

        return (parent & Mirror) == 0
            ? (child & Mirror) | ((parent + child) & 3)
            : ((child & Mirror) ^ Mirror) | ((parent - child) & 3);
    }

    /// <summary>Applies an orientation to a chunk vertex's X and Y.</summary>
    /// <param name="orientation">The orientation the descent reached the chunk with.</param>
    /// <param name="x">The unpacked X offset.</param>
    /// <param name="y">The unpacked Y offset.</param>
    /// <returns>The transformed pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="orientation"/> is outside <c>0</c> to <c>7</c>.</exception>
    /// <remarks>
    /// Z is never touched, matching <see cref="ChildSlot"/>'s fixed bit 2. The original writes this out
    /// as nested branches on the three flag bits in <c>sub_510010</c>; the reading is
    /// <see cref="Mirror"/> first, then <see cref="Negate"/>, then <see cref="SwapAxes"/>.
    /// </remarks>
    public static (int X, int Y) Transform(int orientation, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orientation);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(orientation, Count);

        int mirrored = (orientation & Mirror) != 0 ? -x : x;

        if ((orientation & SwapAxes) != 0)
        {
            return (orientation & Negate) != 0 ? (y, -mirrored) : (-y, mirrored);
        }

        return (orientation & Negate) != 0 ? (-mirrored, -y) : (mirrored, y);
    }
}

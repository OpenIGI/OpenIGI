using System.Collections.ObjectModel;

namespace OpenIGI.Formats.World;

/// <summary>
/// A <c>terrain.ctr</c> file: the octree the terrain renderer descends, one 32-byte
/// <see cref="TerrainNode"/> per record.
/// </summary>
/// <remarks>
/// <para>
/// The tree covers a cube of <c>±<see cref="RootExtent"/></c> engine units about the world origin. It is
/// not a coordinate table: a node's position is <em>accumulated during the descent</em>, halving the
/// octant offset at each step, which is why nothing in the file looks like a position.
/// </para>
/// <para>
/// The root is record <b>1</b>, not record 0 — <c>sub_4D14E0</c> starts at <c>ctr + 32</c>. Record 0 is
/// never entered.
/// </para>
/// <para>
/// Because children carry an orientation, one record can serve as many different subtrees; over the
/// retail corpus a record is referenced about six times on average. Consumers must therefore key
/// anything they cache on the node's position, never on its record index.
/// </para>
/// </remarks>
public sealed class TerrainOctree
{
    /// <summary>The half-width of the root cube, in engine units.</summary>
    /// <remarks>
    /// A node at level-of-detail exponent <c>e</c> spans <c>RootExtent &gt;&gt; e</c> about its centre, so
    /// the root itself is exponent zero.
    /// </remarks>
    public const int RootExtent = 0x40000000;

    /// <summary>The record the descent starts at.</summary>
    public const int RootIndex = 1;

    private readonly TerrainNode[] _nodes;

    private TerrainOctree(TerrainNode[] nodes, IReadOnlyList<string> defects)
    {
        _nodes = nodes;
        Defects = defects;
    }

    /// <summary>Gets the nodes, in file order.</summary>
    public ReadOnlySpan<TerrainNode> Nodes => _nodes;

    /// <summary>Gets how many nodes the file holds.</summary>
    public int Count => _nodes.Length;

    /// <summary>Gets the root node.</summary>
    public TerrainNode Root => _nodes[RootIndex];

    /// <summary>Gets the structural problems found while reading, empty for a sound file.</summary>
    /// <remarks>
    /// Reported rather than thrown, because a caller has to be able to decide that a level simply has no
    /// terrain. Level 14 is exactly that case: it fails every invariant the other thirteen satisfy.
    /// </remarks>
    public IReadOnlyList<string> Defects { get; }

    /// <summary>Gets a value indicating whether the tree can be descended.</summary>
    public bool IsUsable => Defects.Count == 0;

    /// <summary>Gets a node by record index.</summary>
    /// <param name="index">The record index.</param>
    /// <returns>The node.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a record.</exception>
    public TerrainNode this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _nodes.Length);
            return _nodes[index];
        }
    }

    /// <summary>Gets the half-width, in engine units, of a node at a level-of-detail exponent.</summary>
    /// <param name="exponent">The exponent, which is the node's depth below the root.</param>
    /// <returns>The half-width.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is negative or past 30.</exception>
    public static int ExtentOf(int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(exponent, 30);
        return RootExtent >> exponent;
    }

    /// <summary>Reads a <c>terrain.ctr</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The tree, whose <see cref="Defects"/> report anything structurally wrong.</returns>
    /// <exception cref="PackFormatException">The file is not a whole number of records, or holds no root.</exception>
    public static TerrainOctree Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length % TerrainNode.Stride != 0)
        {
            throw new PackFormatException(
                $"A terrain octree is {span.Length} bytes, not a whole number of {TerrainNode.Stride}-byte records.");
        }

        int count = span.Length / TerrainNode.Stride;
        if (count <= RootIndex)
        {
            throw new PackFormatException($"A terrain octree needs at least {RootIndex + 1} records, got {count}.");
        }

        TerrainNode[] nodes = new TerrainNode[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = new TerrainNode(span.Slice(i * TerrainNode.Stride, TerrainNode.Stride));
        }

        return new TerrainOctree(nodes, new ReadOnlyCollection<string>(Validate(nodes)));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        IsUsable ? $"terrain octree, {Count} nodes" : $"terrain octree, {Count} nodes, {Defects.Count} defect(s)";

    /// <summary>Checks the invariants every sound retail file satisfies.</summary>
    /// <remarks>
    /// Only occupied slots are checked. An unoccupied slot's index and orientation byte are ignored by the
    /// descent and are not always cleared — <c>0xFF</c> shows up in both.
    /// </remarks>
    private static List<string> Validate(TerrainNode[] nodes)
    {
        List<string> defects = [];
        int danglingChildren = 0;
        int badOrientations = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            TerrainNode node = nodes[i];
            for (int slot = 0; slot < TerrainOrientation.Count; slot++)
            {
                if (!node.HasChild(slot))
                {
                    continue;
                }

                ushort child = node.Child(slot);
                if (child == TerrainNode.NoChild || child >= nodes.Length)
                {
                    danglingChildren++;
                }

                if (node.Orientation(slot) >= TerrainOrientation.Count)
                {
                    badOrientations++;
                }
            }
        }

        if (danglingChildren > 0)
        {
            defects.Add($"{danglingChildren} child link(s) point outside the file");
        }

        if (badOrientations > 0)
        {
            defects.Add($"{badOrientations} child orientation(s) are not 0..7");
        }

        return defects;
    }
}

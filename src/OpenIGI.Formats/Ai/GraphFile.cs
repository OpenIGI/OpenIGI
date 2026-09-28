using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>
/// A <c>MISSION:graphs/graph&lt;n&gt;.dat</c> — an AI navigation graph and its precomputed route table.
/// </summary>
/// <remarks>
/// <para>
/// Decoded from the loader at <c>0x4F9FF0</c> and the eleven field handlers the <c>Graph</c> and
/// <c>AIGraph</c> registrars install (<c>0x4FAA60</c>, <c>0x508F80</c>). The <c>&lt;n&gt;</c> is the id of
/// the task that loads it (<c>0x4FAE50</c> formats the name from it), which is why a level's graph numbers
/// are scattered — level 1 ships 1, 2, 3, 6, 7, 8, 12, 13, 14, 15 and 4019.
/// </para>
/// <para>
/// <b>Two variants, told apart by the first four bytes.</b> With the magic
/// <see cref="Magic"/> the file is a stream of <em>named</em> fields, each addressed by
/// <see cref="GraphFieldHash"/>; without it the loader rewinds and reads the same values
/// <em>positionally</em> in a fixed order (<c>0x4FA160</c>). 293 of the 300 retail files are named; the
/// seven positional ones are all in level 8.
/// </para>
/// <para>
/// <b>Nothing in the file marks where a node ends.</b> The engine stages fields into one buffer and commits
/// a node when its <c>nNodeCriteria</c> arrives and a link when its <c>eLinkType</c> arrives; an
/// <c>nNodeID</c> clears the staging buffer and an <c>nLinkID1</c> resets both link ids to <c>-1</c>. That
/// is the whole record structure, and it is reproduced here field for field rather than assumed to be a
/// fixed group — a file that omitted, reordered or repeated a field would behave the same way as in the
/// original.
/// </para>
/// <para>
/// The record order is uniform across the corpus all the same: <c>nMaxNodes</c>, <c>RouteTable</c>, then
/// every node, then every link. 203 files have links and 90 have none.
/// </para>
/// </remarks>
public sealed class GraphFile
{
    /// <summary>The four bytes a named graph file opens with, stored <c>CC DD EE FF</c>.</summary>
    public const uint Magic = 0xFFEEDDCC;

    private static readonly (string Name, GraphValueType Type)[] FieldTable =
    [
        ("nMaxNodes", GraphValueType.Int32),
        ("nNodeID", GraphValueType.Int32),
        ("tNodePosition", GraphValueType.Real64x3),
        ("vNodeGamma", GraphValueType.Real32),
        ("vNodeRadius", GraphValueType.Real32),
        ("nNodeMaterial", GraphValueType.Int32),
        ("nNodeCriteria", GraphValueType.String8),
        ("nLinkID1", GraphValueType.Int32),
        ("nLinkID2", GraphValueType.Int32),
        ("eLinkType", GraphValueType.Int32),
        ("RouteTable", GraphValueType.Blob),
    ];

    private static readonly Dictionary<uint, (string Name, GraphValueType Type)> FieldsByHash =
        BuildFieldTable();

    private readonly GraphNode?[] _slots;

    private GraphFile(
        bool positional,
        int maxNodes,
        GraphNode?[] slots,
        List<GraphNode> nodes,
        List<GraphLink> links,
        GraphRouteTable? routeTable,
        List<GraphPositionalNodeExtras> extras,
        List<uint> unknownFields,
        List<int> discardedNodeIds,
        int consumed,
        int length)
    {
        IsPositional = positional;
        MaxNodes = maxNodes;
        _slots = slots;
        Nodes = nodes;
        Links = links;
        RouteTable = routeTable;
        PositionalExtras = extras;
        UnknownFieldHashes = unknownFields;
        DiscardedNodeIds = discardedNodeIds;
        BytesConsumed = consumed;
        Length = length;
    }

    /// <summary>Gets a value indicating whether the file was read positionally rather than by field name.</summary>
    public bool IsPositional { get; }

    /// <summary>Gets the slot count the file declares — <c>nMaxNodes</c>, and the route table's dimension.</summary>
    /// <remarks>
    /// <b>Not uniform</b>, though it looks it from a sample: 274 retail graphs declare 100 slots, 19 declare
    /// 200, four declare 300 and three declare 400. The route table is square over this number, so a graph
    /// that declares 400 carries 1.28 MB of table for however few nodes it fills.
    /// </remarks>
    public int MaxNodes { get; }

    /// <summary>Gets the occupied nodes, in the order the file commits them.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary>Gets the links, in the order the file commits them.</summary>
    public IReadOnlyList<GraphLink> Links { get; }

    /// <summary>Gets the route table, or <see langword="null"/> when the file declares none.</summary>
    /// <remarks>
    /// The engine allocates one only when <c>nMaxNodes &gt; 1</c> (<c>0x5092E0</c>), so a one-slot graph has
    /// no table at all rather than a one-entry one. Every retail file declares far more than one slot and
    /// carries a table.
    /// </remarks>
    public GraphRouteTable? RouteTable { get; }

    /// <summary>Gets the per-node data only the positional variant carries, in node order.</summary>
    /// <remarks>Empty for a named file, which has no equivalent.</remarks>
    public IReadOnlyList<GraphPositionalNodeExtras> PositionalExtras { get; }

    /// <summary>Gets the hashes of any fields the file names that the engine does not register.</summary>
    /// <remarks>
    /// <b>Empty for all 300 retail files</b>, which is what makes the eleven registered names the complete
    /// vocabulary. A non-empty list is worth attending to: skipping an unknown field only works when its
    /// data is not a blob (see <see cref="GraphStreamReader.SkipValue"/>).
    /// </remarks>
    public IReadOnlyList<uint> UnknownFieldHashes { get; }

    /// <summary>Gets the ids of nodes the file declared and the engine dropped for being too large.</summary>
    /// <remarks>
    /// <c>0x4F9510</c> returns without doing anything for an id of <see cref="GraphNode.IdLimit"/> or more,
    /// and the loader does not notice. Empty for every retail file; the largest id shipped is 380.
    /// </remarks>
    public IReadOnlyList<int> DiscardedNodeIds { get; }

    /// <summary>
    /// Gets or sets the owning <c>AIGraph</c> task's "Node cover midoffset", in metres -- its <c>+128</c>, which
    /// the free-position can-shoot ray starts that far above a candidate node (<c>sub_48B6E0</c>).
    /// </summary>
    /// <remarks>
    /// The constructor's default is 1.0, but levels set it per graph: mission 1's main compound graph (task
    /// 4019, 264 nodes) says 0.5 while its small graphs say 1. Read live from a retail run with the probe.
    /// </remarks>
    public double CoverMidOffsetMetres { get; set; } = 1.0;

    /// <summary>
    /// Transforms every node's position from the graph's own frame into world space by adding an offset.
    /// </summary>
    /// <param name="graphPosition">The graph task's declared <c>Position</c>, in engine units.</param>
    /// <remarks>
    /// <para>
    /// A graph file stores node positions relative to the <c>AIGraph</c> task that loaded it.
    /// <c>0x4F9770</c> adds the graph's own position (<c>+32/+40/+48</c> of the owning task) every time a
    /// caller asks for a node's world position. Calling this once at load time has the same effect.
    /// </para>
    /// <para>
    /// The original also supports a rotation when <c>graph+100</c> is set, but no shipped graph uses it
    /// (all have identity orientation). If rotation matters later, this is where it goes.
    /// </para>
    /// </remarks>
    public void ApplyWorldOffset(Vector3d graphPosition)
    {
        foreach (GraphNode node in Nodes)
        {
            // 0x4F9770 adds the graph's position to the node's on every read, rounding toward zero.
            node.Position = new Vector3d(
                X87.Add(graphPosition.X, node.Position.X),
                X87.Add(graphPosition.Y, node.Position.Y),
                X87.Add(graphPosition.Z, node.Position.Z));
        }
    }

    /// <summary>Gets how many bytes the walk consumed.</summary>
    public int BytesConsumed { get; }

    /// <summary>Gets the file's length in bytes.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the walk consumed the file exactly.</summary>
    /// <remarks><see langword="true"/> for all 300 retail files, in both variants.</remarks>
    public bool IsExact => BytesConsumed == Length;

    /// <summary>Gets the node in a slot.</summary>
    /// <param name="id">The node id.</param>
    /// <returns>The node, or <see langword="null"/> when the slot is empty or out of range.</returns>
    /// <remarks>
    /// This is <c>0x4F9720</c>, which returns nothing for a negative id, an id past the capacity, or a slot
    /// whose id field is still <c>-1</c>. It does not throw, and callers rely on that.
    /// </remarks>
    public GraphNode? NodeAt(int id) =>
        id < 0 || id >= _slots.Length ? null : _slots[id];

    /// <summary>Gets every link touching a node.</summary>
    /// <param name="id">The node id.</param>
    /// <returns>The links, in file order.</returns>
    public IReadOnlyList<GraphLink> LinksOf(int id)
    {
        List<GraphLink> found = [];
        foreach (GraphLink link in Links)
        {
            if (link.NodeA == id || link.NodeB == id)
            {
                found.Add(link);
            }
        }

        return found;
    }

    /// <summary>Reads a graph file, picking the variant from its first four bytes.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="GraphFormatException">The stream runs out mid-value, or a node id repeats.</exception>
    public static GraphFile Read(ReadOnlyMemory<byte> data)
    {
        var reader = new GraphStreamReader(data);
        if (data.Length >= 4 && reader.ReadRawUInt32() == Magic)
        {
            return ReadNamed(reader, data.Length);
        }

        reader.Rewind();
        return ReadPositional(reader, data.Length);
    }

    private static Dictionary<uint, (string, GraphValueType)> BuildFieldTable()
    {
        Dictionary<uint, (string, GraphValueType)> map = [];
        foreach ((string name, GraphValueType type) in FieldTable)
        {
            uint hash = GraphFieldHash.Of(name);
            if (!map.TryAdd(hash, (name, type)))
            {
                // The original's own registrar treats this as fatal ("GraphStream - collision in hash").
                throw new InvalidOperationException($"Graph field hash collision on '{name}'.");
            }
        }

        return map;
    }

    private static GraphFile ReadNamed(GraphStreamReader reader, int length)
    {
        var builder = new Builder(positional: false);

        // The staging buffer 0x4FAF50 clears and 0x4FB030 commits from. Kept as one mutable group so a
        // file that omits or reorders a field behaves exactly as the original's globals would.
        int stagedId = 0;
        Vector3d stagedPosition = default;
        float stagedGamma = 0;
        float stagedRadius = 0;
        int stagedMaterial = 0;
        int linkA = -1;
        int linkB = -1;

        while (!reader.IsAtEnd)
        {
            uint hash = reader.ReadUInt32();
            if (!FieldsByHash.TryGetValue(hash, out (string Name, GraphValueType Type) field))
            {
                builder.UnknownFields.Add(hash);
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            var declared = (GraphValueType)reader.ReadByte();
            switch (field.Name)
            {
                case "nMaxNodes":
                    builder.SetMaxNodes(reader.ReadInt32());
                    break;

                case "nNodeID":
                    // 0x4FAF50 memsets all 576 staged bytes, which covers the material slot too.
                    stagedId = reader.ReadInt32();
                    stagedPosition = default;
                    stagedGamma = 0;
                    stagedRadius = 0;
                    stagedMaterial = 0;
                    break;

                case "tNodePosition":
                    stagedPosition = reader.ReadVector3d();
                    break;

                case "vNodeGamma":
                    stagedGamma = reader.ReadSingle();
                    break;

                case "vNodeRadius":
                    stagedRadius = reader.ReadSingle();
                    break;

                case "nNodeMaterial":
                    // 0x4FAFE0 stores a null material for -1 rather than resolving one.
                    stagedMaterial = reader.ReadInt32();
                    break;

                case "nNodeCriteria":
                    builder.AddNode(stagedId, stagedPosition, stagedGamma, stagedRadius, stagedMaterial, reader.ReadString());
                    break;

                case "nLinkID1":
                    // 0x4FB080 resets both ids before reading the first.
                    linkA = reader.ReadInt32();
                    linkB = -1;
                    break;

                case "nLinkID2":
                    linkB = reader.ReadInt32();
                    break;

                case "eLinkType":
                    builder.Links.Add(new GraphLink(linkA, linkB, reader.ReadInt32()));
                    break;

                case "RouteTable":
                    builder.ReadRouteTable(reader);
                    break;

                default:
                    throw new GraphFormatException($"Field '{field.Name}' has no reader.");
            }

            if (declared != field.Type)
            {
                throw new GraphFormatException(
                    $"Field '{field.Name}' declares type {declared} where the engine registers {field.Type}.");
            }
        }

        return builder.Build(reader.Position, length);
    }

    private static GraphFile ReadPositional(GraphStreamReader reader, int length)
    {
        var builder = new Builder(positional: true);

        int nodeCount = reader.ReadInt32();
        builder.SetMaxNodes(reader.ReadInt32());
        int linkCount = reader.ReadInt32();

        // 0x4FA160 assigns the resolved material only when the field is present and not -1, and never
        // clears it — so a node without one would silently inherit the previous node's. No retail node
        // does: all 130 positional nodes carry a material and none of them is -1.
        int carriedMaterial = 0;

        for (int i = 0; i < nodeCount; i++)
        {
            int id = reader.ReadInt32();
            Vector3d position = reader.ReadVector3d();
            float gamma = reader.ReadSingle();
            float radius = reader.ReadSingle();

            float? extra = null;
            if (reader.PeekTag() == GraphValueType.Real32)
            {
                extra = reader.ReadSingle();
            }

            if (reader.PeekTag() == GraphValueType.Int32)
            {
                int material = reader.ReadInt32();
                if (material != -1)
                {
                    carriedMaterial = material;
                }
            }

            builder.AddNode(id, position, gamma, radius, carriedMaterial, reader.ReadString());

            // 0x509480 - AIGraph's per-node hook: two counted lists of integer pairs, all discarded.
            builder.Extras.Add(new GraphPositionalNodeExtras(id, extra, ReadPairs(reader), ReadPairs(reader)));
        }

        for (int i = 0; i < linkCount; i++)
        {
            int a = reader.ReadInt32();
            int b = reader.ReadInt32();
            builder.Links.Add(new GraphLink(a, b, reader.ReadInt32()));
        }

        // The route table arrives through the post-load message (0x509450), after the links.
        if (!reader.IsAtEnd)
        {
            builder.ReadRouteTable(reader);
        }

        return builder.Build(reader.Position, length);
    }

    private static (int First, int Second)[] ReadPairs(GraphStreamReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0)
        {
            throw new GraphFormatException($"A graph node hook list cannot hold {count} pairs.");
        }

        var pairs = new (int First, int Second)[count];
        for (int i = 0; i < count; i++)
        {
            pairs[i] = (reader.ReadInt32(), reader.ReadInt32());
        }

        return pairs;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(IsPositional ? "positional" : "named")} graph, {Nodes.Count} of {MaxNodes} slots, {Links.Count} links";

    private sealed class Builder(bool positional)
    {
        private GraphNode?[] _slots = [];
        private int _maxNodes;
        private GraphRouteTable? _routeTable;

        public List<GraphNode> Nodes { get; } = [];

        public List<GraphLink> Links { get; } = [];

        public List<GraphPositionalNodeExtras> Extras { get; } = [];

        public List<uint> UnknownFields { get; } = [];

        public List<int> Discarded { get; } = [];

        public void SetMaxNodes(int maxNodes)
        {
            if (maxNodes < 0)
            {
                throw new GraphFormatException($"A graph cannot declare {maxNodes} slots.");
            }

            _maxNodes = maxNodes;
            Array.Resize(ref _slots, Math.Max(_slots.Length, maxNodes));
        }

        public void AddNode(int id, Vector3d position, float gamma, float radius, int materialId, string criteriaName)
        {
            if (id >= GraphNode.IdLimit)
            {
                // 0x4F9510's first line: too large, dropped without a word.
                Discarded.Add(id);
                return;
            }

            if (id < 0)
            {
                throw new GraphFormatException($"A graph node cannot have id {id}.");
            }

            if (id >= _slots.Length)
            {
                // 0x4F9620 grows the array for an id past the declared capacity.
                Array.Resize(ref _slots, id + 1);
            }

            if (_slots[id] is not null)
            {
                // The original prints "GRAPHNODE ID ALREADY USED" and hangs.
                throw new GraphFormatException($"Graph node id {id} is used twice.");
            }

            var node = new GraphNode(
                id,
                position,
                gamma,
                radius,
                materialId,
                criteriaName,
                GraphNode.ResolveCriteria(criteriaName));
            _slots[id] = node;
            Nodes.Add(node);
        }

        public void ReadRouteTable(GraphStreamReader reader)
        {
            // 0x5092E0 allocates only above one slot, and always 8 * maxNodes^2 bytes; 0x4FA9D0 then reads
            // exactly that many, ignoring the length the blob declares.
            int expected = _maxNodes > 1 ? GraphRouteTable.EntryStride * _maxNodes * _maxNodes : 0;
            ReadOnlyMemory<byte> bytes = reader.ReadBlob(out _, out int declared, expected);
            if (declared != expected)
            {
                throw new GraphFormatException(
                    $"A route table blob declares {declared} bytes where {_maxNodes} slots need {expected}.");
            }

            _routeTable = expected == 0 ? null : new GraphRouteTable(bytes, _maxNodes);
        }

        public GraphFile Build(int consumed, int length) => new(
            positional,
            _maxNodes,
            _slots,
            Nodes,
            Links,
            _routeTable,
            Extras,
            UnknownFields,
            Discarded,
            consumed,
            length);
    }
}

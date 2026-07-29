using System.Buffers.Binary;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Unit tests for the GraphStream reader and the nav-graph file, over streams built here rather than read
/// from an installation. <see cref="RetailGraphGoldenTests"/> covers the corpus.
/// </summary>
public sealed class GraphStreamTests
{
    /// <summary>
    /// The hashes the eleven registered field names produce are the values the retail files actually store.
    /// Recovered from the bytes of <c>level1/graphs/graph1.dat</c> before the hash function was read, then
    /// reproduced by it — which is what identifies both.
    /// </summary>
    [Theory]
    [InlineData("nMaxNodes", 0x0D3AE6u)]
    [InlineData("nNodeID", 0x0735CEu)]
    [InlineData("tNodePosition", 0x1D4295u)]
    [InlineData("vNodeGamma", 0x0F7E9Cu)]
    [InlineData("vNodeRadius", 0x143023u)]
    [InlineData("nNodeMaterial", 0x1BB629u)]
    [InlineData("nNodeCriteria", 0x1BD3E5u)]
    [InlineData("nLinkID1", 0x09104Au)]
    [InlineData("nLinkID2", 0x0918F6u)]
    [InlineData("eLinkType", 0x0DA923u)]
    [InlineData("RouteTable", 0x0FDE0Du)]
    public void TheFieldHashesAreTheOnesTheFilesStore(string name, uint expected) =>
        Assert.Equal(expected, GraphFieldHash.Of(name));

    /// <summary>The hash folds case, so a name registered in any case hashes the same.</summary>
    [Fact]
    public void TheHashIsCaseInsensitive() =>
        Assert.Equal(GraphFieldHash.Of("RouteTable"), GraphFieldHash.Of("routetable"));

    /// <summary>
    /// The multiplier is kept in sixteen bits, so it wraps after 207 characters. Nothing shipped is that
    /// long, but a hash that widened the multiplier would differ here — which is the only way to tell the
    /// two implementations apart.
    /// </summary>
    [Fact]
    public void TheMultiplierWrapsAtSixteenBits()
    {
        // 1 + 317k first exceeds 65535 at k = 207, so character 207 is multiplied by the wrapped value.
        string name = new('A', 208);
        uint wrapped = GraphFieldHash.Of(name);

        uint widened = 0;
        uint multiplier = 1;
        for (int i = 0; i < name.Length; i++)
        {
            widened += multiplier * 'A';
            multiplier += GraphFieldHash.MultiplierStep;
        }

        Assert.NotEqual(widened, wrapped);
    }

    /// <summary>Every tagged value reads back what was written, at the width its tag implies.</summary>
    [Fact]
    public void TaggedValuesRoundTrip()
    {
        var w = new GraphStreamWriter();
        w.Byte(GraphValueType.Bool8, 1);
        w.Int16(-3);
        w.Int32(-70000);
        w.UInt32(0xDEADBEEF);
        w.Single(0.5f);
        w.Double(-2.25);
        w.Vector3d(new Vector3d(1.5, -2.5, 3.5));
        w.String("NODECRITERIA_DOOR");

        var r = new GraphStreamReader(w.ToArray());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(-3, r.ReadInt16());
        Assert.Equal(-70000, r.ReadInt32());
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
        Assert.Equal(0.5f, r.ReadSingle());
        Assert.Equal(-2.25, r.ReadDouble());
        Assert.Equal(new Vector3d(1.5, -2.5, 3.5), r.ReadVector3d());
        Assert.Equal("NODECRITERIA_DOOR", r.ReadString());
        Assert.True(r.IsAtEnd);
    }

    /// <summary>
    /// A string's length byte counts the NUL, so the empty criteria every ordinary node carries is stored as
    /// a length of one. Reading it as a plain count would leave the stream one byte behind on every node.
    /// </summary>
    [Fact]
    public void AnEmptyStringIsStoredAsALengthOfOne()
    {
        var w = new GraphStreamWriter();
        w.String(string.Empty);
        byte[] bytes = w.ToArray();

        Assert.Equal(3, bytes.Length);
        Assert.Equal((byte)GraphValueType.String8, bytes[0]);
        Assert.Equal(1, bytes[1]);
        Assert.Equal(0, bytes[2]);
        Assert.Equal(string.Empty, new GraphStreamReader(bytes).ReadString());
    }

    /// <summary>Peeking does not move the cursor — the positional variant's optional fields depend on it.</summary>
    [Fact]
    public void PeekingDoesNotConsume()
    {
        var w = new GraphStreamWriter();
        w.Single(1.0f);
        var r = new GraphStreamReader(w.ToArray());

        Assert.Equal(GraphValueType.Real32, r.PeekTag());
        Assert.Equal(GraphValueType.Real32, r.PeekTag());
        Assert.Equal(0, r.Position);
        Assert.Equal(1.0f, r.ReadSingle());
    }

    /// <summary>Skipping walks a value by its tag alone, without knowing the field.</summary>
    [Fact]
    public void SkippingUsesTheTagAlone()
    {
        var w = new GraphStreamWriter();
        w.Vector3d(new Vector3d(1, 2, 3));
        w.String("abc");
        w.Int32(7);
        var r = new GraphStreamReader(w.ToArray());

        Assert.Equal(GraphValueType.Real64x3, r.SkipValue());
        Assert.Equal(GraphValueType.String8, r.SkipValue());
        Assert.Equal(7, r.ReadInt32());
        Assert.True(r.IsAtEnd);
    }

    /// <summary>
    /// An unrecognised tag advances by the tag byte and nothing else. The original's switch has no default
    /// arm, so a damaged stream crawls forward rather than stopping — reproduced deliberately.
    /// </summary>
    [Fact]
    public void AnUnrecognisedTagAdvancesByOneByte()
    {
        var r = new GraphStreamReader(new byte[] { 99, 0, 0, 0 });
        Assert.Equal((GraphValueType)99, r.SkipValue());
        Assert.Equal(1, r.Position);
    }

    /// <summary>A value that runs off the end is refused rather than read from beyond the buffer.</summary>
    [Fact]
    public void ATruncatedValueIsRefused()
    {
        var r = new GraphStreamReader(new byte[] { (byte)GraphValueType.Int32, 1, 2 });
        Assert.Throws<GraphFormatException>(() => r.ReadInt32());
    }

    /// <summary>
    /// A named file commits a node when its criteria arrives and a link when its type arrives, which is the
    /// whole of the record structure.
    /// </summary>
    [Fact]
    public void ANamedFileCommitsOnCriteriaAndOnLinkType()
    {
        byte[] bytes = BuildNamedGraph(
            maxNodes: 4,
            nodes:
            [
                (1, new Vector3d(0, 0, 0), 0.0f, 1.0f, 6, string.Empty),
                (2, new Vector3d(4096, 0, 0), 0.25f, 0.5f, -1, "NODECRITERIA_DOOR"),
            ],
            links: [(1, 2, 1)]);

        var graph = GraphFile.Read(bytes);

        Assert.False(graph.IsPositional);
        Assert.True(graph.IsExact);
        Assert.Equal(4, graph.MaxNodes);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Empty(graph.UnknownFieldHashes);
        Assert.Empty(graph.DiscardedNodeIds);

        GraphNode? first = graph.NodeAt(1);
        Assert.NotNull(first);
        Assert.Equal(new Vector3d(0, 0, 0), first.Position);
        Assert.Equal(6, first.MaterialId);
        Assert.Equal(GraphNodeCriteria.None, first.Criteria);

        GraphNode? second = graph.NodeAt(2);
        Assert.NotNull(second);
        Assert.Equal(GraphNodeCriteria.Door, second.Criteria);
        Assert.Equal(-1, second.MaterialId);
        Assert.Equal(0.5f, second.Radius);

        // Slot 0 and slot 3 are declared but empty, and asking for them is not an error.
        Assert.Null(graph.NodeAt(0));
        Assert.Null(graph.NodeAt(3));
        Assert.Null(graph.NodeAt(-1));
        Assert.Null(graph.NodeAt(4));

        Assert.Equal(new GraphLink(1, 2, 1), Assert.Single(graph.Links));
        Assert.Equal(2, graph.LinksOf(1)[0].Other(1));
    }

    /// <summary>
    /// An <c>nNodeID</c> clears every staged field, so a node that omits one gets the cleared value rather
    /// than the previous node's. That is <c>0x4FAF50</c>'s memset, and it is the reason a partially written
    /// node is harmless.
    /// </summary>
    [Fact]
    public void ANodeIdClearsTheStagedFields()
    {
        var w = new GraphStreamWriter();
        w.RawUInt32(GraphFile.Magic);
        w.Field("nMaxNodes", GraphValueType.Int32, x => x.Int32(4));
        w.Field("RouteTable", GraphValueType.Blob, x => x.Blob(GraphValueType.Blob, new byte[8 * 4 * 4]));

        // A complete node.
        w.Field("nNodeID", GraphValueType.Int32, x => x.Int32(1));
        w.Field("tNodePosition", GraphValueType.Real64x3, x => x.Vector3d(new Vector3d(9, 9, 9)));
        w.Field("vNodeGamma", GraphValueType.Real32, x => x.Single(7.0f));
        w.Field("vNodeRadius", GraphValueType.Real32, x => x.Single(3.0f));
        w.Field("nNodeMaterial", GraphValueType.Int32, x => x.Int32(11));
        w.Field("nNodeCriteria", GraphValueType.String8, x => x.String(string.Empty));

        // A node that declares nothing but its id and its criteria.
        w.Field("nNodeID", GraphValueType.Int32, x => x.Int32(2));
        w.Field("nNodeCriteria", GraphValueType.String8, x => x.String(string.Empty));

        var graph = GraphFile.Read(w.ToArray());

        GraphNode? bare = graph.NodeAt(2);
        Assert.NotNull(bare);
        Assert.Equal(default, bare.Position);
        Assert.Equal(0.0f, bare.Gamma);
        Assert.Equal(0.0f, bare.Radius);
        Assert.Equal(0, bare.MaterialId);
    }

    /// <summary>An <c>nLinkID1</c> resets the second id to <c>-1</c>, which <c>0x4FB080</c> does explicitly.</summary>
    [Fact]
    public void ALinkIdOneResetsTheSecondId()
    {
        var w = new GraphStreamWriter();
        w.RawUInt32(GraphFile.Magic);
        w.Field("nMaxNodes", GraphValueType.Int32, x => x.Int32(4));
        w.Field("nLinkID1", GraphValueType.Int32, x => x.Int32(1));
        w.Field("nLinkID2", GraphValueType.Int32, x => x.Int32(2));
        w.Field("eLinkType", GraphValueType.Int32, x => x.Int32(1));
        w.Field("nLinkID1", GraphValueType.Int32, x => x.Int32(3));
        w.Field("eLinkType", GraphValueType.Int32, x => x.Int32(1));

        var graph = GraphFile.Read(w.ToArray());

        Assert.Equal(2, graph.Links.Count);
        Assert.Equal(new GraphLink(1, 2, 1), graph.Links[0]);
        Assert.Equal(new GraphLink(3, -1, 1), graph.Links[1]);
    }

    /// <summary>A node id at or above the limit is dropped without a word, as <c>0x4F9510</c> drops it.</summary>
    [Fact]
    public void ANodeIdAtTheLimitIsSilentlyDropped()
    {
        byte[] bytes = BuildNamedGraph(
            maxNodes: 4,
            nodes: [(GraphNode.IdLimit, default, 0, 0, -1, string.Empty)],
            links: []);

        var graph = GraphFile.Read(bytes);

        Assert.Empty(graph.Nodes);
        Assert.Equal(GraphNode.IdLimit, Assert.Single(graph.DiscardedNodeIds));
    }

    /// <summary>A node id past the declared capacity grows the array, as <c>0x4F9620</c> does.</summary>
    [Fact]
    public void ANodeIdPastTheCapacityGrowsTheArray()
    {
        byte[] bytes = BuildNamedGraph(
            maxNodes: 2,
            nodes: [(9, new Vector3d(1, 2, 3), 0, 1, -1, string.Empty)],
            links: []);

        var graph = GraphFile.Read(bytes);

        Assert.Equal(2, graph.MaxNodes);
        Assert.NotNull(graph.NodeAt(9));
    }

    /// <summary>A repeated node id is fatal in the original and refused here.</summary>
    [Fact]
    public void ARepeatedNodeIdIsRefused()
    {
        byte[] bytes = BuildNamedGraph(
            maxNodes: 4,
            nodes:
            [
                (1, default, 0, 0, -1, string.Empty),
                (1, default, 0, 0, -1, string.Empty),
            ],
            links: []);

        Assert.Throws<GraphFormatException>(() => GraphFile.Read(bytes));
    }

    /// <summary>An unknown field name is skipped by consuming exactly two tagged values.</summary>
    [Fact]
    public void AnUnknownFieldIsSkipped()
    {
        var w = new GraphStreamWriter();
        w.RawUInt32(GraphFile.Magic);
        w.RawFieldHash(GraphFieldHash.Of("nSomethingElse"));
        w.Byte(GraphValueType.Bool8, (byte)GraphValueType.Real64);
        w.Double(1.5);
        w.Field("nMaxNodes", GraphValueType.Int32, x => x.Int32(4));

        var graph = GraphFile.Read(w.ToArray());

        Assert.Equal(GraphFieldHash.Of("nSomethingElse"), Assert.Single(graph.UnknownFieldHashes));
        Assert.Equal(4, graph.MaxNodes);
        Assert.True(graph.IsExact);
    }

    /// <summary>
    /// The route table is indexed <c>from + to × maxNodes</c>, so the destination is the major axis. A
    /// transposed reader passes any test built on a symmetric table; this one is deliberately asymmetric.
    /// </summary>
    [Fact]
    public void TheRouteTableIsIndexedByDestinationMajor()
    {
        const int max = 4;
        byte[] bytes = new byte[GraphRouteTable.EntryStride * max * max];
        Fill(bytes, max);

        // Entry (from 1, to 2) sits at 1 + 2*4 = 9.
        Write(bytes, 9, 3, 100.0f);

        // Entry (from 2, to 1) sits at 2 + 1*4 = 6, and stays the sentinel.
        var table = new GraphRouteTable(bytes, max);

        Assert.True(table.TryGetStep(1, 2, out int next, out float cost));
        Assert.Equal(3, next);
        Assert.Equal(100.0f, cost);
        Assert.False(table.TryGetStep(2, 1, out _, out _));
    }

    /// <summary>A route is walked one table lookup at a time, and stops when it reaches the destination.</summary>
    [Fact]
    public void ARouteIsWalkedOneStepAtATime()
    {
        const int max = 5;
        byte[] bytes = new byte[GraphRouteTable.EntryStride * max * max];
        Fill(bytes, max);

        // Destination 4: from 1 step to 2, from 2 step to 3, from 3 step to 4.
        Write(bytes, 1 + (4 * max), 2, 30.0f);
        Write(bytes, 2 + (4 * max), 3, 20.0f);
        Write(bytes, 3 + (4 * max), 4, 10.0f);

        var table = new GraphRouteTable(bytes, max);

        Assert.Equal([2, 3, 4], table.EnumerateRoute(1, 4));
        Assert.Empty(table.EnumerateRoute(4, 4));
        Assert.Empty(table.EnumerateRoute(0, 4));
    }

    /// <summary>A route table that loops is bounded rather than hanging the caller.</summary>
    [Fact]
    public void ALoopingRouteTableTerminates()
    {
        const int max = 3;
        byte[] bytes = new byte[GraphRouteTable.EntryStride * max * max];
        Fill(bytes, max);

        // Destination 2: 0 -> 1 -> 0 -> ... and never arrives.
        Write(bytes, 0 + (2 * max), 1, 1.0f);
        Write(bytes, 1 + (2 * max), 0, 1.0f);

        var table = new GraphRouteTable(bytes, max);

        Assert.Empty(table.EnumerateRoute(0, 2));
    }

    /// <summary>A route table of the wrong size for its slot count is refused.</summary>
    [Fact]
    public void ARouteTableOfTheWrongSizeIsRefused() =>
        Assert.Throws<GraphFormatException>(() => new GraphRouteTable(new byte[8], 4));

    /// <summary>The criteria names resolve to the bits registration order gives them.</summary>
    [Theory]
    [InlineData("", GraphNodeCriteria.None)]
    [InlineData("NODECRITERIA_DOOR", GraphNodeCriteria.Door)]
    [InlineData("NODECRITERIA_VIEW", GraphNodeCriteria.View)]
    [InlineData("NODECRITERIA_STAIR", GraphNodeCriteria.Stair)]
    [InlineData("NODECRITERIA_SOMETHING", GraphNodeCriteria.None)]
    public void TheCriteriaNamesResolveToTheirBits(string name, GraphNodeCriteria expected) =>
        Assert.Equal(expected, GraphNode.ResolveCriteria(name));

    /// <summary>Only a view node is kept out of the nearest-node query.</summary>
    [Fact]
    public void OnlyAViewNodeIsExcludedFromTheNearestQuery()
    {
        Assert.True(Node("NODECRITERIA_VIEW").IsExcludedFromNearestQuery);
        Assert.False(Node("NODECRITERIA_DOOR").IsExcludedFromNearestQuery);
        Assert.False(Node("NODECRITERIA_STAIR").IsExcludedFromNearestQuery);
        Assert.False(Node(string.Empty).IsExcludedFromNearestQuery);

        static GraphNode Node(string criteria) =>
            new(1, default, 0, 1, -1, criteria, GraphNode.ResolveCriteria(criteria));
    }

    /// <summary>A positional file reads the same values in a fixed order, with the two optional fields.</summary>
    [Fact]
    public void APositionalFileReadsTheSameValues()
    {
        var w = new GraphStreamWriter();
        w.Int32(2);   // node count
        w.Int32(4);   // max nodes
        w.Int32(1);   // link count

        // A node with the optional extra float and a material.
        w.Int32(1);
        w.Vector3d(new Vector3d(1, 2, 3));
        w.Single(0.0f);
        w.Single(1.0f);
        w.Single(1.0f);          // the optional Real32
        w.Int32(6);              // the optional material
        w.String(string.Empty);
        w.Int32(1);
        w.Int32(10);
        w.Int32(11);
        w.Int32(0);

        // A node with neither optional field.
        w.Int32(2);
        w.Vector3d(new Vector3d(4, 5, 6));
        w.Single(0.5f);
        w.Single(2.0f);
        w.String("NODECRITERIA_STAIR");
        w.Int32(0);
        w.Int32(0);

        w.Int32(1);
        w.Int32(2);
        w.Int32(1);

        w.Blob(GraphValueType.Blob, new byte[8 * 4 * 4]);

        var graph = GraphFile.Read(w.ToArray());

        Assert.True(graph.IsPositional);
        Assert.True(graph.IsExact);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(new GraphLink(1, 2, 1), Assert.Single(graph.Links));
        Assert.NotNull(graph.RouteTable);

        Assert.Equal(1.0f, graph.PositionalExtras[0].ExtraValue);
        Assert.Equal((10, 11), Assert.Single(graph.PositionalExtras[0].FirstList));
        Assert.Empty(graph.PositionalExtras[0].SecondList);

        Assert.Null(graph.PositionalExtras[1].ExtraValue);
        Assert.Equal(GraphNodeCriteria.Stair, graph.NodeAt(2)!.Criteria);

        // 0x4FA160 never clears the resolved material, so a node without the field keeps the previous
        // node's. No retail node reaches this, and the behaviour is reproduced rather than corrected.
        Assert.Equal(6, graph.NodeAt(2)!.MaterialId);
    }

    private static void Fill(byte[] bytes, int max)
    {
        for (int i = 0; i < max * max; i++)
        {
            Write(bytes, i, GraphRouteTable.NoRoute, -1.0f);
        }
    }

    private static void Write(byte[] bytes, int index, int next, float cost)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * GraphRouteTable.EntryStride), next);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((index * GraphRouteTable.EntryStride) + 4), cost);
    }

    private static byte[] BuildNamedGraph(
        int maxNodes,
        (int Id, Vector3d Position, float Gamma, float Radius, int Material, string Criteria)[] nodes,
        (int A, int B, int Type)[] links)
    {
        var w = new GraphStreamWriter();
        w.RawUInt32(GraphFile.Magic);
        w.Field("nMaxNodes", GraphValueType.Int32, x => x.Int32(maxNodes));
        w.Field(
            "RouteTable",
            GraphValueType.Blob,
            x => x.Blob(GraphValueType.Blob, new byte[GraphRouteTable.EntryStride * maxNodes * maxNodes]));

        foreach ((int id, Vector3d position, float gamma, float radius, int material, string criteria) in nodes)
        {
            w.Field("nNodeID", GraphValueType.Int32, x => x.Int32(id));
            w.Field("tNodePosition", GraphValueType.Real64x3, x => x.Vector3d(position));
            w.Field("vNodeGamma", GraphValueType.Real32, x => x.Single(gamma));
            w.Field("vNodeRadius", GraphValueType.Real32, x => x.Single(radius));
            w.Field("nNodeMaterial", GraphValueType.Int32, x => x.Int32(material));
            w.Field("nNodeCriteria", GraphValueType.String8, x => x.String(criteria));
        }

        foreach ((int a, int b, int type) in links)
        {
            w.Field("nLinkID1", GraphValueType.Int32, x => x.Int32(a));
            w.Field("nLinkID2", GraphValueType.Int32, x => x.Int32(b));
            w.Field("eLinkType", GraphValueType.Int32, x => x.Int32(type));
        }

        return w.ToArray();
    }
}

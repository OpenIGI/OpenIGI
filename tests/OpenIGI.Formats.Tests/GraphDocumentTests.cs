using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Graph files edited as records: unchanged they save byte for byte, and a moved node is read back where it went.</summary>
public sealed class GraphDocumentTests
{
    /// <summary>Every named retail graph of both games saves back unchanged and agrees with the game's own reader.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_graphs_round_trip() => AssertRoundTrips(GameDataSource.Igi1);

    /// <summary>The same for I.G.I. 2.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_graphs_round_trip() => AssertRoundTrips(GameDataSource.Igi2);

    /// <summary>A moved node changes 24 bytes and nothing else, and the game's reader sees it moved.</summary>
    [RequiresTitleDataFact("igi1")]
    public void A_moved_node_is_where_it_was_put()
    {
        string path = GameDataSource.Igi1.EnumerateFiles("graph*.dat").First(p => GraphDocument.IsNamed(File.ReadAllBytes(p)) && GraphDocument.Read(File.ReadAllBytes(p)).Nodes.Count > 2);
        byte[] original = File.ReadAllBytes(path);
        GraphDocument document = GraphDocument.Read(original);
        GraphDocumentNode node = document.Nodes[1];
        Vector3d moved = new(node.Position.X + 4096, node.Position.Y, node.Position.Z - 2048);

        Assert.True(document.SetNodePosition(node.Id, moved));
        byte[] saved = document.Write();
        Assert.Equal(original.Length, saved.Length);
        Assert.InRange(original.Zip(saved).Count(static p => p.First != p.Second), 1, 24);

        GraphFile game = GraphFile.Read(saved);
        Assert.Equal(moved, game.Nodes.Single(n => n.Id == node.Id).Position);
    }

    /// <summary>
    /// A rebuilt table follows the nodes: on a ring, moving one node far away sends the route the other way round, and
    /// the game's reader walks the new route.
    /// </summary>
    [Fact]
    public void A_rebuilt_table_follows_moved_nodes()
    {
        GraphDocument document = GraphDocument.Read(Ring());
        Assert.True(document.HasRouteTable);
        Assert.Equal(4, document.MaxNodes);

        Assert.True(document.RebuildRoutes());
        Assert.Equal([1, 2], GraphFile.Read(document.Write()).RouteTable!.EnumerateRoute(0, 2));

        document.SetNodePosition(1, new Vector3d(30, 0, 0));
        Assert.True(document.RebuildRoutes());
        GraphRouteTable routes = GraphFile.Read(document.Write()).RouteTable!;
        Assert.Equal([3, 2], routes.EnumerateRoute(0, 2));
        Assert.True(routes.TryGetStep(0, 2, out _, out float cost));
        Assert.Equal(12f + MathF.Sqrt(104f), cost, 3);
        Assert.False(routes.TryGetStep(0, 0, out _, out _));
    }

    /// <summary>
    /// Rebuilt from their own nodes and links, the retail I.G.I. graphs reach exactly the pairs their tables reach, and
    /// take the same first step on all but the few links whose nodes were moved after the original bake.
    /// </summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_routes_rebuild()
    {
        long pairs = 0;
        long sameStep = 0;
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("graph*.dat"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!GraphDocument.IsNamed(bytes) || GraphFile.Read(bytes).RouteTable is not { } retail)
            {
                continue;
            }

            GraphDocument document = GraphDocument.Read(bytes);
            Assert.True(document.RebuildRoutes(), path);
            GraphRouteTable rebuilt = GraphFile.Read(document.Write()).RouteTable!;
            int[] ids = [.. document.Nodes.Select(static n => n.Id).Where(id => id < document.MaxNodes).Distinct()];
            foreach (int from in ids)
            {
                foreach (int to in ids)
                {
                    bool reached = retail.TryGetStep(from, to, out int retailStep, out _);
                    Assert.Equal(reached, rebuilt.TryGetStep(from, to, out int step, out _));
                    if (reached)
                    {
                        pairs++;
                        sameStep += retailStep == step ? 1 : 0;
                    }
                }
            }
        }

        Assert.True(pairs > 100_000, $"Only {pairs} routes were compared.");
        Assert.True(sameStep >= pairs * 0.99, $"{sameStep} of {pairs} routes take the retail first step.");
    }

    /// <summary>
    /// Links added and removed are what the game reads: a chord across the ring becomes the route, and taking a side away
    /// sends the route round the other way.
    /// </summary>
    [Fact]
    public void Links_added_and_removed_are_read_by_the_game()
    {
        GraphDocument document = GraphDocument.Read(Ring());
        Assert.False(document.AddLink(0, 1));
        Assert.False(document.AddLink(2, 2));
        Assert.False(document.AddLink(0, 9));

        Assert.True(document.AddLink(0, 2));
        Assert.True(document.AreLinked(2, 0));
        Assert.Equal(1, document.LinkTypes[^1]);
        Assert.True(document.RebuildRoutes());
        GraphFile game = GraphFile.Read(document.Write());
        Assert.Equal(5, game.Links.Count);
        Assert.Equal([2], game.RouteTable!.EnumerateRoute(0, 2));

        Assert.Equal(1, document.RemoveLink(2, 0));
        Assert.Equal(1, document.RemoveLink(2, 1));
        Assert.Null(document.RemoveLink(2, 1));
        Assert.True(document.RebuildRoutes());
        Assert.Equal([3, 2], GraphFile.Read(document.Write()).RouteTable!.EnumerateRoute(0, 2));
    }

    /// <summary>On every retail graph that has links, adding a link and removing it again gives back the file byte for byte.</summary>
    [RequiresTitleDataFact("igi1")]
    public void A_link_added_and_removed_leaves_the_file_as_it_was()
    {
        int seen = 0;
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("graph*.dat"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!GraphDocument.IsNamed(bytes) || GraphDocument.Read(bytes) is not { Nodes.Count: > 2, Links.Count: > 0 } document)
            {
                continue;
            }

            // A small graph can link every pair already; it has nothing to add.
            (int A, int B)[] free = [.. document.Nodes.SelectMany(n => document.Nodes.Select(m => (n.Id, m.Id)))
                .Where(p => p.Item1 != p.Item2 && !document.AreLinked(p.Item1, p.Item2)).Take(1)];
            if (free is not [(int a, int b)])
            {
                continue;
            }

            Assert.True(document.AddLink(a, b), path);
            Assert.Equal(document.Links.Count, GraphFile.Read(document.Write()).Links.Count);
            Assert.NotNull(document.RemoveLink(b, a));
            Assert.Equal(bytes, document.Write());
            seen++;
        }

        Assert.True(seen > 0);
    }

    // Four nodes on a ring, 0 (0,0), 1 (10,0), 2 (10,10), 3 (0,12): from 0 to 2 the way through 1 is shorter.
    private static byte[] Ring()
    {
        GraphStreamWriter w = new();
        w.RawUInt32(GraphFile.Magic);
        w.Field("nMaxNodes", GraphValueType.Int32, f => f.Int32(4));
        w.Field("RouteTable", GraphValueType.Blob, f => f.Blob(GraphValueType.Int8, new byte[GraphRouteTable.EntryStride * 16]));
        Vector3d[] at = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 12, 0)];
        for (int id = 0; id < at.Length; id++)
        {
            w.Field("nNodeID", GraphValueType.Int32, f => f.Int32(id));
            w.Field("tNodePosition", GraphValueType.Real64x3, f => f.Vector3d(at[id]));
            w.Field("vNodeGamma", GraphValueType.Real32, f => f.Single(0f));
            w.Field("vNodeRadius", GraphValueType.Real32, f => f.Single(1f));
            w.Field("nNodeMaterial", GraphValueType.Int32, f => f.Int32(-1));
            w.Field("nNodeCriteria", GraphValueType.String8, f => f.String(string.Empty));
        }

        foreach ((int a, int b) in new[] { (0, 1), (1, 2), (2, 3), (3, 0) })
        {
            w.Field("nLinkID1", GraphValueType.Int32, f => f.Int32(a));
            w.Field("nLinkID2", GraphValueType.Int32, f => f.Int32(b));
            w.Field("eLinkType", GraphValueType.Int32, f => f.Int32(1));
        }

        return w.ToArray();
    }

    private static void AssertRoundTrips(GameDataSource source)
    {
        int named = 0;
        foreach (string path in source.EnumerateFiles("graph*.dat"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!GraphDocument.IsNamed(bytes))
            {
                continue;
            }

            named++;
            GraphDocument document = GraphDocument.Read(bytes);
            Assert.True(bytes.AsSpan().SequenceEqual(document.Write()), $"{path} changed on a save with no edit.");

            GraphFile game = GraphFile.Read(bytes);
            Assert.Equal(game.Links.Count, document.Links.Count);
            // The document lists every node record; the game keeps some and discards the rest (I.G.I. 2 drops many).
            Assert.Equal(game.Nodes.Count + game.DiscardedNodeIds.Count, document.Nodes.Count);
        }

        Assert.True(named > 0, "No named graph file was found.");
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Covers the nearest-node query — the radius bias, the view-node exclusion and the two success codes.</summary>
public sealed class GraphNearestNodeTests
{
    /// <summary>An empty graph finds nothing.</summary>
    [Fact]
    public void AnEmptyGraphFindsNothing()
    {
        GraphFile graph = Build([]);

        Assert.Equal(GraphNearestNodeResult.None, GraphNearestNode.Find(graph, default, out GraphNode? best, out _));
        Assert.Null(best);
    }

    /// <summary>The nearest node wins, and the target being outside its radius gives result 1.</summary>
    [Fact]
    public void TheNearestNodeWinsAndOutsideGivesOne()
    {
        GraphFile graph = Build(
        [
            (1, new Vector3d(100_000, 0, 0), 1.0f, string.Empty),
            (2, new Vector3d(10_000, 0, 0), 1.0f, string.Empty),
        ]);

        var result = GraphNearestNode.Find(graph, default, out GraphNode? best, out _);

        Assert.Equal(GraphNearestNodeResult.Outside, result);
        Assert.Equal(2, best!.Id);
    }

    /// <summary>
    /// <b>Inside the radius is a different answer, not the same one.</b> The score goes negative and the query
    /// returns 2 — which is how a caller tells "the nearest node is over there" from "you are standing in it".
    /// </summary>
    [Fact]
    public void InsideTheRadiusGivesTwo()
    {
        // A node 2 m away with a 3 m radius: the score is negative.
        GraphFile graph = Build([(1, new Vector3d(2 * 4096.0, 0, 0), 3.0f, string.Empty)]);

        var result = GraphNearestNode.Find(graph, default, out GraphNode? best, out _);

        Assert.Equal(GraphNearestNodeResult.Inside, result);
        Assert.Equal(1, best!.Id);
    }

    /// <summary>
    /// <b>A node's radius pulls it closer in the ranking.</b> A sloppy far node beats a precise near one, which
    /// is the whole reason the radius is in metres rather than units.
    /// </summary>
    [Fact]
    public void ALargeRadiusBeatsABetterDistance()
    {
        GraphFile graph = Build(
        [
            // 10 m away, no radius: score 40960.
            (1, new Vector3d(10 * 4096.0, 0, 0), 0.0f, string.Empty),
            // 20 m away but a 15 m radius: score 20480. Further, and it still wins.
            (2, new Vector3d(20 * 4096.0, 0, 0), 15.0f, string.Empty),
        ]);

        GraphNearestNode.Find(graph, default, out GraphNode? best, out _);

        Assert.Equal(2, best!.Id);
    }

    /// <summary>A view node is skipped however near it is — an observation post is never the answer.</summary>
    [Fact]
    public void AViewNodeIsNeverChosen()
    {
        GraphFile graph = Build(
        [
            (1, new Vector3d(1000, 0, 0), 0.0f, "NODECRITERIA_VIEW"),
            (2, new Vector3d(100_000, 0, 0), 0.0f, string.Empty),
        ]);

        GraphNearestNode.Find(graph, default, out GraphNode? best, out _);

        Assert.Equal(2, best!.Id);

        // Door and stair nodes are not excluded.
        GraphFile doors = Build([(1, new Vector3d(1000, 0, 0), 0.0f, "NODECRITERIA_DOOR")]);
        GraphNearestNode.Find(doors, default, out GraphNode? door, out _);
        Assert.Equal(1, door!.Id);
    }

    /// <summary>A graph of only view nodes finds nothing at all.</summary>
    [Fact]
    public void AGraphOfOnlyViewNodesFindsNothing()
    {
        GraphFile graph = Build([(1, new Vector3d(1000, 0, 0), 0.0f, "NODECRITERIA_VIEW")]);

        Assert.Equal(GraphNearestNodeResult.None, GraphNearestNode.Find(graph, default, out _, out _));
    }

    /// <summary>The engine ranks three and returns one; the other two are computed and thrown away.</summary>
    [Fact]
    public void ThreeAreRankedAndTheFourthIsDropped()
    {
        GraphFile graph = Build(
        [
            (1, new Vector3d(40_000, 0, 0), 0.0f, string.Empty),
            (2, new Vector3d(10_000, 0, 0), 0.0f, string.Empty),
            (3, new Vector3d(30_000, 0, 0), 0.0f, string.Empty),
            (4, new Vector3d(20_000, 0, 0), 0.0f, string.Empty),
        ]);

        GraphNearestNode.Find(graph, default, out GraphNode? best, out IReadOnlyList<GraphNode?> ranked);

        Assert.Equal(2, best!.Id);
        Assert.Equal(GraphNearestNode.CandidateCount, ranked.Count);
        Assert.Equal([2, 4, 3], ranked.Select(n => n!.Id));
    }

    /// <summary>Over retail data every graph with a node answers, and never with a view node.</summary>
    [RequiresGameDataFact]
    public void EveryRetailGraphAnswersWithAWalkableNode()
    {
        int answered = 0;
        int empty = 0;

        foreach (string path in GameData.EnumerateFiles("graph*.dat"))
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));

            // Aim at the median node so the query has something plausible to find.
            if (graph.Nodes.Count == 0)
            {
                empty++;
                continue;
            }

            Vector3d target = graph.Nodes[graph.Nodes.Count / 2].Position;
            var result = GraphNearestNode.Find(graph, target, out GraphNode? best, out _);

            if (graph.Nodes.All(n => n.IsExcludedFromNearestQuery))
            {
                Assert.Equal(GraphNearestNodeResult.None, result);
                continue;
            }

            Assert.NotEqual(GraphNearestNodeResult.None, result);
            Assert.NotNull(best);
            Assert.False(best.IsExcludedFromNearestQuery);
            answered++;
        }

        Assert.Equal(0, empty);
        Assert.Equal(300, answered);
    }

    private static GraphFile Build((int Id, Vector3d Position, float Radius, string Criteria)[] nodes)
    {
        var w = new GraphStreamWriter();
        w.RawUInt32(GraphFile.Magic);
        w.Field("nMaxNodes", GraphValueType.Int32, x => x.Int32(16));
        w.Field(
            "RouteTable",
            GraphValueType.Blob,
            x => x.Blob(GraphValueType.Blob, new byte[GraphRouteTable.EntryStride * 16 * 16]));

        foreach ((int id, Vector3d position, float radius, string criteria) in nodes)
        {
            w.Field("nNodeID", GraphValueType.Int32, x => x.Int32(id));
            w.Field("tNodePosition", GraphValueType.Real64x3, x => x.Vector3d(position));
            w.Field("vNodeGamma", GraphValueType.Real32, x => x.Single(0));
            w.Field("vNodeRadius", GraphValueType.Real32, x => x.Single(radius));
            w.Field("nNodeMaterial", GraphValueType.Int32, x => x.Int32(-1));
            w.Field("nNodeCriteria", GraphValueType.String8, x => x.String(criteria));
        }

        return GraphFile.Read(w.ToArray());
    }
}

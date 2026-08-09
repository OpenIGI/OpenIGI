using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The node occupancy claims of the free-position search — <c>sub_452C60</c>'s records and
/// <c>0x48B780</c>'s sub-slot grid, which are what spread a squad over the graph instead of stacking
/// every guard on the same node.
/// </summary>
public sealed class AiNodeOccupancyTests
{
    [RequiresGameDataFact]
    public void ClaimantsSpreadInsteadOfStacking()
    {
        GraphFile graph = Graph();
        AiNodeOccupancy occupancy = new();
        int myNode = graph.Nodes[0].Id;
        int target = graph.Nodes[10].Id;

        object first = new();
        object second = new();
        Vector3d? firstSlot = null;
        Vector3d? secondSlot = null;

        int? a = AiFreePosition.Find(
            graph, myNode, target, towardTarget: false,
            occupancy: occupancy, claimant: first, claimedPosition: p => firstSlot = p);
        int? b = AiFreePosition.Find(
            graph, myNode, target, towardTarget: false,
            occupancy: occupancy, claimant: second, claimedPosition: p => secondSlot = p);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(firstSlot);
        Assert.NotNull(secondSlot);

        // Same node means the grid gave them distinct sub-slots; a small node's zero-capacity record
        // pushes the second claimant to a neighbour instead. Either way they do not stack.
        if (a == b)
        {
            Assert.NotEqual(firstSlot!.Value, secondSlot!.Value);
        }
        else
        {
            Assert.NotEqual(a, b);
        }

        // Releasing the first claim frees its node again, exactly as the combat action's end does.
        occupancy.Release(first);
        object third = new();
        int? c = AiFreePosition.Find(
            graph, myNode, target, towardTarget: false,
            occupancy: occupancy, claimant: third, claimedPosition: _ => { });
        Assert.Equal(a, c);
    }

    private static GraphFile Graph()
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        return GraphFile.Read(File.ReadAllBytes(path));
    }
}

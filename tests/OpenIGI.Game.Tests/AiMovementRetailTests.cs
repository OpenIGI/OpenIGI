using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>How a soldier turns and walks, as <c>0x4610C0</c>, <c>0x4518E0</c> and <c>0x4520C0</c> define it.</summary>
public sealed class AiMovementRetailTests
{
    [Fact]
    public void ATurnTakesHalfOnItsFirstTickAndAllOnTheNext()
    {
        // With the clamp at pi the input is the whole error over pi; the unzoomed body averages it with the
        // last tick's, so a 90-degree turn from rest takes 45 degrees, then 90.
        HumanBodyTurn turn = new();

        Assert.Equal(Math.PI / 4.0, turn.Step(0.5f), 6);
        Assert.Equal(Math.PI / 2.0, turn.Step(0.5f), 6);
    }

    [Fact]
    public void ALookAtNodeTurnsAtTheClampedRate()
    {
        // 0x452580 clamps the angle to 0.025925925 rad: s = 0.0082525, below the 0.06 threshold, so each
        // steady tick turns s * pi / (2 - 16.67 s) = 0.0139203 rad, about 24 degrees a second.
        HumanBodyTurn turn = new();
        float input = 0.025925925f / MathF.PI;

        double first = turn.Step(input);
        double steady = turn.Step(input);

        Assert.Equal(0.0139203 / 2.0, first, 3);
        Assert.Equal(0.0139203, steady, 6);
    }

    [Fact]
    public void ASmallErrorHalvesEachTick()
    {
        HumanBodyTurn turn = new();
        turn.Step(0.01f / MathF.PI);

        double second = turn.Step(0.01f / MathF.PI);

        Assert.Equal(0.01 / (2.0 - (0.01 / Math.PI * 16.666666666666668)), second, 6);
    }

    [RequiresGameDataFact]
    public void AWalkFromInsideANodeLeavesByTheEdgeOfItsSquare()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));

        // A hop whose far node lies beyond the near one's square on some axis, so 0x4520C0 finds an edge.
        (GraphNode From, GraphNode To)? found = null;
        foreach (GraphNode from in graph.Nodes)
        {
            foreach (GraphNode to in graph.Nodes)
            {
                double half = from.Radius * GraphNode.MetreInUnits;
                if (from.Id != to.Id
                    && graph.RouteTable!.EnumerateRoute(from.Id, to.Id) is { Count: > 1 } route
                    && graph.NodeAt(route[0]) is { } next
                    && (Math.Abs(next.Position.X - from.Position.X) > half
                        || Math.Abs(next.Position.Y - from.Position.Y) > half))
                {
                    found = (from, to);
                    break;
                }
            }

            if (found is not null)
            {
                break;
            }
        }

        Assert.NotNull(found);
        (GraphNode From, GraphNode To) hop = found.Value;
        AiSoldier soldier = new(graph, hop.From.Position, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.GoTo(hop.To.Id));

        Vector3d first = soldier.DebugWaypoint!.Value;
        double edge = hop.From.Radius * GraphNode.MetreInUnits;
        double reach = Math.Max(
            Math.Abs(first.X - hop.From.Position.X),
            Math.Abs(first.Y - hop.From.Position.Y));
        Assert.Equal(edge, reach, 3);
        Assert.Equal(hop.From.Position.Z, first.Z);
    }
}

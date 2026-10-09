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
    public void TheBodyTakesALargeTurnWholeOnItsFirstTick()
    {
        // With the clamp at pi the input is the whole error over pi, and 0x4610C0's averaging never applies
        // (its tangent is compared unrounded): probed, patrol 411 turned 62 degrees in one tick.
        HumanBodyTurn turn = new(averages: false);

        Assert.Equal(Math.PI / 2.0, turn.Step(0.5f), 6);
        Assert.Equal(62.0 * Math.PI / 180.0, turn.Step(62f / 180f), 5);
    }

    [Fact]
    public void TheViewPitchTakesHalfOnItsFirstTickAndAllOnTheNext()
    {
        // 0x464800 rounds tan(pi/4) to the float 1.0 an unzoomed view's scale is, so the pitch does average.
        HumanBodyTurn turn = new(averages: true);

        Assert.Equal(Math.PI / 4.0, turn.Step(0.5f), 6);
        Assert.Equal(Math.PI / 2.0, turn.Step(0.5f), 6);
    }

    [Fact]
    public void AnEightAndAHalfDegreeErrorTurnsSevenOnTheFirstTick()
    {
        // Probed: 411's heading went from 7.82 to 0.74 degrees toward a node 8.5 degrees round, 8.5 / (2 - 16.67 s).
        HumanBodyTurn turn = new(averages: false);
        float input = 8.5f / 180f;

        Assert.Equal(8.5 / (2.0 - (input * 16.666666666666668)), turn.Step(input) * 180.0 / Math.PI, 3);
    }

    [Fact]
    public void ALookAtNodeTurnsAtTheClampedRate()
    {
        // 0x452580 clamps the angle to 0.025925925 rad: s = 0.0082525, below the 0.06 threshold, so each
        // tick turns s * pi / (2 - 16.67 s) = 0.0139203 rad, about 24 degrees a second, from the first.
        HumanBodyTurn turn = new(averages: false);
        float input = 0.025925925f / MathF.PI;

        Assert.Equal(0.0139203, turn.Step(input), 6);
        Assert.Equal(0.0139203, turn.Step(input), 6);
    }

    [Fact]
    public void ASmallErrorHalvesEachTick()
    {
        HumanBodyTurn turn = new(averages: false);

        Assert.Equal(0.01 / (2.0 - (0.01 / Math.PI * 16.666666666666668)), turn.Step(0.01f / MathF.PI), 6);
    }

    [Theory]
    [InlineData(100.0, 0.0, 100.0, 0.0)]
    [InlineData(0.0, -100.0, 0.0, -100.0)]
    public void TheClipsWholeDeltaMovesTheBodyEveryTick(double lateral, double forward, double dx, double dy)
    {
        // 0x45D080 rotates the clip's delta by the body and 0x4614B0 applies it, whatever the AI asked for:
        // a roll carries a soldier sideways, a step back steps back.
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d far = new(0, -500 * 4096.0, 0);

        soldier.Tick(1, far, rootMotionThisTick: forward, enemyAlive: false, rootMotionLateral: lateral);

        Assert.Equal(dx, soldier.Position.X, 3);
        Assert.Equal(dy, soldier.Position.Y, 3);
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

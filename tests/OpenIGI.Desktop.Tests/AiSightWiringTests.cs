using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks the AI's sight request to physical world geometry and retail's contact rule.</summary>
public sealed class AiSightWiringTests
{
    private static readonly Vector3d Eye = Vector3d.Zero;
    private static readonly Vector3d Mark = new(0.0, 10_000.0, 0.0);

    [Fact]
    public void AiSightDoesNotTurnAuthoredDoorwayAndWindowPortalsIntoSolidWalls()
    {
        WorldTraceRequest captured = default;
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            captured = request;
            hit = default;
            return false;
        };

        Assert.True(SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.SightObstructionTolerance));

        Assert.Equal(WorldTraceMask.World, captured.Mask);
        Assert.False(captured.TracePortals);
    }

    [Theory]
    [InlineData(WorldTraceHitKind.StaticObject, 8_200.0, false)]
    [InlineData(WorldTraceHitKind.StaticObject, 7_000.0, false)]
    [InlineData(WorldTraceHitKind.Terrain, 9_500.0, false)]
    [InlineData(WorldTraceHitKind.DynamicObject, 8_000.0, true)]
    [InlineData(WorldTraceHitKind.DynamicObject, 7_000.0, false)]
    public void PlacedGeometryHidesTheTargetWhereverItIsMet(WorldTraceHitKind kind, double distance, bool seen)
    {
        // 0x489B20 accepts a returned fraction of 0.79 or more, but for terrain and placed geometry the retail
        // casts only ever return 0.0 when blocked -- measured with the probe, including a static roof met 82%
        // along. Only a moving object is still given the tolerance.
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = new WorldTraceHit(default, default, distance, distance / 10_000.0, Material: 0, kind);
            return true;
        };

        Assert.Equal(seen, SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.SightObstructionTolerance));
    }
}

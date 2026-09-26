using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;

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

        Assert.True(SoldierRenderer.HasLineOfSight(trace, Eye, Mark, AiSoldier.SightObstructionTolerance));

        Assert.Equal(WorldTraceMask.World, captured.Mask);
        Assert.False(captured.TracePortals);
    }

    [Theory]
    [InlineData(WorldTraceHitKind.StaticObject, 8_000.0, true)]
    [InlineData(WorldTraceHitKind.StaticObject, 7_000.0, false)]
    [InlineData(WorldTraceHitKind.Terrain, 9_500.0, false)]
    public void AnObjectHidesTheTargetOnlyNearerThanRetailsTolerance(WorldTraceHitKind kind, double distance, bool seen)
    {
        // 0x489B20: visible when 0x4CE410 reports no contact or a contact fraction of at least 0.79; a terrain
        // contact is forced to fraction zero, so it always hides.
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = new WorldTraceHit(default, default, distance, distance / 10_000.0, Material: 0, kind);
            return true;
        };

        Assert.Equal(seen, SoldierRenderer.HasLineOfSight(trace, Eye, Mark, AiSoldier.SightObstructionTolerance));
    }
}

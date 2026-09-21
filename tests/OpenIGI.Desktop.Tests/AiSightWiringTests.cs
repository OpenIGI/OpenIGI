using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Presentation.Igi1;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks the desktop's AI sight request to physical world geometry.</summary>
public sealed class AiSightWiringTests
{
    [Fact]
    public void AiSightDoesNotTurnAuthoredDoorwayAndWindowPortalsIntoSolidWalls()
    {
        WorldTraceRequest captured = default;
        bool called = false;
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            captured = request;
            called = true;
            hit = default;
            return false;
        };

        WorldRayCast sight = SoldierRenderer.AiSightCast(trace);
        Assert.False(sight(Vector3d.Zero, new Vector3d(0.0, 4096.0, 0.0), out _));

        Assert.True(called);
        Assert.Equal(WorldTraceMask.World, captured.Mask);
        Assert.False(captured.TracePortals);
    }
}

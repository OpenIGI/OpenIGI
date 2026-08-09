using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The ordered rich trace shared by AI sight and projectiles.</summary>
public sealed class WorldTraceTests
{
    [Fact]
    public void NearestProviderWinsAndCarriesItsIdentity()
    {
        WorldTraceCast far = Contact(80.0, WorldTraceHitKind.Terrain, "terrain");
        WorldTraceCast near = Contact(25.0, WorldTraceHitKind.StaticObject, "building_12");
        WorldTraceCast trace = WorldTrace.Nearest(far, near);

        WorldTraceRequest request = new(default, new Vector3d(100.0, 0.0, 0.0));
        Assert.True(trace(request, out WorldTraceHit hit));
        Assert.Equal(25.0, hit.Distance);
        Assert.Equal(0.25, hit.Fraction);
        Assert.Equal(WorldTraceHitKind.StaticObject, hit.Kind);
        Assert.Equal("building_12", hit.Label);
    }

    [Fact]
    public void LegacyAdapterHonoursTheRequestedMask()
    {
        WorldRayCast legacy = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = new WorldRayHit(new Vector3d(5.0, 0.0, 0.0), new Vector3f(-1f, 0f, 0f), 5.0, 7);
            return true;
        };
        WorldTraceCast trace = WorldTrace.FromRayCast(legacy, WorldTraceHitKind.StaticObject);

        Assert.False(trace(
            new WorldTraceRequest(default, new Vector3d(10.0, 0.0, 0.0), WorldTraceMask.Terrain),
            out _));
        Assert.True(trace(
            new WorldTraceRequest(default, new Vector3d(10.0, 0.0, 0.0), WorldTraceMask.StaticObjects),
            out WorldTraceHit hit));
        Assert.Equal(7, hit.Material);
        Assert.Equal(0.5, hit.Fraction);
    }

    [RequiresGameDataFact]
    public void TerrainTraceMeetsTheSameRetailSurfaceAsTheGroundQuery()
    {
        (TerrainData terrain, double x, double y, double z) = RetailLevelOneGround.Find();
        Vector3d from = new(x, y, z + 8192.0);
        Vector3d to = new(x, y, z - 8192.0);

        Assert.True(TerrainRayTrace.TryTrace(
            terrain,
            new WorldTraceRequest(from, to, WorldTraceMask.Terrain),
            out WorldTraceHit hit));
        Assert.Equal(WorldTraceHitKind.Terrain, hit.Kind);
        Assert.Equal(z - 1.0, hit.Position.Z, 1.0);
        Assert.InRange(hit.Fraction, 0.0, 1.0);
    }

    private static WorldTraceCast Contact(double distance, WorldTraceHitKind kind, string label) =>
        (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            double length = request.Length;
            double fraction = distance / length;
            hit = new WorldTraceHit(
                new Vector3d(distance, 0.0, 0.0),
                new Vector3f(-1f, 0f, 0f),
                distance,
                fraction,
                0,
                kind,
                Label: label);
            return true;
        };
}

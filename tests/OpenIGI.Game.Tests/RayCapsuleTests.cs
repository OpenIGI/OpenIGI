using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Unit tests for <see cref="RayCapsule"/>, the analytic soldier body test.</summary>
public sealed class RayCapsuleTests
{
    // A standing human: foot at z=0, top of head at z=1.8, radius 0.55 (the HumanSeparation shape).
    private static readonly Vector3d Foot = new(0.0, 0.0, 0.0);
    private static readonly Vector3d Head = new(0.0, 0.0, 1.8);
    private const double Radius = 0.55;

    [Fact]
    public void HorizontalRayThroughChestHitsSide()
    {
        // Ray runs along -X at chest height, aimed at the axis.
        Vector3d origin = new(2.0, 0.0, 0.9);
        Vector3d end = new(-0.01, 0.0, 0.9);
        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out double t, out Vector3d normal);

        Assert.True(hit);
        double expected = (2.0 - Radius) / 2.01; // x falls from 2 to 0.55 over a 2.01-unit direction.
        Assert.Equal(expected, t, 4);
        Assert.Equal(1.0, normal.X, 3); // radial, facing back along the ray travel
        Assert.Equal(0.0, normal.Y, 3);
        Assert.Equal(0.0, normal.Z, 3); // horizontal => a wall for HumanWallSweep
    }

    [Fact]
    public void EntryPointIsNearestNotExit()
    {
        // Ray passes straight through the middle: entry at x=-0.55 is nearer than exit at x=+0.55.
        Vector3d origin = new(-3.0, 0.0, 0.9);
        Vector3d end = new(3.0, 0.0, 0.9);
        Vector3d direction = new(6.0, 0.0, 0.0);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out double t, out _);

        Assert.True(hit);
        Assert.Equal((3.0 - Radius) / 6.0, t, 4); // entry at the near face, x = -0.55.
    }

    [Fact]
    public void RayThatPassesBeyondNeverHits()
    {
        Vector3d origin = new(5.0, 0.0, 0.9);
        Vector3d end = new(2.0, 0.0, 0.9); // stops 2 units from origin, still 1.45 from the axis.
        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void VerticalRayUpThroughAxisHitsBottomCap()
    {
        Vector3d origin = new(0.0, 0.0, -1.0);
        Vector3d end = new(0.0, 0.0, 0.1);
        Vector3d direction = new(0.0, 0.0, 1.1);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out double t, out Vector3d normal);

        Assert.True(hit);
        // Enters the bottom hemisphere (centre at foot) at z = -radius.
        Assert.Equal((1.0 - Radius) / 1.1, t, 4);
        Assert.Equal(0.0, normal.X, 3);
        Assert.Equal(0.0, normal.Y, 3);
        Assert.Equal(-1.0, normal.Z, 3); // downward
    }

    [Fact]
    public void VerticalRayDownThroughAxisHitsTopCap()
    {
        Vector3d origin = new(0.0, 0.0, 3.0);
        Vector3d end = new(0.0, 0.0, 2.0);
        Vector3d direction = new(0.0, 0.0, -1.0);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out double t, out Vector3d normal);

        Assert.True(hit);
        // Top hemisphere centred at head (1.8), surface at 1.8 + radius.
        Assert.Equal(3.0 - (Head.Z + Radius), t, 4);
        Assert.Equal(0.0, normal.X, 3);
        Assert.Equal(0.0, normal.Y, 3);
        Assert.Equal(1.0, normal.Z, 3); // upward
    }

    [Fact]
    public void VerticalRayBesideAxisMisses()
    {
        Vector3d origin = new(1.0, 0.0, 0.9); // 1 unit lateral, > radius.
        Vector3d end = new(1.0, 0.0, 2.5);
        Vector3d direction = new(0.0, 0.0, 1.6);

        bool hit = RayCapsule.Intersect(origin, direction, Foot, Head, Radius, out _, out _);

        Assert.False(hit);
    }
}

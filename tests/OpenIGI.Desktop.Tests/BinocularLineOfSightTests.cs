using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The binocular detector's line-of-sight probe (<see cref="SoldierRenderer.HasLineOfSight"/>) must
/// step past see-through surfaces so a chain-link fence does not hide the solid wall behind it.
/// </summary>
public sealed class BinocularLineOfSightTests
{
    private static readonly Vector3d Eye = new(0d, 0d, 0d);
    private static readonly Vector3d Target = new(1000d, 0d, 0d);

    private const int Glass = 14;
    private const int Fence = 18;
    private const int Concrete = 11;

    // A cast that reports the nearest surface strictly between the current origin and the target.
    private static WorldRayCast Surfaces(params (double Distance, int Material)[] surfaces) =>
        (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            double originX = origin.X;
            double best = double.PositiveInfinity;
            bool found = false;
            foreach ((double distance, int material) in surfaces)
            {
                double ahead = distance - originX;
                if (ahead <= 0d || distance >= end.X || ahead >= best)
                {
                    continue;
                }

                best = ahead;
                hit = new WorldRayHit(new Vector3d(distance, 0d, 0d), new Vector3f(-1f, 0f, 0f), ahead, material);
                found = true;
            }

            return found;
        };

    [Fact]
    public void ClearRayReachesTheTarget()
    {
        Assert.True(SoldierRenderer.HasLineOfSight(Surfaces(), Eye, Target));
    }

    [Fact]
    public void SolidWallBlocksTheView()
    {
        Assert.False(SoldierRenderer.HasLineOfSight(Surfaces((400d, Concrete)), Eye, Target));
    }

    [Fact]
    public void GuardBehindAFenceStaysVisible()
    {
        Assert.True(SoldierRenderer.HasLineOfSight(Surfaces((300d, Fence)), Eye, Target));
    }

    [Fact]
    public void FenceInFrontOfAWallStillBlocks()
    {
        // The nearest hit is the see-through fence; the probe must step past it and find the concrete.
        Assert.False(SoldierRenderer.HasLineOfSight(Surfaces((300d, Fence), (500d, Concrete)), Eye, Target));
    }

    [Fact]
    public void StackedGlassAndFenceStayVisible()
    {
        Assert.True(SoldierRenderer.HasLineOfSight(Surfaces((200d, Glass), (350d, Fence)), Eye, Target));
    }
}

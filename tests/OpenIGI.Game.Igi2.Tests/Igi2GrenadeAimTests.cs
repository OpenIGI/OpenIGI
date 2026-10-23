using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A soldier's grenade flight, <c>TraceTrajectory</c> as <c>HumanAI_GetGrenadeOrientationAndSpeedToTarget</c> follows it.</summary>
public sealed class Igi2GrenadeAimTests
{
    /// <summary>
    /// Ten metres across open ground at forty-five degrees is seven metres a second (<c>v² = g·d² / 2d</c>), 952 units a tick
    /// split between the run and the rise; it gets there, and the flight is the ticks it took.
    /// </summary>
    [Fact]
    public void A_throw_across_open_ground_lands_on_the_target()
    {
        Vector3d from = default;
        Vector3d target = new(40960, 0, 0);
        float launch = (float)(Math.Sqrt(9.8 * 10 * 10 / (2 * 10)) * Igi2HumanAi.GrenadeSpeedScale);
        Vector3f speed = new(launch * 1f, 0f, launch * (float)Math.Tan(Igi2HumanAi.GrenadeThrowAngles[1]));

        Assert.True(Igi2HumanAi.TryFlyGrenade(new Walls(), from, speed, target, out int flyTime));
        Assert.InRange(flyTime, 30, 70);
    }

    /// <summary>A wall in the way stops the flight: the throw does not get there.</summary>
    [Fact]
    public void A_wall_in_the_way_stops_the_throw()
    {
        Vector3d target = new(40960, 0, 0);
        float launch = (float)(Math.Sqrt(9.8 * 10 * 10 / (2 * 10)) * Igi2HumanAi.GrenadeSpeedScale);
        Vector3f speed = new(launch, 0f, launch);

        Assert.False(Igi2HumanAi.TryFlyGrenade(new Walls(atX: 20480), default, speed, target, out _));
    }

    // Nothing, or a wall across the X axis at one place, as tall as any throw.
    private sealed class Walls(double? atX = null) : IIgi2CollisionQuery
    {
        public void NextZ(WorldLineTrace trace, ReadOnlySpan<double> distances, uint groups)
        {
        }

        public void TraceLines(WorldLineTrace trace, uint groups)
        {
            for (int i = 0; i < trace.Count; i++)
            {
                trace.Intersections[i].IsHit = atX is { } x && (trace.Starts[i].X - x) * (trace.Ends[i].X - x) <= 0;
            }
        }
    }
}

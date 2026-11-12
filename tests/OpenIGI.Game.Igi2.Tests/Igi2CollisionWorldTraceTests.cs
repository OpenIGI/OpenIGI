using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The world answers the other game's world trace: what it meets, as what kind, and what it passes through.</summary>
public sealed class Igi2CollisionWorldTraceTests
{
    private static readonly Vector3d From = new(0, 0, 0);
    private static readonly Vector3d To = new(10_000, 0, 0);

    [Fact]
    public void An_object_is_met_as_a_static_object_with_its_owner_and_the_fraction_along_the_line()
    {
        GameTask owner = new PlainTask();
        Igi2CollisionWorld world = World(new Wall(4_000, owner, group: 0x4, material: 7));

        Assert.True(world.Trace(new WorldTraceRequest(From, To), out WorldTraceHit hit));
        Assert.Equal(WorldTraceHitKind.StaticObject, hit.Kind);
        Assert.Same(owner, hit.Owner);
        Assert.Equal(4_000, hit.Distance, 6);
        Assert.Equal(0.4, hit.Fraction, 6);
        Assert.Equal(7, hit.Material);
    }

    [Fact]
    public void A_human_is_passed_through_unless_humans_are_asked_for()
    {
        Igi2CollisionWorld world = World(
            new Wall(2_000, new PlainTask(), Igi2CollisionWorld.HumanGroup, material: 1),
            new Wall(6_000, new PlainTask(), group: 0x4, material: 2));

        Assert.True(world.Trace(new WorldTraceRequest(From, To), out WorldTraceHit blocked));
        Assert.Equal(WorldTraceHitKind.StaticObject, blocked.Kind);
        Assert.Equal(6_000, blocked.Distance, 0);

        Assert.True(world.Trace(new WorldTraceRequest(From, To, WorldTraceMask.All), out WorldTraceHit human));
        Assert.Equal(WorldTraceHitKind.Human, human.Kind);
        Assert.Equal(2_000, human.Distance, 6);
    }

    [Fact]
    public void An_ignored_owner_is_passed_through_and_a_line_past_everything_is_clear()
    {
        GameTask ignored = new PlainTask();
        Igi2CollisionWorld world = World(new Wall(3_000, ignored, group: 0x4, material: 1));

        Assert.False(world.Trace(new WorldTraceRequest(From, To, IgnoredOwner: ignored), out _));
        Assert.False(world.Trace(new WorldTraceRequest(From, To, WorldTraceMask.Terrain), out _));
    }

    private static Igi2CollisionWorld World(params IWorldLineCollider[] colliders)
    {
        Igi2CollisionWorld world = new([], null);
        foreach (IWorldLineCollider collider in colliders)
        {
            world.AddCollider(collider);
        }

        return world;
    }

    private sealed class PlainTask : GameTask;

    // A wall across the line at a distance along X: every line from the origin out along X meets it there.
    private sealed class Wall(double x, object owner, uint group, int material) : IWorldLineCollider
    {
        public object Owner => owner;

        public uint Group => group;

        public void NextZ(WorldLineTrace trace)
        {
        }

        public void TraceLines(WorldLineTrace trace)
        {
            for (int i = 0; i < trace.Count; i++)
            {
                Vector3d start = trace.Starts[i];
                Vector3d end = trace.Ends[i];
                if (start.X >= x || end.X < x)
                {
                    continue;
                }

                double distance = x - start.X;
                WorldIntersection hit = trace.Intersections[i];
                if (hit.IsHit && hit.Distance <= distance)
                {
                    continue;
                }

                hit.IsHit = true;
                hit.Distance = distance;
                hit.Position = new Vector3d(x, start.Y, start.Z);
                hit.Normal = new Vector3f(-1f, 0f, 0f);
                hit.HitObject = this;
                hit.EnterMaterial = material;
                hit.LeaveMaterial = null;
            }
        }

        public void LineOfSight(WorldLineOfSight sight)
        {
        }
    }
}

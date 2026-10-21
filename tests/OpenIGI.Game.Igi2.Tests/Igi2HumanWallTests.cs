using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The wall sweep, <c>Human_ObjectCollision</c> (<c>igi2 0x4FBA60</c>).</summary>
public sealed class Igi2HumanWallTests
{
    private static readonly Lazy<(Igi2Level Level, Igi2CollisionWorld World)> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        return (level, Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain"));
    });

    /// <summary>A ceiling 1.5 metres above a standing body takes standing and jumping away, and the body cannot rise.</summary>
    [Fact]
    public void A_low_ceiling_takes_standing_and_jumping_away()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(6000.0, facesUp: false));

        Vector3d to = new(0.0, 0.0, 300.0);
        player.ObjectCollision(default, ref to, out Vector3f speed);

        Assert.False(player.AllowedStance.HasFlag(Igi2AllowedStance.Stand));
        Assert.False(player.AllowedStance.HasFlag(Igi2AllowedStance.Jump));
        Assert.True(player.AllowedStance.HasFlag(Igi2AllowedStance.Crouch));
        Assert.Equal(0.0, to.Z);
        Assert.Equal(0f, speed.Z);
    }

    /// <summary>Lying flat under 1.5 metres of headroom leaves only lying; under 1.8, crouching too.</summary>
    [Theory]
    [InlineData(5000.0, false)]
    [InlineData(7000.0, true)]
    public void A_ceiling_over_a_prone_body_keeps_it_down(double height, bool mayCrouch)
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        player.Pose = Igi2HumanPose.LyingFlat;
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(height, facesUp: false));

        Vector3d to = default;
        player.ObjectCollision(default, ref to, out _);

        Assert.False(player.AllowedStance.HasFlag(Igi2AllowedStance.Stand));
        Assert.False(player.AllowedStance.HasFlag(Igi2AllowedStance.Jump));
        Assert.Equal(mayCrouch, player.AllowedStance.HasFlag(Igi2AllowedStance.Crouch));
        Assert.True(player.AllowedStance.HasFlag(Igi2AllowedStance.Lie));
    }

    /// <summary>With nothing around, a move goes where it was going and every posture is allowed.</summary>
    [Fact]
    public void An_open_move_is_untouched()
    {
        (Igi2HumanPlayer player, _) = Body();
        player.AllowedStance = Igi2AllowedStance.None;

        Vector3d to = new(300.0, -200.0, 0.0);
        player.ObjectCollision(default, ref to, out Vector3f speed);

        Assert.Equal(new Vector3d(300.0, -200.0, 0.0), to);
        Assert.Equal(new Vector3f(300f, -200f, 0f), speed);
        Assert.Equal(Igi2AllowedStance.All, player.AllowedStance);
        Assert.Equal(0u, (uint)player.Signal & 0x4);
    }

    /// <summary>
    /// On mission 1, walking straight at a wall stops short of it, since the slide takes the whole of the move into
    /// the wall away; walking along it keeps moving.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_wall_stops_a_body_walking_into_it_and_lets_it_slide_along()
    {
        (Igi2Level level, Igi2CollisionWorld world) = Mission1.Value;
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeSurroundings surroundings) = Body();
        surroundings.Query = world;

        Vector3d feet = Stand(player, spawn.Position);
        (Vector3d wallPoint, Vector3f normal) = NearestWall(world, feet);

        // Half a metre from the wall, facing it, stepping a metre into it.
        Vector3d into = new(-normal.X, -normal.Y, 0.0);
        Vector3d from = new(wallPoint.X + (normal.X * 2048.0), wallPoint.Y + (normal.Y * 2048.0), feet.Z);
        Vector3d to = new(from.X + (into.X * 4096.0), from.Y + (into.Y * 4096.0), from.Z);
        player.ObjectCollision(from, ref to, out _);

        double before = Distance(from, wallPoint, normal);
        double after = Distance(to, wallPoint, normal);
        Assert.True(((uint)player.Signal & 0x4) != 0, "the sweep never touched the wall.");
        Assert.True(after > 0.0, $"the body went through the wall: {after} units past it.");
        Assert.True(after <= before + 1.0, $"the wall threw the body back: {before} to {after}.");

        // Along the wall: the move keeps most of its length.
        Vector3d along = new(-normal.Y, normal.X, 0.0);
        Vector3d slideFrom = new(wallPoint.X + (normal.X * 1024.0), wallPoint.Y + (normal.Y * 1024.0), feet.Z);
        Vector3d slideTo = new(slideFrom.X + (along.X * 400.0), slideFrom.Y + (along.Y * 400.0), slideFrom.Z);
        player.ObjectCollision(slideFrom, ref slideTo, out Vector3f slide);
        Assert.True(Math.Sqrt((slide.X * slide.X) + (slide.Y * slide.Y)) > 100.0, $"a slide along the wall went nowhere: {slide}.");
    }

    private static double Distance(Vector3d point, Vector3d wall, Vector3f normal) =>
        ((point.X - wall.X) * normal.X) + ((point.Y - wall.Y) * normal.Y);

    private static Vector3d Stand(Igi2HumanPlayer player, Vector3d at)
    {
        Vector3d feet = at;
        player.GroundCollision(at, ref feet, 819.2f);
        return feet;
    }

    /// <summary>The nearest steep surface around a point at waist height, by casting horizontally in sixteen directions.</summary>
    private static (Vector3d Point, Vector3f Normal) NearestWall(Igi2CollisionWorld world, Vector3d feet)
    {
        Igi2LineTrace trace = new(16) { Mesh = 1 };
        for (int i = 0; i < 16; i++)
        {
            double angle = i * Math.PI / 8.0;
            Vector3d start = new(feet.X, feet.Y, feet.Z + 4096.0);
            trace.Starts[i] = start;
            trace.Ends[i] = new Vector3d(start.X + (Math.Cos(angle) * 409600.0), start.Y + (Math.Sin(angle) * 409600.0), start.Z);
        }

        world.TraceLines(trace, Igi2RigidObject.RigidGroup);
        Igi2Intersection? best = null;
        foreach (Igi2Intersection hit in trace.Intersections)
        {
            if (hit.IsHit && Math.Abs(hit.Normal.Z) < 0.3f && (best is null || hit.Distance < best.Distance))
            {
                best = hit;
            }
        }

        Assert.True(best is not null, "no wall within a hundred metres of the spawn.");
        Vector3f n = best!.Normal;
        double length = Math.Sqrt((n.X * n.X) + (n.Y * n.Y));
        return (best.Position, new Vector3f((float)(n.X / length), (float)(n.Y / length), 0f));
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeSurroundings Surroundings) Body()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.GroundCheckZOffset = 4096f;
        return (player, surroundings);
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Poses and the vertical query against mission 1's placed objects and ground.</summary>
public sealed class RetailIgi2CollisionWorldTests
{
    private static readonly Lazy<Igi2Level> Mission1 = new(static () => Igi2Level.Find("location1", "level1"));

    private static readonly Lazy<Igi2CollisionWorld> World = new(static () =>
        Igi2CollisionWorld.Build(Mission1.Value.Scene, Mission1.Value.ModelFor, Mission1.Value.Terrain(), "terrain"));

    /// <summary>The pose from three angles is Z(gamma)·Y(beta)·X(alpha), to the last bits single precision keeps.</summary>
    [Fact]
    public void A_pose_from_angles_is_the_engine_rotation()
    {
        Random random = new(11);
        for (int i = 0; i < 200; i++)
        {
            Vector3f angles = new((float)(random.NextDouble() * 7.0 - 3.5), (float)(random.NextDouble() * 7.0 - 3.5), (float)(random.NextDouble() * 7.0 - 3.5));
            Igi2Pose pose = Igi2Pose.FromAngles(default, angles);
            Matrix3f expected = Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z);
            float[] rows = [expected.M11, expected.M12, expected.M13, expected.M21, expected.M22, expected.M23, expected.M31, expected.M32, expected.M33];
            for (int k = 0; k < 9; k++)
            {
                Assert.InRange(pose.Orientation[k], rows[k] - 4e-7f, rows[k] + 4e-7f);
            }
        }
    }

    /// <summary>
    /// An attachment is turned by its record's matrix transposed, and placed by the parent's rows dotted with its
    /// offset: <c>ModelObj_CreateAttachObject</c>.
    /// </summary>
    [Fact]
    public void An_attachment_is_turned_by_its_record_transposed()
    {
        Igi2Pose parent = Igi2Pose.FromAngles(new Vector3d(1302000.0, -654321.5, 2048.0), new Vector3f(0.3f, -0.2f, 1.1f));
        Matrix3f turn = Matrix3f.FromEngineEulerAngles(0.1f, 0.4f, -0.7f);
        MefAttachment record = new("part", new Vector3f(512f, -256f, 64f), turn, 0);

        Igi2Pose child = parent.Attach(record);

        double[] p = [.. parent.Orientation.Select(static v => (double)v)];
        double[] f = [turn.M11, turn.M12, turn.M13, turn.M21, turn.M22, turn.M23, turn.M31, turn.M32, turn.M33];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                // (P · Fᵀ)[r][c] = Σk P[r][k] · F[c][k].
                double expected = (p[(3 * r) + 0] * f[(3 * c) + 0]) + (p[(3 * r) + 1] * f[(3 * c) + 1]) + (p[(3 * r) + 2] * f[(3 * c) + 2]);
                Assert.InRange(child.Orientation[(3 * r) + c], expected - 1e-6, expected + 1e-6);
            }
        }

        Vector3d offset = new(
            (p[0] * 512.0) + (p[1] * -256.0) + (p[2] * 64.0),
            (p[3] * 512.0) + (p[4] * -256.0) + (p[5] * 64.0),
            (p[6] * 512.0) + (p[7] * -256.0) + (p[8] * 64.0));

        // The sum with the parent's position is a register sum: a 24-bit step at 1.3 million units is a quarter.
        Assert.InRange(child.Position.X, parent.Position.X + offset.X - 0.25, parent.Position.X + offset.X + 0.25);
        Assert.InRange(child.Position.Y, parent.Position.Y + offset.Y - 0.125, parent.Position.Y + offset.Y + 0.125);
        Assert.InRange(child.Position.Z, parent.Position.Z + offset.Z - 1e-3, parent.Position.Z + offset.Z + 1e-3);
    }

    /// <summary>
    /// Every attached part the collision world places is drawn at the same place, turned the same way: both go
    /// through <see cref="Igi2Pose.Attach"/>, so what is seen is what is stood on.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Attached_parts_are_drawn_where_they_collide()
    {
        IReadOnlyList<WorldPlacement> drawn = Igi2LevelScene.Build(Mission1.Value).Built.Scene.Placements;
        Dictionary<(long, long, long), List<Matrix3f>> byCell = [];
        foreach (WorldPlacement placement in drawn)
        {
            (long, long, long) cell = Cell(placement.Position * WorldScene.UnitsPerMetre);
            if (!byCell.TryGetValue(cell, out List<Matrix3f>? list))
            {
                byCell[cell] = list = [];
            }

            list.Add(placement.Orientation);
        }

        int compared = 0, turned = 0;
        foreach (Igi2RigidObject solid in World.Value.Objects)
        {
            if (solid.Owner is not Igi2CollisionOwner { Depth: > 0 })
            {
                continue;
            }

            float[] m = solid.Orientation;
            Matrix3f expected = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
            Assert.True(byCell.TryGetValue(Cell(solid.Position), out List<Matrix3f>? candidates), $"nothing drawn at {solid.Position}.");
            Assert.Contains(candidates, c => c.Equals(expected));
            compared++;
            turned += Math.Abs(m[1] - m[3]) > 0.5f ? 1 : 0;
        }

        Assert.True(compared > 100, $"only {compared} attached parts were compared.");
        Assert.True(turned > 0, "no attached part is turned asymmetrically, so the transpose is untested.");

        static (long, long, long) Cell(Vector3d units) => ((long)Math.Round(units.X), (long)Math.Round(units.Y), (long)Math.Round(units.Z));
    }

    /// <summary>Mission 1's world holds its placed solid models and their attachments, and its ground.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_world_holds_its_solid_models()
    {
        Igi2CollisionWorld world = World.Value;

        Assert.NotNull(world.Terrain);
        Assert.InRange(world.Objects.Count, 450, 5000);
        Assert.Contains(world.Objects, static o => o.Owner is Igi2CollisionOwner { TypeName: "Building", Depth: 0 });
        Assert.Contains(world.Objects, static o => o.Owner is Igi2CollisionOwner { Depth: > 0 });
        Assert.DoesNotContain(world.Objects, static o => o.Owner is Igi2CollisionOwner { TypeName: "HumanSoldier" or "EditBoneObj" });
    }

    /// <summary>The player's own footing query stands mission 1's spawn on something no more than a few centimetres off its authored height.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_spawn_stands_on_its_footing()
    {
        Assert.True(PlayerSpawn.TryFindGameplay(Mission1.Value.Root, out PlayerSpawnPoint spawn));

        WorldIntersection ground = Footing(spawn.Position);

        Assert.True(ground.IsHit, "nothing under the spawn.");
        Assert.InRange(ground.Position.Z, spawn.Position.Z - 2048.0, spawn.Position.Z + 2048.0);
    }

    /// <summary>Over the hangar, where the terrain is cut away, a body stands on the building's floor.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_hangar_floor_is_stood_on()
    {
        Igi2TerrainSurface surface = Mission1.Value.Terrain();
        Igi2DiscardBox box = Assert.Single(surface.DiscardBoxes);
        Vector3d middle = new(
            surface.Origin.X + ((box.MinX + (double)box.MaxX) * 0.5),
            surface.Origin.Y + ((box.MinY + (double)box.MaxY) * 0.5),
            surface.Origin.Z + 200000.0);

        // From well above, so the roof, if any, is found first; step down until a floor faces up.
        WorldIntersection hit = Footing(middle, reach: -400000.0);
        Assert.True(hit.IsHit, "nothing under the hangar.");
        Assert.IsType<Igi2CollisionOwner>(hit.HitObject?.Owner);
    }

    /// <summary>The two-line query <c>Human_GroundCollision</c> makes: down 40960 for the ground, up 8192 for the roof.</summary>
    private static WorldIntersection Footing(Vector3d at, double reach = -40960.0)
    {
        WorldLineTrace trace = new(2) { Mesh = 1 };
        trace.Starts[0] = at;
        trace.Starts[1] = at;
        World.Value.NextZ(trace, [reach, 8192.0], 0xFFFFFFFF);
        return trace.Intersections[0];
    }
}

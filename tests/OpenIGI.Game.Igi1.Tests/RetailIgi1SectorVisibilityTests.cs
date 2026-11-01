using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Sectors;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s sector cull on mission 1: what a camera inside a building, and one outside, leaves drawn.</summary>
/// <remarks>
/// The case that prompted it: a chain fence, <c>303_01_1</c>, runs along the outside of an office of <c>Building_1118</c> and
/// clips through one of its corners. Retail never shows it from inside, because <c>QCube_CallHandlers</c> (<c>IGI.exe 0x4D12C0</c>)
/// draws an object beyond the building's inside sphere only once the walk from the camera's room has reached the shell.
/// </remarks>
public sealed class RetailIgi1SectorVisibilityTests
{
    // The free camera's telemetry where the fence was reported: units, and the yaw and pitch in degrees.
    private static readonly Vector3d ReportedEye = new(24548789, -55912539, 174423676);
    private const float ReportedYaw = 32.8f;
    private const float ReportedPitch = -7.3f;

    // The fence's own origin, beside the office.
    private static readonly Vector3d FenceOrigin = new(24588444, -55901136, 174412480);

    private static readonly Lazy<Setup> Mission1 = new(() => Load(1));

    private static readonly Lazy<Setup> Mission3 = new(() => Load(3));

    private static readonly Lazy<Setup> Mission14 = new(() => Load(14));

    // A standing player's eye above the feet, world units.
    private static readonly double Eye = new PlayerBody().EyeHeight;

    /// <summary>
    /// From mission 1's compound, standing on <c>Building_1118</c>'s shell and looking along the fence, the rail beyond it stays
    /// drawn as the view turns: a length of rail is weighed against the outside's window by its bent mesh's bound, laid about its
    /// middle (<c>0x4E4530</c>), not by the straight model's bound at its first waypoint, which left the window as the view
    /// turned away from that one end.
    /// </summary>
    [RequiresGameDataFact]
    public void A_bent_rail_is_weighed_by_its_own_bound()
    {
        Setup m = Mission1.Value;
        WorldPlacement rail = m.Scene.Placements.Single(static p => p.TaskName == "spline:1:4");
        foreach (float yaw in new[] { -18.1f, -3.2f })
        {
            m.Visibility.Decide(
                new Camera3D(new Vector3d(24578866, -55865450, 174413292 + Eye), Camera3D.LookRotation(yaw * MathF.PI / 180f, -1.2f * MathF.PI / 180f)),
                SectorScreenProjection.ForView(1f, 0.5625f));
            Assert.True(m.Visibility.IsVisible(rail), $"the rail is culled at yaw {yaw}");
        }
    }

    /// <summary>
    /// In mission 3's tunnel, looking in at the inner room's door, and on its ramp looking back out: a portal the eye stands in
    /// covers the screen, as the original's (logged at <c>Sector_FloodPortals</c>, <c>0x4DD650</c>) do, so the rooms beyond
    /// are drawn. The near clip's interpolated depth used to round under the plane and cut each to a sliver at the screen's edge.
    /// </summary>
    [RequiresGameDataFact]
    public void A_portal_the_eye_stands_in_covers_the_screen()
    {
        Setup m = Mission3.Value;
        m.Visibility.Decide(
            new Camera3D(new Vector3d(23786718, -55266762, 174406288), Camera3D.LookRotation(-456.4f * MathF.PI / 180f, -6.5f * MathF.PI / 180f)),
            Projection());
        Assert.Contains(m.Scene.Placements, p => p.Owner.Name == "Building_3083" && p.Instance.Name == "424_02_1" && m.Visibility.IsVisible(p));

        m.Visibility.Decide(
            new Camera3D(new Vector3d(23783625, -55273469, 174405691), Camera3D.LookRotation(-643f * MathF.PI / 180f, -1.4f * MathF.PI / 180f)),
            Projection());
        Assert.Contains(m.Scene.Placements, p => p.Owner.Name == "Building_3083" && p.Instance.Name == "424_01_1" && m.Visibility.IsVisible(p));
        Assert.True(m.Visibility.SeesOut);
    }

    /// <summary>
    /// On mission 3's ramp down into <c>Building_3083</c>'s tunnel the eye stands in the tunnel room, as the original's locate
    /// (logged at <c>0x4DBCC0</c>) says: the shell's walking level has a floor 3.7 cm above the tunnel's there, and a locate that
    /// met it put the eye on the shell, both of whose portals face away, so the tunnel and the rooms beyond it were never drawn.
    /// </summary>
    [RequiresGameDataFact]
    public void On_the_tunnel_ramp_the_eye_stands_in_the_tunnel()
    {
        Setup m = Mission3.Value;
        Vector3d eye = new(23779577, -55268579, 174398817 + 6000);
        m.Visibility.Decide(new Camera3D(eye, Camera3D.LookRotation(-91.2f * MathF.PI / 180f, -6.9f * MathF.PI / 180f)), Projection());

        Assert.StartsWith("sector: Building_3083 room", m.Visibility.DescribeView(), StringComparison.Ordinal);
        Assert.Contains(m.Scene.Placements, p => p.Owner.Name == "Building_3083" && p.Instance.Name == "424_02_1" && m.Visibility.IsVisible(p));
    }

    /// <summary>
    /// From inside the office the office's rooms enclose the eye and stay two-sided; the garage's rooms, whose white inner walls
    /// lie in the planes of its doors, do not, so they are culled as the original culls every model and stop showing through.
    /// </summary>
    [RequiresGameDataFact]
    public void Only_the_eyes_own_building_encloses_it()
    {
        Setup m = Mission1.Value;
        m.Visibility.Decide(
            new Camera3D(ReportedEye, Camera3D.LookRotation(ReportedYaw * MathF.PI / 180f, ReportedPitch * MathF.PI / 180f)),
            Projection());

        Assert.All(m.Scene.Placements.Where(static p => p.Owner.Name == "Building_1118" && p.Model.IsSector), p => Assert.True(m.Visibility.EnclosesEye(p)));
        IEnumerable<WorldPlacement> garage = m.Scene.Placements.Where(static p => p.Owner.Name == "Building_1107" && p.Model.IsSector);
        Assert.NotEmpty(garage);
        Assert.All(garage, p => Assert.False(m.Visibility.EnclosesEye(p)));
        Assert.True(m.Visibility.EnclosesEye(m.Fence), "a placement that is no building's part has no opinion");
    }

    /// <summary>
    /// In mission 14's ammo yard, standing in the doorway of a corridor building (<c>476_01_1</c>) set into the yard's wall, the
    /// eye is located in the corridor, but the yard around it is a room of another building (<c>462_04_1</c>,
    /// <c>LEVEL14_AMMO_AREA</c>). That room encloses the eye too: culled from outside, its floor and walls vanished and the
    /// crates stood on fog.
    /// </summary>
    [RequiresGameDataFact]
    public void A_room_around_the_eye_encloses_it_though_the_eye_is_located_in_another_building()
    {
        Setup m = Mission14.Value;
        m.Visibility.Decide(
            new Camera3D(new Vector3d(24712755, -56216826, 174087119 + Eye), Camera3D.LookRotation(80.9f * MathF.PI / 180f, 4.9f * MathF.PI / 180f)),
            Projection());

        WorldPlacement yard = m.Scene.Placements.First(static p => p.Instance.Name == "462_04_1");
        Assert.NotSame(m.Visibility.EyeBuilding, yard.Owner);
        Assert.True(m.Visibility.IsVisible(yard));
        Assert.True(m.Visibility.EnclosesEye(yard));
    }

    /// <summary>
    /// In mission 14's upper lift room, facing the shaft door, the car of <c>Elevator_1701</c> ridden up stands behind the door,
    /// and is drawn there: it is weighed where it is drawn, not at the bottom of the shaft where the level places it, which
    /// lies outside the doorway's window and left the shaft empty.
    /// </summary>
    [RequiresGameDataFact]
    public void A_lift_car_is_weighed_where_it_stands()
    {
        Setup m = Mission14.Value;
        m.Visibility.Decide(
            new Camera3D(new Vector3d(24583283, -56221005, 174145533 + Eye), Camera3D.LookRotation(-540.5f * MathF.PI / 180f, -1.1f * MathF.PI / 180f)),
            Projection());

        ElevatorRegistry lifts = ElevatorRegistry.Build(m.Root, new ScriptVarRegistry(new NativeRegistry()));
        PlacedElevator lift = lifts.Elevators.Single(static l => l.Task.Name == "Elevator_1701");
        WorldPlacement car = m.Scene.Placements.First(p => ReferenceEquals(p.Owner, lift.Task));
        Vector3d top = car.Position + ((lift.State.Floors[^1].Position - lift.State.StartPosition) / WorldScene.UnitsPerMetre);

        Assert.False(m.Visibility.IsVisible(car), "at the bottom of the shaft the car is beyond the doorway");
        Assert.True(m.Visibility.IsVisible(car, top), "ridden up, the car behind the doorway is culled");
    }

    /// <summary>
    /// Mission 14's halls carry their normals out of the room, so wound to them their floors faced the ground beneath and a
    /// hall seen into from a corridor, culled, lost its floor and walls. Built from the authored order, every hall's floor faces
    /// up into it, and the rooms whose normals face in are wound as they were.
    /// </summary>
    /// <param name="room">The room model's instance name.</param>
    [RequiresGameDataTheory]
    [InlineData("482_02_1")]
    [InlineData("481_02_1")]
    [InlineData("462_04_1")]
    [InlineData("476_02_1")]
    public void A_room_floor_faces_up_into_the_room(string room)
    {
        Setup m = Mission14.Value;
        MeshGeometry mesh = MefMeshBuilder.Build(m.Scene.Placements.First(p => p.Instance.Name == room).Model)!;
        float bottom = mesh.Vertices.Min(static v => v.Position.Z), top = mesh.Vertices.Max(static v => v.Position.Z);
        int up = 0, down = 0;
        for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            Vertex3D a = mesh.Vertices[mesh.Indices[t]], b = mesh.Vertices[mesh.Indices[t + 1]], c = mesh.Vertices[mesh.Indices[t + 2]];
            Vector3f face = Vector3f.Cross(b.Position - a.Position, c.Position - a.Position);
            if ((a.Position.Z + b.Position.Z + c.Position.Z) / 3f < bottom + ((top - bottom) * 0.2f) && MathF.Abs(face.Z) > 0.9f * MathF.Sqrt(face.LengthSquared))
            {
                up += face.Z > 0f ? 1 : 0;
                down += face.Z < 0f ? 1 : 0;
            }
        }

        Assert.True(up > 0 && down == 0, $"{room}: {up} floor faces up, {down} down");
    }

    /// <summary>
    /// On the stairs of mission 3's security building the eye stands just above the portal the stairwell (<c>400_08_1</c>) and
    /// the floor below (<c>400_09_1</c>) share, and the locate's line meets it at one depth in both rooms. The original's line
    /// handler (<c>0x4C1840</c>) keeps the face it meets from the front, the stairwell's, so the eye is in the stairwell; keeping
    /// whichever copy rounded nearer put it below, behind the portal, and the stairwell's walls vanished.
    /// </summary>
    [RequiresGameDataFact]
    public void On_the_security_building_stairs_the_eye_stands_in_the_stairwell()
    {
        Setup m = Mission3.Value;
        m.Visibility.Decide(
            new Camera3D(new Vector3d(23273798, -54832592, 174408581 + Eye), Camera3D.LookRotation(-219.5f * MathF.PI / 180f, 13.6f * MathF.PI / 180f)),
            Projection());

        Assert.StartsWith("sector: Building_3072 room 3", m.Visibility.DescribeView(), StringComparison.Ordinal);
        Assert.True(m.Visibility.IsVisible(m.Scene.Placements.Single(static p => p.Owner.Name == "Building_3072" && p.Instance.Name == "400_08_1")));
    }

    /// <summary>Mission 1 places 26 buildings, 21 rooms among them.</summary>
    [RequiresGameDataFact]
    public void Mission_one_places_its_buildings_and_their_rooms()
    {
        Setup m = Mission1.Value;
        Assert.Equal(26, m.Visibility.BuildingCount);
        Assert.Equal(21, m.Visibility.RoomCount);
    }

    /// <summary>
    /// At the reported pose the camera stands in a room of the office whose only opening to the outside it is behind the plane
    /// of, so the walk never sees out; the fence, beyond the inside sphere, is not drawn -- as in the original.
    /// </summary>
    [RequiresGameDataFact]
    public void The_fence_through_the_office_wall_is_not_drawn_from_inside()
    {
        Setup m = Mission1.Value;
        m.Visibility.Decide(
            new Camera3D(ReportedEye, Camera3D.LookRotation(ReportedYaw * MathF.PI / 180f, ReportedPitch * MathF.PI / 180f)),
            Projection());

        Assert.Equal("Building_1118", m.Visibility.EyeBuilding?.Name);
        Assert.False(m.Visibility.SeesOut);
        Assert.True(m.Visibility.IsOutsideVisible, "the ground is drawn whatever the camera stands in");
        Assert.False(m.Visibility.IsVisible(m.Fence));
        Assert.StartsWith("sector: Building_1118 room 2, sees no way out", m.Visibility.DescribeView(), StringComparison.Ordinal);

        // The office's own room, and what it carries, are drawn.
        Assert.Contains(m.Scene.Placements, p => p.Owner.Name == "Building_1118" && p.IsAttached && m.Visibility.IsVisible(p));

        // So is its shell, which the walk never reached: it stands behind the walls to close the seams between them.
        Assert.Contains(m.Scene.Placements, p => p.Owner.Name == "Building_1118" && !p.IsAttached && m.Visibility.IsVisible(p));
    }

    /// <summary>The room that cannot see out from the reported spot cannot from anywhere in it: turning the camera changes nothing.</summary>
    [RequiresGameDataFact]
    public void Turning_on_the_spot_never_sees_out_of_that_room()
    {
        Setup m = Mission1.Value;
        for (int yaw = 0; yaw < 360; yaw += 15)
        {
            m.Visibility.Decide(new Camera3D(ReportedEye, Camera3D.LookRotation(yaw * MathF.PI / 180f, 0f)), Projection());
            Assert.False(m.Visibility.SeesOut, $"yaw {yaw}");
            Assert.False(m.Visibility.IsVisible(m.Fence), $"yaw {yaw}");
        }
    }

    /// <summary>
    /// From outside, nothing but rooms and what they carry is ever left out: every task's own placement, and everything that is
    /// not a building's, stays to the frustum.
    /// </summary>
    [RequiresGameDataFact]
    public void From_outside_only_unseen_rooms_are_left_out()
    {
        Setup m = Mission1.Value;
        Assert.True(PlayerSpawn.TryFindGameplay(m.Root, out PlayerSpawnPoint spawn));
        Vector3d eye = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 7000.0);

        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            m.Visibility.Decide(new Camera3D(eye, Camera3D.LookRotation(yaw * MathF.PI / 180f, 0f)), Projection());
            Assert.Null(m.Visibility.EyeBuilding);
            Assert.True(m.Visibility.IsVisible(m.Fence));
            Assert.All(
                m.Scene.Placements.Where(p => !m.Visibility.IsVisible(p)),
                p =>
                {
                    Assert.True(p.IsAttached, $"{p.Instance.Name} of {p.Owner.Name} is a task's own placement");
                    Assert.Equal(Igi1SectorVisibility.SectorObjectType, p.Owner.Type.Name);
                });
        }
    }

    /// <summary>
    /// From a room that sees out, what lies beyond the inside sphere is clipped to the outside's window -- a part of the view, not
    /// all of it -- and the building the camera stands in is not clipped at all.
    /// </summary>
    [RequiresGameDataFact]
    public void Seen_out_of_a_room_the_outside_is_clipped_to_its_window()
    {
        Setup m = Mission1.Value;
        Assert.True(TrySeeOut(m, out Camera3D eye), "no room of mission 1 sees out from a metre above its origin");

        m.Visibility.Decide(eye, Projection());
        Assert.True(m.Visibility.SeesOut);
        int clipped = 0;
        foreach (WorldPlacement placement in m.Scene.Placements)
        {
            if (m.Visibility.ClipOf(placement) is not { } clip)
            {
                continue;
            }

            clipped++;
            Assert.NotSame(m.Visibility.EyeBuilding, placement.Owner);
            Assert.InRange(clip.Left, -0.001f, 1.001f);
            Assert.InRange(clip.Right, clip.Left, 1.001f);
            Assert.InRange(clip.Top, -0.001f, 1.001f);
            Assert.InRange(clip.Bottom, clip.Top, 1.001f);
            Assert.NotEqual(ViewClip.Full, clip);
        }

        Assert.True(clipped > 0, "seeing out clipped nothing");
        Assert.Null(((IPlacementVisibility)m.Visibility).OutsideClip);
    }

    /// <summary>
    /// A soldier is gated as every queued object is: inside the sphere it is drawn, beyond it only through the outside's window --
    /// so from the reported spot, which never sees out, a soldier by the fence is not drawn and one in the office is.
    /// </summary>
    [RequiresGameDataFact]
    public void A_soldier_beyond_the_inside_sphere_is_gated_like_the_fence()
    {
        Setup m = Mission1.Value;
        Camera3D eye = new(ReportedEye, Camera3D.LookRotation(ReportedYaw * MathF.PI / 180f, ReportedPitch * MathF.PI / 180f));
        m.Visibility.Decide(eye, Projection());

        Assert.False(m.Visibility.IsObjectVisible(FenceOrigin, FenceOrigin, 4096f));
        Assert.True(m.Visibility.IsObjectVisible(ReportedEye, ReportedEye, 4096f));
        Assert.Null(m.Visibility.ClipOfObject(FenceOrigin));
        Assert.True(m.Visibility.IsEye(new Camera3D(WorldScene.ToMetres(ReportedEye), eye.Orientation)));
    }

    // Stands a metre above each room's origin and turns about until the walk sees out.
    private static bool TrySeeOut(Setup m, out Camera3D eye)
    {
        foreach (WorldPlacement room in m.Scene.Placements.Where(static p => p.IsAttached && p.Model.IsSector
                     && p.Owner.Type.Name == Igi1SectorVisibility.SectorObjectType))
        {
            Vector3d at = WorldScene.ToUnits(room.Position);
            at = new Vector3d(at.X, at.Y, at.Z + 4096.0);
            for (int yaw = 0; yaw < 360; yaw += 15)
            {
                eye = new Camera3D(at, Camera3D.LookRotation(yaw * MathF.PI / 180f, 0f));
                m.Visibility.Decide(eye, Projection());
                if (m.Visibility.EyeBuilding is not null && m.Visibility.SeesOut)
                {
                    return true;
                }
            }
        }

        eye = default;
        return false;
    }

    /// <summary>Two spheres apart are merged into the one spanning both, centred between their far edges.</summary>
    [Fact]
    public void Two_apart_spheres_merge_into_the_one_spanning_both()
    {
        (Vector3f centre, float radius) = SectorInsideSphere.Merge([(new Vector3f(0f, 0f, 0f), 1f), (new Vector3f(10f, 0f, 0f), 2f)])!.Value;
        Assert.Equal(new Vector3f(5.5f, 0f, 0f), centre);
        Assert.Equal(6.5f, radius);
    }

    /// <summary>A sphere inside another is dropped, and spheres sharing a centre keep the larger radius.</summary>
    [Fact]
    public void A_contained_sphere_is_dropped_and_a_shared_centre_keeps_the_larger_radius()
    {
        Assert.Equal((new Vector3f(0f, 0f, 0f), 5f), SectorInsideSphere.Merge([(new Vector3f(0f, 0f, 0f), 5f), (new Vector3f(1f, 0f, 0f), 1f)]));
        Assert.Equal((new Vector3f(1f, 0f, 0f), 5f), SectorInsideSphere.Merge([(new Vector3f(0f, 0f, 0f), 1f), (new Vector3f(1f, 0f, 0f), 5f)]));
        Assert.Equal((new Vector3f(2f, 2f, 2f), 3f), SectorInsideSphere.Merge([(new Vector3f(2f, 2f, 2f), 1f), (new Vector3f(2f, 2f, 2f), 3f)]));
        Assert.Null(SectorInsideSphere.Merge([]));
    }

    private static SectorScreenProjection Projection() => SectorScreenProjection.ForView(1.25f, 0.7f);

    private static Setup Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(
            scene.Placements, AssetFormatProfile.Igi1.Collision, name => mission.TryGetModel(name, out MefModel? model) ? model : null);
        Igi1SectorVisibility visibility = Igi1SectorVisibility.Build(scene, mission.TryGetModel, magic, solids)
            ?? throw new InvalidOperationException($"Mission {missionId} places no building.");
        WorldPlacement? fence = scene.Placements.SingleOrDefault(static p =>
            string.Equals(p.Instance.Name, "303_01_1", StringComparison.OrdinalIgnoreCase)
            && (WorldScene.ToUnits(p.Position) - FenceOrigin).LengthSquared < 1.0);
        return new Setup(root, scene, visibility, fence!);
    }

    private sealed record Setup(LevelTask Root, WorldScene Scene, Igi1SectorVisibility Visibility, WorldPlacement Fence);
}

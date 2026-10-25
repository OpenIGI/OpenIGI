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

    private static readonly Lazy<Setup> Mission1 = new(Load);

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

    private static Setup Load()
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
            missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(
            scene.Placements, AssetFormatProfile.Igi1.Collision, name => mission.TryGetModel(name, out MefModel? model) ? model : null);
        Igi1SectorVisibility visibility = Igi1SectorVisibility.Build(scene, mission.TryGetModel, magic, solids)
            ?? throw new InvalidOperationException("Mission 1 places no building.");
        WorldPlacement fence = scene.Placements.Single(static p =>
            string.Equals(p.Instance.Name, "303_01_1", StringComparison.OrdinalIgnoreCase)
            && (WorldScene.ToUnits(p.Position) - FenceOrigin).LengthSquared < 1.0);
        return new Setup(root, scene, visibility, fence);
    }

    private sealed record Setup(LevelTask Root, WorldScene Scene, Igi1SectorVisibility Visibility, WorldPlacement Fence);
}

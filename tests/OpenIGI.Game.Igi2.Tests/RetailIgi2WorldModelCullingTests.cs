using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>World bodies outside the camera do not consume skinning or mesh-upload work.</summary>
public sealed class RetailIgi2WorldModelCullingTests
{
    /// <summary>A hidden body is never uploaded; an existing body skips hidden ticks and refreshes on return.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Hidden_bodies_skip_mesh_creation_and_pose_uploads()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        BonePose pose = Pose(model, default);
        object instance = new();
        TitleWorldModel body = new(name, new Vector3d(0, -409600, 0), Matrix3f.Identity, pose, instance, 1);
        Matrix4f projection = Matrix4f.CreateProjection(640f, 360f, 640f, 360f, 1280, 720, 0.1f, 1000f);

        renderer.DrawWorld([body], Camera3D.Default, projection);
        Assert.Empty(device.Meshes);

        body = body with { Position = new Vector3d(0, 409600, 0) };
        renderer.DrawWorld([body], Camera3D.Default, projection);
        uint mesh = Assert.Single(device.AnimatedMeshUpdates).Key;
        Assert.NotEmpty(device.MeshDraws);
        int updates = device.AnimatedMeshUpdates[mesh];

        TitleWorldModel hidden = body with { Position = new Vector3d(0, -409600, 0), Tick = 2 };
        renderer.DrawWorld([hidden], Camera3D.Default, projection);
        Assert.Equal(updates, device.AnimatedMeshUpdates[mesh]);

        renderer.DrawWorld([body with { Tick = 3 }], Camera3D.Default, projection);
        Assert.Equal(updates + 1, device.AnimatedMeshUpdates[mesh]);
    }

    /// <summary>The culling sphere follows animation root translation even when the object origin is behind the eye.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Root_motion_carries_the_render_bound_into_view()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        TitleWorldModel body = new(name, new Vector3d(0, -409600, 0), Matrix3f.Identity,
            Pose(model, new Vector3f(0, 819200, 0)), new object(), 1);
        renderer.DrawWorld([body], Camera3D.Default, Matrix4f.CreateProjection(640f, 360f, 640f, 360f, 1280, 720, 0.1f, 1000f));
        Assert.Single(device.AnimatedMeshUpdates);
        Assert.NotEmpty(device.MeshDraws);
    }

    private static BonePose Pose(MefModel model, Vector3f root)
    {
        Igi2BoneHierarchy rig = model.BoneHierarchy!;
        return BonePose.Evaluate(rig.CreateSkeleton(), Enumerable.Repeat(Quaternion32.Identity, rig.BoneCount).ToArray(), root);
    }

    private static (LoadedMission, ResourceRegistry, string, MefModel) Mission()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        MefModel model = level.ModelFor(spawn.Model)!;
        Assert.True(model.RenderSphereRadius > 0f);
        MissionDescriptor descriptor = new(11, "igi2", string.Empty, string.Empty,
            MissionDescriptor.NextMissionUndefined, "MISSION:", packs[0].Directory, packs[^1].Directory,
            packs[0].Name, packs[^1].Name, string.Empty);
        return (new LoadedMission(descriptor, packs[0], packs[^1], [], Packs: packs), resources, spawn.Model, model);
    }
}

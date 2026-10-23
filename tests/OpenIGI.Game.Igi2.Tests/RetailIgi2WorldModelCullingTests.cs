using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.ViewModel;

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

        renderer.DrawWorld([body with { Tick = 3, Pose = Pose(model, new Vector3f(0, 0, 16)) }], Camera3D.Default, projection);
        Assert.Equal(updates + 1, device.AnimatedMeshUpdates[mesh]);
    }

    /// <summary>A body drawn for a thing the binoculars marked glows through walls in its colour; an unmarked one does not.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_marked_body_glows_through_walls_and_an_unmarked_one_does_not()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        object instance = new();
        TitleWorldModel[] drawn = [new(name, new Vector3d(0, 409600, 0), Matrix3f.Identity, Pose(model, default), instance, 1)];
        renderer.DrawWorld(drawn, Camera3D.Default, Matrix4f.CreateProjection(640f, 360f, 640f, 360f, 1280, 720, 0.1f, 1000f));

        renderer.DrawWorldOutlines(drawn, new Dictionary<object, Bgra32>(), Camera3D.Default, 8f);
        Assert.Equal(0, device.OutlineDrawCount);

        Bgra32 red = Bgra32.FromRgba(255, 72, 72, 210);
        renderer.DrawWorldOutlines(drawn, new Dictionary<object, Bgra32> { [instance] = red }, Camera3D.Default, 8f);
        Assert.Equal(1, device.OutlineDrawCount);
        Assert.Equal(red, device.LastOutlineColour);
        Assert.True(device.LastOutlineThroughWalls);
    }

    /// <summary>A fresh tick with an identical bone palette draws without skinning or uploading it again.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Unchanged_world_poses_do_not_upload_again_on_new_ticks()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        TitleWorldModel body = new(name, new Vector3d(0, 409600, 0), Matrix3f.Identity, Pose(model, default), new object(), 1);
        Matrix4f projection = Matrix4f.CreateProjection(640f, 360f, 640f, 360f, 1280, 720, 0.1f, 1000f);
        renderer.DrawWorld([body], Camera3D.Default, projection);
        uint mesh = Assert.Single(device.AnimatedMeshUpdates).Key;
        int updates = device.AnimatedMeshUpdates[mesh];
        int draws = device.MeshDraws.Count;
        renderer.DrawWorld([body with { Tick = 2, Pose = Pose(model, default) }], Camera3D.Default, projection);
        Assert.Equal(updates, device.AnimatedMeshUpdates[mesh]);
        Assert.Equal(draws * 2, device.MeshDraws.Count);
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

    /// <summary>A batch of independent palettes uploads exact serial vertices on the caller's graphics thread.</summary>
    [RequiresTitleDataFact("igi2")]
    public void World_skin_batch_preserves_vertices_and_graphics_thread()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        Igi2BoneSkin skin = Igi2BoneSkin.Create(model)!;
        TitleWorldModel[] bodies = new TitleWorldModel[8];
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i] = new(name, new Vector3d(0, 409600, 0), Matrix3f.Identity,
                Pose(model, new Vector3f(i * 16, 0, 0)), new object(), 1);
        }

        int thread = Environment.CurrentManagedThreadId;
        Matrix4f projection = Matrix4f.CreateProjection(640f, 360f, 640f, 360f, 1280, 720, 0.1f, 1000f);
        // The render mesh retains extra source records beyond the base vertices; skinning leaves that tail intact.
        Vertex3D[] expected = [.. MefMeshBuilder.Build(model, 1f)!.Vertices];
        for (int tick = 1; tick <= 3; tick++)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i] = bodies[i] with { Tick = tick, Pose = Pose(model, new Vector3f(i * 16, 0, tick * 32)) };
            }
            renderer.DrawWorld(bodies, Camera3D.Default, projection);
            Assert.Equal(bodies.Length, device.AnimatedMeshUpdates.Count);
            for (int i = 0; i < bodies.Length; i++)
            {
                skin.Skin(bodies[i].Pose!, 1f / 4096f, expected);
                Vertex3D[] actual = device.Meshes[device.AnimatedMeshUpdates.Keys.ElementAt(i)].Vertices;
                Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(actual.AsSpan())));
            }
        }
        Assert.All(device.AnimatedMeshThreadIds, id => Assert.Equal(thread, id));
    }

    private static BonePose Pose(MefModel model, Vector3f root)
    {
        Igi2BoneHierarchy rig = model.BoneHierarchy!;
        return BonePose.Evaluate(rig.CreateSkeleton(), Enumerable.Repeat(Quaternion32.Identity, rig.BoneCount).ToArray(), root);
    }

    /// <summary>The previous first-person pose is already uploaded; one update advances both GPU streams to the new tick pair.</summary>
    [RequiresTitleDataFact("igi2")]
    public void First_person_reuses_the_uploaded_previous_pose()
    {
        (LoadedMission mission, ResourceRegistry resources, string name, MefModel model) = Mission();
        using RecordingGraphicsDevice device = new();
        using Igi2ViewModelRenderer renderer = new(device, resources);
        Assert.True(renderer.Load(mission));
        BonePose previous = Pose(model, default), current = Pose(model, new Vector3f(0, 0, 16));
        TitleViewModelPart part = new(name, previous, default, Matrix3f.Identity);
        TitleFirstPersonView view = new(default, Matrix3f.Identity, 1f, 1, [part]);
        renderer.Draw(view, Camera3D.Default, Matrix4f.Identity);
        uint mesh = Assert.Single(device.AnimatedMeshUpdates).Key;
        Vertex3D[] before = device.Meshes[mesh].Vertices;
        renderer.Draw(view with { Tick = 2, PoseInterpolation = 0.5f,
            Parts = [part with { Pose = current, PreviousPose = previous }] }, Camera3D.Default, Matrix4f.Identity);
        Assert.Equal(1, device.AnimatedMeshUpdates[mesh]);
        Assert.Equal(before, device.PreviousPoseStreams[mesh]);
        Assert.NotEqual(before[0].Position, device.Meshes[mesh].Vertices[0].Position);
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

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.RayTracing;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// The traced world above the RHI, over acceleration structures with nothing behind them: what it builds,
/// when, and how long what it replaces is kept alive.
/// </summary>
/// <remarks>
/// It used to live inside the Vulkan backend, where the only way to see any of this was a device fault.
/// Above the RHI, every backend shares it, and a recording device can watch it.
/// </remarks>
public sealed class TracedWorldTests
{
    [Fact]
    public void A_scene_every_ray_misses_is_built_at_once_and_bound_until_the_world_is_ready()
    {
        using RecordingRhiDevice rhi = new() { AccelerationStructures = new RecordingAccelerationStructures() };
        using TracedWorld world = Open(rhi);

        // Built and submitted on its own, before any frame: a tracing shader binds a structure from its
        // first dispatch.
        RecordedRhiCommand.BuildAccelerationStructures placeholder =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BuildAccelerationStructures>());
        Assert.Single(placeholder.BottomLevel);
        Assert.Equal(1, placeholder.InstanceCount);

        Assert.False(world.IsWorldReady);
        Assert.Null(world.WorldScene);
        Assert.NotNull(world.TraceableScene);
    }

    [Fact]
    public void A_mesh_drawn_once_is_built_and_placed_by_the_next_update_and_only_placed_after_that()
    {
        using RecordingRhiDevice rhi = new() { AccelerationStructures = new RecordingAccelerationStructures() };
        using TracedWorld world = Open(rhi);
        TracedMeshBuffers mesh = Mesh(rhi);

        world.Observe(1, mesh, Matrix4f.Identity, new TracedSurface(0, 3, TextureId: 0), RayMask.All);
        RecordedRhiCommand.BuildAccelerationStructures first = Update(rhi, world);

        Assert.Single(first.BottomLevel);
        Assert.False(first.BottomLevel[0].Update);
        Assert.Equal(1, first.InstanceCount);
        Assert.True(world.IsWorldReady);
        Assert.Same(world.WorldScene, world.TraceableScene);

        // Offered again at the same place: no new structure, the same placement.
        world.Observe(1, mesh, Matrix4f.Identity, new TracedSurface(0, 3, TextureId: 0), RayMask.All);
        RecordedRhiCommand.BuildAccelerationStructures second = Update(rhi, world);

        Assert.Empty(second.BottomLevel);
        Assert.Equal(1, second.InstanceCount);
        Assert.Equal(1, world.Statistics.Geometries);
    }

    [Fact]
    public void A_skinned_mesh_is_refitted_every_frame_rather_than_rebuilt()
    {
        using RecordingRhiDevice rhi = new() { AccelerationStructures = new RecordingAccelerationStructures() };
        using TracedWorld world = Open(rhi);
        TracedMeshBuffers body = Mesh(rhi) with { Animated = true };

        world.Observe(7, body, Matrix4f.Identity, new TracedSurface(0, 3, TextureId: 0), RayMask.All);
        Assert.False(Assert.Single(Update(rhi, world).BottomLevel).Update);

        world.Observe(7, body, Matrix4f.Identity, new TracedSurface(0, 3, TextureId: 0), RayMask.All);
        Assert.True(Assert.Single(Update(rhi, world).BottomLevel).Update);
        Assert.Equal(1, world.Statistics.Skinned);
    }

    [Fact]
    public void A_forgotten_mesh_is_freed_only_once_every_frame_that_could_trace_it_has_finished()
    {
        RecordingAccelerationStructures structures = new();
        using RecordingRhiDevice rhi = new() { AccelerationStructures = structures, HoldsSubmittedWork = true };
        using TracedWorld world = Open(rhi);
        TracedMeshBuffers mesh = Mesh(rhi);

        world.Observe(1, mesh, Matrix4f.Identity, new TracedSurface(0, 3, TextureId: 0), RayMask.All);
        Update(rhi, world);
        RecordedAccelerationStructure built = structures.Created.Last(s => s.Kind == RhiAccelerationStructureKind.BottomLevel);

        world.Forget(1);

        // The GPU has finished nothing, however many frames go by: the structure stays.
        for (int frame = 0; frame < 8; frame++)
        {
            Update(rhi, world);
        }

        Assert.False(built.IsDisposed);

        // Everything submitted finishes; the next updates find the frames that named it are done.
        rhi.ReleaseSubmittedWork();
        Update(rhi, world);

        Assert.True(built.IsDisposed);
    }

    [Fact]
    public void A_new_top_level_is_built_into_a_structure_no_frame_in_flight_is_tracing()
    {
        RecordingAccelerationStructures structures = new();
        using RecordingRhiDevice rhi = new() { AccelerationStructures = structures, HoldsSubmittedWork = true };
        using TracedWorld world = Open(rhi);

        string? first = Update(rhi, world).TopLevel;
        string? second = Update(rhi, world).TopLevel;
        int scenes = structures.Created.Count(s => s.Kind == RhiAccelerationStructureKind.TopLevel);

        Assert.NotNull(first);
        Assert.Equal(first, second);

        // Two frames in flight, each with its own scene structure: building into the first again would
        // write over the rays it is still casting. Placeholder's, and one per unfinished frame.
        Assert.Equal(3, scenes);
    }

    private static TracedWorld Open(RecordingRhiDevice rhi) =>
        new(rhi, rhi.AccelerationStructures!, albedoResolution: 32, NullLogger.Instance);

    private static TracedMeshBuffers Mesh(RecordingRhiDevice rhi)
    {
        IRhiBuffer vertices = rhi.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(3 * Vertex3D.Size), RhiBufferUsage.Vertex | RhiBufferUsage.RayTracingInput, "vertices"));
        IRhiBuffer indices = rhi.CreateBuffer(new RhiBufferDescriptor(
            sizeof(uint) * 3, RhiBufferUsage.Index | RhiBufferUsage.RayTracingInput, "indices"));

        return new TracedMeshBuffers(
            vertices,
            indices,
            VertexCapacity: 3,
            IndexCapacity: 3,
            DrawableIndexCount: 3,
            Animated: false,
            Storage: false,
            ContentVersion: 0);
    }

    private static RecordedRhiCommand.BuildAccelerationStructures Update(RecordingRhiDevice rhi, TracedWorld world)
    {
        using IRhiCommandEncoder encoder = rhi.CreateCommandEncoder("frame");
        world.RecordSceneUpdate(encoder);
        rhi.Submit([encoder.Finish()]);
        return rhi.CommandsOf<RecordedRhiCommand.BuildAccelerationStructures>()[^1];
    }
}

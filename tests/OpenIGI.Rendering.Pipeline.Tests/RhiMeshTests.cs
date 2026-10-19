using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That a mesh over the RHI keeps what the Vulkan one had, the pose ring above all.
/// </summary>
/// <remarks>
/// The ring is the part worth testing. It exists because a pose written over vertices a frame in flight
/// was still fetching made bodies judder, and the rule that fixed it — a slot is written only once
/// every frame that bound it has retired — is invisible in any picture and provable here.
/// </remarks>
public sealed class RhiMeshTests
{
    [Fact]
    public void A_static_mesh_holds_what_it_was_created_with()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateStatic(device, Pose(1f), [0, 1, 2], "wall");

        Assert.Equal(3, mesh.VertexCapacity);
        Assert.Equal(3, mesh.DrawableIndexCount);
        Assert.False(mesh.Animated);
        Assert.False(mesh.Storage);

        // The same buffer either way: with no ring, the previous pose is the current one.
        Assert.Same(mesh.VertexBuffer, mesh.PreviousVertexBuffer);
    }

    [Fact]
    public void An_animated_mesh_rings_through_more_slots_than_there_are_frames_in_flight()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        Assert.True(RhiMesh.PoseRingLength > RhiMesh.MaxFramesInFlight);
        Assert.Equal(
            RhiMesh.PoseRingLength,
            device.Resources.OfType<RecordedBuffer>().Count(b => b.Label.StartsWith("body", StringComparison.Ordinal)
                && !b.Label.EndsWith(".indices", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_new_pose_becomes_the_current_stream_and_the_old_one_becomes_the_previous()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        IRhiBuffer first = mesh.VertexBuffer;

        mesh.UpdateVertices(Pose(2f), frameCounter: 10);

        Assert.NotSame(first, mesh.VertexBuffer);
        Assert.Same(first, mesh.PreviousVertexBuffer);
    }

    [Fact]
    public void A_pose_is_never_written_over_a_slot_a_frame_in_flight_still_binds()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        long before = RhiMesh.PoseWriteHazards;
        HashSet<IRhiBuffer> live = [];

        // A tick and a frame, alternating, which is what a running game does.
        for (ulong frame = 1; frame <= 32; frame++)
        {
            mesh.UpdateVertices(Pose(frame), frame);

            // Whatever this frame binds must not be a buffer an unretired frame is still reading.
            Assert.DoesNotContain(mesh.VertexBuffer, live.Where(b => !ReferenceEquals(b, mesh.PreviousVertexBuffer)));

            mesh.MarkPoseSlotsBound(frame);

            live.Clear();
            live.Add(mesh.VertexBuffer);
            live.Add(mesh.PreviousVertexBuffer);
        }

        Assert.Equal(before, RhiMesh.PoseWriteHazards);
    }

    [Fact]
    public void Two_ticks_inside_one_frame_still_never_reuse_the_pose_being_drawn()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        mesh.MarkPoseSlotsBound(1);

        IRhiBuffer drawn = mesh.VertexBuffer;
        IRhiBuffer drawnPrevious = mesh.PreviousVertexBuffer;

        mesh.UpdateVertices(Pose(2f), frameCounter: 1);
        mesh.UpdateVertices(Pose(3f), frameCounter: 1);

        // The frame that is still in flight reads those two; neither may have been written over.
        Assert.NotSame(drawn, mesh.VertexBuffer);
        Assert.NotSame(drawnPrevious, mesh.VertexBuffer);
    }

    [Fact]
    public void Replacing_the_vertices_of_a_mesh_that_is_not_animated_is_refused()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateStatic(device, Pose(1f), [0, 1, 2], "wall");

        Assert.Throws<GraphicsDeviceException>(() => mesh.UpdateVertices(Pose(2f), 0));
    }

    [Fact]
    public void A_pose_of_the_wrong_length_is_refused()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        Assert.Throws<GraphicsDeviceException>(() => mesh.UpdateVertices(new Vertex3D[2], 0));
    }

    [Fact]
    public void A_storage_mesh_grows_what_it_draws_as_ranges_arrive()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(device, 64, 64, "terrain");

        Assert.Equal(0, mesh.DrawableIndexCount);

        mesh.UploadRange(0, Pose(1f), 0, [0, 1, 2]);
        Assert.Equal(3, mesh.DrawableIndexCount);

        mesh.UploadRange(3, Pose(2f), 3, [3, 4, 5]);
        Assert.Equal(6, mesh.DrawableIndexCount);
    }

    [Fact]
    public void An_odd_index_range_does_not_take_its_neighbours_with_it()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(device, 64, 64, "terrain");

        // Three indices from zero, then three more from three: the second range starts on an odd index,
        // so the write has to be widened to the word -- and the index it then also covers is the first
        // range's last. Without the shadow it would come back zero.
        mesh.UploadRange(0, Pose(1f), 0, [7, 8, 9]);
        mesh.UploadRange(3, Pose(2f), 3, [10, 11, 12]);

        RecordedBuffer indices = device.Resources.OfType<RecordedBuffer>().Single(b => b.Label == "terrain.indices");

        Assert.Equal<ushort[]>(
            [7, 8, 9, 10, 11, 12],
            [.. MemoryMarshal.Cast<byte, ushort>(indices.Contents)[..6]]);
    }

    [Fact]
    public void A_range_past_the_capacity_is_refused()
    {
        using RecordingRhiDevice device = new();
        using RhiMesh mesh = RhiMesh.CreateStorage(device, 4, 4, "terrain");

        Assert.Throws<GraphicsDeviceException>(() => mesh.UploadRange(3, Pose(1f), 0, []));
        Assert.Throws<GraphicsDeviceException>(() => mesh.UploadRange(0, [], 3, [0, 1, 2]));
    }

    [Fact]
    public void Every_buffer_of_a_mesh_goes_when_the_mesh_does()
    {
        using RecordingRhiDevice device = new();
        RhiMesh mesh = RhiMesh.CreateAnimated(device, Pose(1f), [0, 1, 2], "body");

        List<RecordedBuffer> buffers =
            [.. device.Resources.OfType<RecordedBuffer>().Where(b => b.Label.StartsWith("body", StringComparison.Ordinal))];

        Assert.All(buffers, b => Assert.False(b.IsDisposed));

        mesh.Dispose();

        Assert.All(buffers, b => Assert.True(b.IsDisposed));
    }

    private static Vertex3D[] Pose(float x) =>
    [
        new(new Vector3f(x, 0f, 0f), Vector3f.UnitY, 0f, 0f),
        new(new Vector3f(x, 1f, 0f), Vector3f.UnitY, 1f, 0f),
        new(new Vector3f(x, 1f, 1f), Vector3f.UnitY, 1f, 1f),
    ];

    private static Vertex3D[] Pose(ulong x) => Pose((float)x);
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// Pins that a marker on recorded work does not answer before that work has gone to the GPU.
/// </summary>
/// <remarks>
/// The RHI can only speak for what it has been sent, and a frame's work is sent when the frame is
/// presented. A marker that asked the RHI straight away would cover the frames before and not the one it
/// was taken in -- which is how the surface cache's convergence read sixteen slices as finished in two
/// milliseconds and kept the GPU fed until macOS killed it. The recording RHI calls every submission
/// finished at once, so what is left to see here is only when the device asks.
/// </remarks>
public sealed class RecordedWorkMarkerTests
{
    [Fact]
    public void A_marker_taken_inside_a_frame_waits_for_the_frame_to_be_presented()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));

        MeshHandle mesh = Triangle(device);

        device.Clear(new Bgra32(0, 0, 0, 255));
        device.DrawOutline(mesh, [new MeshDrawRange(0, 3)], Matrix4f.Identity, new Bgra32(255, 200, 0, 255), 6f, throughWalls: true);
        IGpuWorkMarker marker = device.MarkRecordedWork();

        Assert.False(marker.IsComplete);

        device.SwapBuffers();

        Assert.True(marker.IsComplete);
    }

    [Fact]
    public void A_marker_taken_between_frames_covers_what_has_already_been_sent()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));

        device.Clear(new Bgra32(0, 0, 0, 255));
        device.SwapBuffers();

        Assert.True(device.MarkRecordedWork().IsComplete);
    }

    private static MeshHandle Triangle(GraphicsDevice device) => device.CreateMesh(
        [
            new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
            new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
            new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
        ],
        [0, 1, 2]);
}

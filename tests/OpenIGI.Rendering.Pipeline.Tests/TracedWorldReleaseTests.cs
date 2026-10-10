using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.RayTracing;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That a released traced world is dropped only where no recorded work can still name it.</summary>
public sealed class TracedWorldReleaseTests
{
    [Fact]
    public void A_release_asked_for_mid_frame_waits_for_the_top_of_the_next_frame()
    {
        // Resetting frees every structure, and the idle wait before it covers only what was submitted.
        // Done partway into a frame, the frame's own unsubmitted scene build ran against freed structures
        // and the device was lost on the way from the menu into a mission.
        WorldStandIn world = new();
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60), captureFrames: false, logger: null, tracedWorld: world);

        DrawFrame(device);
        device.ReleaseWorld();
        DrawFrame(device);

        Assert.Equal(0, world.Resets);
        device.SwapBuffers();

        int idleBefore = rhi.WaitIdleCount;
        DrawFrame(device);

        Assert.Equal(1, world.Resets);
        Assert.True(rhi.WaitIdleCount > idleBefore);
        Assert.Equal(0, world.RecordedBeforeReset);
        device.SwapBuffers();

        DrawFrame(device);
        Assert.Equal(1, world.Resets);
        device.SwapBuffers();
    }

    private static void DrawFrame(GraphicsDevice device)
    {
        Bgra32 white = new(255, 255, 255, 255);
        device.Clear(new Bgra32(0, 0, 0, 255));
        device.DrawTriangles2D(
            [new(0, 0, 0, 0, white), new(10, 0, 1, 0, white), new(10, 10, 1, 1, white)],
            [0, 1, 2],
            TextureHandle.None,
            BlendMode.Opaque);
    }

    /// <summary>A traced world that only counts what is done to it.</summary>
    private sealed class WorldStandIn : ITracedWorld
    {
        private int _updates;

        public int Resets { get; private set; }

        /// <summary>Gets how many scene updates the frame that reset had recorded before the reset.</summary>
        public int RecordedBeforeReset { get; private set; }

        public IRhiAccelerationStructure? WorldScene => null;

        public bool IsWorldReady => false;

        public IRhiTextureView TracedAlbedo => throw new NotSupportedException();

        public IRhiAccelerationStructure TraceableScene => throw new NotSupportedException();

        public int PlacementCount => 0;

        public TracedWorldStatistics Statistics => default;

        public ulong HitRecordHandle => 0;

        public IRhiTextureView CutoutMasks => throw new NotSupportedException();

        public void ObserveTexture(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
        {
        }

        public void Observe(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, in TracedSurface surface, RayMask mask)
        {
        }

        public void ObserveCaster(uint meshId, in TracedMeshBuffers mesh, in Matrix4f worldTransform, int indexCount)
        {
        }

        public void Forget(uint meshId)
        {
        }

        public void ForgetTexture(uint textureId)
        {
        }

        public void Reset()
        {
            Resets++;
            RecordedBeforeReset = _updates;
        }

        public void RecordSceneUpdate(IRhiCommandEncoder encoder, RhiTimestampWrites? timestamps = null) => _updates++;
    }
}

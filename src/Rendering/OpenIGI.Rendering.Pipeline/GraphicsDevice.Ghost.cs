using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>A race's ghost, laid over the lit scene before the lens. See <see cref="GhostPass"/>.</summary>
public sealed partial class GraphicsDevice
{
    private readonly List<GhostBodyDraw> _ghostDraws = [];
    private GhostPass? _ghost;
    private GhostLook _ghostLook;

    /// <inheritdoc/>
    /// <remarks>
    /// Taken as the thermal sensor's bodies are (<see cref="DrawThermalBody"/>): the pose as drawn, the unjittered projection.
    /// Not for a shadow: a ghost casts none.
    /// </remarks>
    public void DrawGhost(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, in GhostLook look)
    {
        ThrowIfDisposed();
        if (DrawingShadow || TakeBody(mesh, ranges, modelView) is not { } body)
        {
            return;
        }

        _ghostLook = look;
        _ghostDraws.Add(new GhostBodyDraw(body, new System.Numerics.Vector4(modelView.M31, modelView.M32, modelView.M33, modelView.M34)));
    }

    /// <summary>Lays the frame's ghost over the lit scene, where any of it was drawn.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The lit scene the lens is about to read.</param>
    /// <param name="exposure">The exposure the resolve will apply this frame.</param>
    /// <returns>The picture with the ghost on it, or null where there was none.</returns>
    private IRhiTextureView? RecordGhost(SceneTargetSet scene, IRhiTextureView colour, float exposure)
    {
        if (_ghostDraws.Count == 0)
        {
            return null;
        }

        _ghost ??= new GhostPass(_device, FramesInFlight, _retired);
        (int width, int height) = PostSize(scene);
        _ghost.RecordMask(Encoder(), width, height, _ghostDraws, _ghostLook.Seconds);
        _ghostDraws.Clear();

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "ghost"),
            "ghost"));
        return _ghost.RecordPicture(
            pass,
            colour,
            scene.View(SceneTargetId.Depth),
            new GhostFrame(width, height, scene.Width, scene.Height, _unprojection, exposure),
            _ghostLook,
            FrameSlot);
    }

    private void DisposeGhost()
    {
        _ghost?.Dispose();
        _ghost = null;
        _ghostDraws.Clear();
    }
}

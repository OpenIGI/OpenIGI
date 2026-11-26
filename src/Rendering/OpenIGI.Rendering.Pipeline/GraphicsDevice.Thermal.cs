using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The remastered thermal sensor: the lit scene re-imaged as heat before the lens. See <see cref="ThermalPass"/>.</summary>
public sealed partial class GraphicsDevice
{
    private readonly List<ThermalBodyDraw> _thermalDraws = [];
    private ThermalPass? _thermal;

    /// <inheritdoc/>
    /// <remarks>
    /// Taken as the silhouette's bodies are (<see cref="DrawSilhouette"/>): the pose as drawn, the unjittered projection. Not for
    /// a shadow, which is no picture of anyone.
    /// </remarks>
    public void DrawThermalBody(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, float heat)
    {
        ThrowIfDisposed();
        if (DrawingShadow || TakeBody(mesh, ranges, modelView) is not { } body)
        {
            return;
        }

        _thermalDraws.Add(new ThermalBodyDraw(
            body,
            new System.Numerics.Vector4(modelView.M31, modelView.M32, modelView.M33, modelView.M34),
            heat));
    }

    /// <summary>Re-images the lit scene as heat, where the frame's sensor is up.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The lit scene the lens is about to read.</param>
    /// <param name="post">What the frame asked for.</param>
    /// <param name="exposure">The exposure the resolve will apply this frame.</param>
    /// <returns>The sensor's picture, or null where the sensor is down.</returns>
    /// <remarks>Not for a debug view: a view isolating one term is not a picture a sensor could see.</remarks>
    private IRhiTextureView? RecordThermal(SceneTargetSet scene, IRhiTextureView colour, in PostProcessSettings post, float exposure)
    {
        if (!post.Thermal.IsActive || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            _thermalDraws.Clear();
            return null;
        }

        _thermal ??= new ThermalPass(_device, FramesInFlight, _retired);
        (int width, int height) = PostSize(scene);
        _thermal.RecordHeat(Encoder(), width, height, _thermalDraws);
        _thermalDraws.Clear();

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "thermal"),
            "thermal"));
        return _thermal.RecordPicture(
            pass,
            colour,
            scene.View(SceneTargetId.Depth),
            new ThermalFrame(width, height, scene.Width, scene.Height, _unprojection, exposure, (int)(_frameCounter % ThermalPass.GrainCycle)),
            post.Thermal,
            FrameSlot);
    }

    private void DisposeThermal()
    {
        _thermal?.Dispose();
        _thermal = null;
        _thermalDraws.Clear();
    }
}

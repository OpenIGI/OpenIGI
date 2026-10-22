using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The mods' passes over the lit scene, between the shutter and the lens.</summary>
public sealed partial class GraphicsDevice
{
    private ModPostChain? _modPasses;

    /// <inheritdoc/>
    public void SetPostPasses(IReadOnlyList<PostPassModule> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ThrowIfDisposed();
        if (passes.Count == 0 && _modPasses is null)
        {
            return;
        }

        _modPasses ??= new ModPostChain(_device, FramesInFlight, _retired, _logger ?? NullLogger.Instance);
        _modPasses.Set(passes);
    }

    /// <inheritdoc/>
    public void SetPostPassValues(string name, ReadOnlySpan<float> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        _modPasses?.SetValues(name, values);
    }

    /// <summary>Runs the mods' passes over the picture the lens is about to read.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The picture.</param>
    /// <returns>What the last pass wrote, or null where none ran.</returns>
    /// <remarks>Not for a debug view: a view isolating one term is not the picture a mod's pass was written for.</remarks>
    private IRhiTextureView? RecordModPasses(SceneTargetSet scene, IRhiTextureView colour)
    {
        if (_modPasses is not { IsEmpty: false } passes || _debugView is not (DebugView.None or DebugView.Lighting or DebugView.LampLight))
        {
            return null;
        }

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.CameraEffects, "mods"),
            "mods"));

        (int width, int height) = PostSize(scene);
        return passes.Record(pass, colour, scene.View(SceneTargetId.Depth), width, height, (_unprojection.Near, _unprojection.Far), FrameSlot);
    }
}

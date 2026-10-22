using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The mods' passes: over the lit scene between the shutter and the lens, and over the finished picture between the tone map
/// and the interface.
/// </summary>
/// <remarks>
/// While a pass over the finished picture runs, the tone map draws into an image of its own rather than where the picture is
/// going (the back buffer, or the frame-generation layer); the passes read it, and the last one's image is laid where the
/// picture was going (<c>Post/ModDisplay.slang</c>).
/// </remarks>
public sealed partial class GraphicsDevice
{
    private ModPostChain? _modPasses;
    private ModPostChain? _modDisplayPasses;
    private IRhiTexture? _modDisplaySource;
    private IRhiBindGroupLayout? _modDisplayLayout;
    private ShaderBindings? _modDisplayBindings;
    private readonly Dictionary<RhiTextureFormat, FullscreenPipeline> _modDisplayCopies = [];

    /// <inheritdoc/>
    public void SetPostPasses(IReadOnlyList<PostPassModule> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ThrowIfDisposed();
        PostPassModule[] scene = [.. passes.Where(static p => p.Stage == PostPassStage.Scene)];
        PostPassModule[] display = [.. passes.Where(static p => p.Stage == PostPassStage.Display)];
        if (scene.Length > 0 || _modPasses is not null)
        {
            _modPasses ??= new ModPostChain(_device, FramesInFlight, _retired, _logger ?? NullLogger.Instance, "mods");
            _modPasses.Set(scene);
        }

        if (display.Length > 0 || _modDisplayPasses is not null)
        {
            _modDisplayPasses ??= new ModPostChain(_device, FramesInFlight, _retired, _logger ?? NullLogger.Instance, "mods.display");
            _modDisplayPasses.Set(display);
        }
    }

    /// <inheritdoc/>
    public void SetPostPassValues(string name, ReadOnlySpan<float> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        _modPasses?.SetValues(name, values);
        _modDisplayPasses?.SetValues(name, values);
    }

    // Not for a debug view: a view isolating one term is not the picture a mod's pass was written for.
    private bool ModPassesShow => _debugView is DebugView.None or DebugView.Lighting or DebugView.LampLight;

    /// <summary>Runs the mods' scene passes over the picture the lens is about to read.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="colour">The picture.</param>
    /// <returns>What the last pass wrote, or null where none ran.</returns>
    private IRhiTextureView? RecordModPasses(SceneTargetSet scene, IRhiTextureView colour)
    {
        if (_modPasses is not { IsEmpty: false } passes || !ModPassesShow)
        {
            return null;
        }

        using IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.CameraEffects, "mods"),
            "mods"));
        (int width, int height) = PostSize(scene);
        return passes.Record(pass, colour, scene.View(SceneTargetId.Depth), width, height, (_unprojection.Near, _unprojection.Far), (scene.Width, scene.Height), FrameSlot);
    }

    /// <summary>Where the tone map draws this frame while a mod's pass over the finished picture runs.</summary>
    /// <returns>The image, and the resolve that draws into it; null where no such pass runs.</returns>
    private (IRhiTextureView Target, DeferredResolve Resolve)? BeginModDisplayResolve()
    {
        if (_modDisplayPasses is not { IsEmpty: false } || !ModPassesShow)
        {
            return null;
        }

        int width = Math.Max(1, CurrentMode.Width);
        int height = Math.Max(1, CurrentMode.Height);
        if (_modDisplaySource is null || _modDisplaySource.Width != width || _modDisplaySource.Height != height)
        {
            if (_modDisplaySource is not null)
            {
                _retired.Retire(_modDisplaySource);
            }

            _modDisplaySource = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                DisplayFormat,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
                Label: "mods.display.source"));
        }

        _displayResolve ??= new DeferredResolve(_device, DisplayFormat);
        return (_modDisplaySource.View, _displayResolve);
    }

    /// <summary>Runs the mods' passes over the finished picture and lays the last one's image where the picture was going.</summary>
    /// <param name="scene">The frame's targets, whose depth the passes read.</param>
    /// <param name="target">Where the picture was going.</param>
    /// <param name="format">The target's format.</param>
    private void RecordModDisplayPasses(SceneTargetSet scene, IRhiTextureView target, RhiTextureFormat format)
    {
        if (_modDisplayPasses is not { } passes || _modDisplaySource is not { } source)
        {
            return;
        }

        int width = Math.Max(1, CurrentMode.Width);
        int height = Math.Max(1, CurrentMode.Height);
        IRhiTextureView? picture;
        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.ToneMapGrade, "mods.display"),
            "mods.display")))
        {
            picture = passes.Record(pass, source.View, scene.View(SceneTargetId.Depth), width, height, (_unprojection.Near, _unprojection.Far), (scene.Width, scene.Height), FrameSlot);
        }

        if (_modDisplayLayout is null)
        {
            ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ModDisplay);
            _modDisplayLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
                ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
                "mods.display"));
            _modDisplayBindings = new ShaderBindings(_device, reflection, [_modDisplayLayout], RhiShaderStages.Fragment, "mods.display");
        }

        if (!_modDisplayCopies.TryGetValue(format, out FullscreenPipeline? copy))
        {
            copy = new FullscreenPipeline(
                _device,
                ShaderModuleId.ModDisplay,
                _modDisplayLayout,
                format,
                fragmentConstants: new Dictionary<int, RhiConstant>
                {
                    [Mesh3DConstants.TargetEncodes] = RhiConstant.Of(format.IsSrgb() ? 1 : 0),
                });
            _modDisplayCopies[format] = copy;
        }

        _modDisplayBindings!.Bind("uPicture", picture ?? source.View);
        using IRhiRenderPassEncoder draw = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(target, RhiLoadOp.Clear, RhiStoreOp.Store, _clearColour)],
            Timestamps: _profiler.Time(RenderStage.ToneMapGrade, "mods.display.copy"),
            Label: "mods.display"));
        draw.SetViewport(0, 0, width, height, 0f, 1f);
        draw.SetScissor(0, 0, width, height);
        copy.Draw(draw, _modDisplayBindings.GroupFor(0), []);
        draw.EndPass();
    }

    private void DisposeModPasses()
    {
        _modPasses?.Dispose();
        _modDisplayPasses?.Dispose();
        foreach (FullscreenPipeline copy in _modDisplayCopies.Values)
        {
            copy.Dispose();
        }

        _modDisplayCopies.Clear();
        _modDisplayBindings?.Dispose();
        _modDisplayLayout?.Dispose();
        _modDisplaySource?.Dispose();
    }
}

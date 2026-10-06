using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The picture and the interface drawn apart, and laid together at the end of the frame.</summary>
/// <remarks>
/// <para>
/// Ordinarily the resolve writes the finished picture straight into the back buffer and the interface is
/// drawn over it there. Frame generation needs the two apart: it interpolates the picture without the
/// interface (the "HUD-less" colour) and moves the interface separately, or every menu and readout smears
/// between frames.
/// </para>
/// <para>
/// So when it is wanted the resolve writes an image of its own — <see cref="_display"/> — and
/// everything drawn after it lands in a transparent layer. The sprites blend into that layer over
/// transparent black, which accumulates premultiplied colour and coverage, and laying it over the picture
/// as <c>ui.rgb + (1 − ui.a)·picture</c> gives the pixel the direct path would have drawn. Only additive
/// sprites differ, and they are built to leave coverage alone (see <c>SpritePipelineSet.LayerBlendFor</c>).
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The picture's own format: plain eight-bit RGBA.</summary>
    private const RhiTextureFormat DisplayFormat = RhiTextureFormat.Rgba8Unorm;

    private IRhiTexture? _display;
    private IRhiTexture? _interfaceLayer;
    private DeferredResolve? _displayResolve;
    private FullscreenPipeline? _compose;
    private IRhiBindGroupLayout? _composeLayout;
    private ShaderBindings? _composeBindings;

    // Whether this frame resolved into the picture of its own, so that what is drawn after lands in the
    // layer and the end of the frame lays the two together.
    private bool _layered;
    private bool _layerClearPending;

    /// <summary>Gets a value indicating whether the frame's 2D drawing lands in the interface layer.</summary>
    private bool DrawsIntoLayer => _layered;

    /// <summary>Gets the image the next 2D draw lands in: the interface layer, or the back buffer.</summary>
    private IRhiTextureView FrameDrawTarget => _layered && _interfaceLayer is { } layer ? layer.View : _frame.Target;

    /// <summary>Gets the image the frame's picture is read from by a copy: the picture's own, or the back buffer.</summary>
    private IRhiTexture FrameColourSource => _layered && _display is { } display ? display : _frame.Target.Texture;

    /// <summary>Gets a value indicating whether this frame should resolve into the picture of its own.</summary>
    private bool WantsLayer =>
        RenderCVars.InterfaceLayer.Value || FrameGenerationWantsLayer;

    /// <summary>Gets the picture without the interface, where this frame drew one: what frame generation interpolates.</summary>
    internal IRhiTexture? HudlessColour => _layered ? _display : null;

    /// <summary>Gets the interface layer, where this frame drew one.</summary>
    internal IRhiTexture? InterfaceLayer => _layered ? _interfaceLayer : null;

    /// <summary>Picks the resolve's target for this frame, building the picture's own image when it is needed.</summary>
    /// <returns>The target, and the resolve that draws into its format.</returns>
    private (IRhiTextureView Target, DeferredResolve Resolve)? BeginLayeredResolve()
    {
        if (!WantsLayer || _sceneResolve is null)
        {
            _layered = false;
            return null;
        }

        int width = Math.Max(1, CurrentMode.Width);
        int height = Math.Max(1, CurrentMode.Height);

        if (_display is null || _display.Width != width || _display.Height != height)
        {
            if (_display is not null)
            {
                _retired.Retire(_display);
            }

            // A copy source, for the frame copies that read the picture (the frosted-glass backdrop, a held
            // frame).
            _display = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                DisplayFormat,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled | RhiTextureUsage.CopySource,
                Label: "frame.display"));
        }

        if (_interfaceLayer is null || _interfaceLayer.Width != width || _interfaceLayer.Height != height)
        {
            if (_interfaceLayer is not null)
            {
                _retired.Retire(_interfaceLayer);
            }

            // The back buffer's own format, so every 2D pipeline already built for it draws here unchanged.
            _interfaceLayer = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                PresentFormat,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
                Label: "frame.interface"));
        }

        _displayResolve ??= new DeferredResolve(_device, DisplayFormat);
        _layered = true;
        _layerClearPending = true;
        _sprites.DrawsIntoLayer = true;
        return (_display.View, _displayResolve);
    }

    /// <summary>Lays the interface layer over the picture in the back buffer, where this frame drew them apart.</summary>
    /// <remarks>Before the frame's capture and present, which both read the back buffer.</remarks>
    private void ComposeLayers()
    {
        if (!_layered || _display is not { } display || _interfaceLayer is not { } layer)
        {
            return;
        }

        // A frame whose interface drew nothing still has to lay a cleared layer over the picture.
        if (_layerClearPending)
        {
            Pass();
        }

        ClosePass();

        if (_compose is null)
        {
            ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Compose);
            _composeLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
                ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
                "compose"));
            _compose = new FullscreenPipeline(
                _device,
                ShaderModuleId.Compose,
                _composeLayout,
                PresentFormat,
                fragmentConstants: new Dictionary<int, RhiConstant>
                {
                    [Mesh3DConstants.TargetEncodes] = RhiConstant.Of(PresentFormat.IsSrgb() ? 1 : 0),
                });
            _composeBindings = new ShaderBindings(_device, reflection, [_composeLayout], RhiShaderStages.Fragment, "compose");
        }

        _composeBindings!.Bind("uDisplay", display.View);
        _composeBindings.Bind("uInterface", layer.View);

        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(_frame.Target, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.UserInterface, "compose"),
            Label: "compose")))
        {
            pass.SetViewport(0, 0, CurrentMode.Width, CurrentMode.Height, 0f, 1f);
            pass.SetScissor(0, 0, CurrentMode.Width, CurrentMode.Height);
            _compose.Draw(pass, _composeBindings.GroupFor(0), []);
            pass.EndPass();
        }

        EndLayers();
    }

    /// <summary>Ends the frame's layering, whether or not it was composed.</summary>
    private void EndLayers()
    {
        _layered = false;
        _layerClearPending = false;
        _sprites.DrawsIntoLayer = false;
    }

    private void DisposeLayers()
    {
        _composeBindings?.Dispose();
        _compose?.Dispose();
        _composeLayout?.Dispose();
        _displayResolve?.Dispose();
        _display?.Dispose();
        _interfaceLayer?.Dispose();
    }
}

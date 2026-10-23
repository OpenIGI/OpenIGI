using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The remastered heads-up display drawn apart and laid over the frame as if projected on a visor.</summary>
/// <remarks>
/// <para>
/// Between <see cref="BeginHudLayer"/> and <see cref="ComposeHudLayer"/> the 2D drawing lands in a transparent layer of
/// its own, as the interface layer's does (<c>GraphicsDevice.Layer.cs</c>): every sprite blends into transparent black, which
/// accumulates premultiplied colour and coverage. The compose blurs that layer into its glow with the backdrop's own
/// kernels (<see cref="BackdropBlurPass"/>) and draws it over whatever the interface draws into -- the back buffer, or the
/// interface layer of a frame drawn apart -- through <c>Ui/HudFx.slang</c>, blended as premultiplied "over".
/// </para>
/// <para>
/// The frame's picture is never read here, so the frosted glass under the display's panels, which samples the picture
/// without the interface, is unaffected.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private IRhiTexture? _hudLayer;
    private BackdropBlurPass? _hudGlow;
    private FullscreenPipeline? _hudFx;
    private IRhiBindGroupLayout? _hudFxLayout;
    private ShaderBindings? _hudFxBindings;
    private IRhiSampler? _hudFxSampler;
    private bool _hudActive;
    private bool _hudClearPending;

    /// <inheritdoc/>
    public bool BeginHudLayer()
    {
        ThrowIfDisposed();
        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        if (!_inFrame || _hudActive || width <= 1 || height <= 1)
        {
            return false;
        }

        if (_hudLayer is null || _hudLayer.Width != width || _hudLayer.Height != height)
        {
            if (_hudLayer is not null)
            {
                _retired.Retire(_hudLayer);
            }

            // The back buffer's own format, so every 2D pipeline already built for it draws here unchanged.
            _hudLayer = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                PresentFormat,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
                Label: "frame.hud"));
        }

        // What was being drawn lands where it was going before the target changes.
        ClosePass();
        _hudActive = true;
        _hudClearPending = true;
        _sprites.DrawsIntoLayer = true;
        return true;
    }

    /// <inheritdoc/>
    public void ComposeHudLayer(in HudFxSettings fx)
    {
        ThrowIfDisposed();
        if (!_hudActive || _hudLayer is not { } layer)
        {
            return;
        }

        bool drew = !_hudClearPending;
        ClosePass();
        EndHudLayer();
        if (!drew)
        {
            return;
        }

        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        BuildHudFx();

        // The glow: the layer blurred at the backdrop's widths, of which the finest three are laid back over it.
        _hudGlow ??= new BackdropBlurPass(_device, FramesInFlight, _retired);
        using (IRhiComputePassEncoder blur = Encoder().BeginComputePass(new RhiComputePassDescriptor(Label: "hud.glow")))
        {
            _hudGlow.Record(blur, layer.View, width, height, FrameSlot);
        }

        IReadOnlyList<IRhiTexture> chain = _hudGlow.Chain;
        _hudFxBindings!.Bind("uHud", layer.View);
        _hudFxBindings.Bind("uHudSampler", _hudFxSampler!);
        _hudFxBindings.Bind("uGlow0", chain[0].View);
        _hudFxBindings.Bind("uGlow1", chain[Math.Min(1, chain.Count - 1)].View);
        _hudFxBindings.Bind("uGlow2", chain[Math.Min(2, chain.Count - 1)].View);

        HudFxPush push = HudFxPush.From(fx, width, height);

        // Over whatever the interface draws into, kept: the picture, or the interface layer of a frame drawn apart, which
        // starts transparent if nothing has been drawn into it yet.
        bool clearInterface = DrawsIntoLayer && _layerClearPending;
        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(FrameDrawTarget, clearInterface ? RhiLoadOp.Clear : RhiLoadOp.Load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.UserInterface, "hud.fx"),
            Label: "hud.fx")))
        {
            pass.SetViewport(0, 0, width, height, 0f, 1f);
            pass.SetScissor(0, 0, width, height);
            _hudFx!.Draw(pass, _hudFxBindings.GroupFor(0), MemoryMarshal.AsBytes(new ReadOnlySpan<HudFxPush>(in push)));
            pass.EndPass();
        }

        if (clearInterface)
        {
            _layerClearPending = false;
        }
    }

    /// <summary>Ends the HUD layer, whether or not it was composed: the 2D drawing goes back where it was going.</summary>
    private void EndHudLayer()
    {
        _hudActive = false;
        _hudClearPending = false;
        _sprites.DrawsIntoLayer = _layered;
    }

    private void BuildHudFx()
    {
        if (_hudFx is not null)
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.HudFx);
        _hudFxLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
            "hud.fx"));
        RhiBlendComponent over = new(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add);
        _hudFx = new FullscreenPipeline(_device, ShaderModuleId.HudFx, _hudFxLayout, PresentFormat, blend: new RhiBlendState(over, over));
        _hudFxBindings = new ShaderBindings(_device, reflection, [_hudFxLayout], RhiShaderStages.Fragment, "hud.fx");
        _hudFxSampler = _device.CreateSampler(new RhiSamplerDescriptor(Label: "hud.fx"));
    }

    private void DisposeHudLayer()
    {
        _hudFxBindings?.Dispose();
        _hudFx?.Dispose();
        _hudFxLayout?.Dispose();
        _hudFxSampler?.Dispose();
        _hudGlow?.Dispose();
        _hudLayer?.Dispose();
    }

    /// <summary>The visor pass's push block, as <c>Ui/HudFx.slang</c> lays it out: seven vectors of four.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct HudFxPush
    {
        private readonly float _width, _height, _inverseWidth, _inverseHeight;
        private readonly float _intensity, _curvature, _fringe, _glow;
        private readonly float _scanlines, _scanlineStrength, _appear, _seconds;
        private readonly float _shakeX, _shakeY, _unused0, _unused1;
        private readonly float _topLeft, _topRight, _bottomLeft, _bottomRight;
        private readonly float _red, _green, _blue, _unused2;
        private readonly float _alert, _alertRed, _alertGreen, _alertBlue;

        private HudFxPush(in HudFxSettings fx, int width, int height)
        {
            (_width, _height, _inverseWidth, _inverseHeight) = (width, height, 1f / width, 1f / height);
            (_intensity, _curvature, _fringe, _glow) = (Math.Clamp(fx.Intensity, 0f, 1f), fx.Curvature, fx.FringePixels, fx.Glow);
            (_scanlines, _scanlineStrength, _appear, _seconds) = (fx.ScanlineDensity, fx.ScanlineStrength, fx.Appear, fx.Seconds);
            (_shakeX, _shakeY, _unused0, _unused1) = (fx.Shake.X, fx.Shake.Y, 0f, 0f);
            (_topLeft, _topRight, _bottomLeft, _bottomRight) = fx.Damage;
            (_red, _green, _blue, _unused2) = (fx.DamageColour.R, fx.DamageColour.G, fx.DamageColour.B, 0f);
            (_alert, _alertRed, _alertGreen, _alertBlue) = (fx.Alert, fx.AlertColour.R, fx.AlertColour.G, fx.AlertColour.B);
        }

        public static HudFxPush From(in HudFxSettings fx, int width, int height) => new(fx, width, height);
    }
}

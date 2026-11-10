using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The remastered computer's map put on screen as a one-colour satellite feed.</summary>
/// <remarks>
/// A full-screen pass over whatever the 2D drawing goes to -- the picture, or the interface layer of a frame drawn apart --
/// reading the frame as the glass copies it (<see cref="BlurFrameForBackdrop"/>), sharp and blurred, and writing the feed in
/// its place (<c>Ui/MapFeed.slang</c>). The copy is this pass's own to ask for: the glass drawn after it copies the frame
/// again, and so stands on the feed and not on the picture under it.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private FullscreenPipeline? _mapFeed;
    private IRhiBindGroupLayout? _mapFeedLayout;
    private ShaderBindings? _mapFeedBindings;
    private IRhiSampler? _mapFeedSampler;

    /// <inheritdoc/>
    public bool DrawMapFeed(in MapFeedSettings feed)
    {
        ThrowIfDisposed();
        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        if (!_inFrame || !feed.IsActive || width <= 1 || height <= 1)
        {
            return false;
        }

        BlurFrameForBackdrop();
        if (!_backdropFresh || _backdropSource is not { } source || _backdropBlur is not { Chain.Count: > 0 } blur)
        {
            return false;
        }

        BuildMapFeed();
        IReadOnlyList<IRhiTexture> chain = blur.Chain;
        _mapFeedBindings!.Bind("uScene", source.View);
        _mapFeedBindings.Bind("uSceneSampler", _mapFeedSampler!);
        _mapFeedBindings.Bind("uSceneSoft", chain[Math.Min(1, chain.Count - 1)].View);
        _mapFeedBindings.Bind("uSceneWide", chain[^1].View);

        MapFeedPush push = MapFeedPush.From(feed, width, height, PresentFormat.IsSrgb());
        ClosePass();
        using (IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(FrameDrawTarget, RhiLoadOp.Load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.UserInterface, "map.feed"),
            Label: "map.feed")))
        {
            pass.SetViewport(0, 0, width, height, 0f, 1f);
            pass.SetScissor(0, 0, width, height);
            _mapFeed!.Draw(pass, _mapFeedBindings.GroupFor(0), MemoryMarshal.AsBytes(new ReadOnlySpan<MapFeedPush>(in push)));
            pass.EndPass();
        }

        // The whole target is written: a clear still owed to the interface layer is this pass's own picture.
        if (DrawsIntoLayer && _layerClearPending)
        {
            _layerClearPending = false;
        }

        return true;
    }

    private void BuildMapFeed()
    {
        if (_mapFeed is not null)
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.MapFeed);
        _mapFeedLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
            "map.feed"));
        _mapFeed = new FullscreenPipeline(_device, ShaderModuleId.MapFeed, _mapFeedLayout, PresentFormat);
        _mapFeedBindings = new ShaderBindings(_device, reflection, [_mapFeedLayout], RhiShaderStages.Fragment, "map.feed");
        _mapFeedSampler = _device.CreateSampler(new RhiSamplerDescriptor(Label: "map.feed"));
    }

    private void DisposeMapFeed()
    {
        _mapFeedBindings?.Dispose();
        _mapFeed?.Dispose();
        _mapFeedLayout?.Dispose();
        _mapFeedSampler?.Dispose();
    }

    /// <summary>The feed pass's push block, as <c>Ui/MapFeed.slang</c> lays it out: seven vectors of four.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MapFeedPush
    {
        private readonly float _width, _height, _inverseWidth, _inverseHeight;
        private readonly float _amount, _boot, _sweep, _sweepPosition;
        private readonly float _seconds, _curvature, _scanlines, _scanlineStrength;
        private readonly float _dim, _burst, _target, _grain;
        private readonly float _shadowR, _shadowG, _shadowB, _unused1;
        private readonly float _bodyR, _bodyG, _bodyB, _unused2;
        private readonly float _highlightR, _highlightG, _highlightB, _unused3;

        private MapFeedPush(in MapFeedSettings feed, int width, int height, bool targetEncodes)
        {
            (_width, _height, _inverseWidth, _inverseHeight) = (width, height, 1f / width, 1f / height);
            (_amount, _boot, _sweep, _sweepPosition) = (Math.Clamp(feed.Amount, 0f, 1f), feed.Boot, feed.Sweep, feed.SweepPosition);
            (_seconds, _curvature, _scanlines, _scanlineStrength) = (feed.Seconds, feed.Curvature, feed.ScanlineDensity, feed.ScanlineStrength);
            (_dim, _burst, _target, _grain) = (feed.Dim, feed.Burst, targetEncodes ? 1f : 2f, feed.Grain);
            (_shadowR, _shadowG, _shadowB, _unused1) = (Linear(feed.Shadow.R), Linear(feed.Shadow.G), Linear(feed.Shadow.B), 0f);
            (_bodyR, _bodyG, _bodyB, _unused2) = (Linear(feed.Body.R), Linear(feed.Body.G), Linear(feed.Body.B), 0f);
            (_highlightR, _highlightG, _highlightB, _unused3) = (Linear(feed.Highlight.R), Linear(feed.Highlight.G), Linear(feed.Highlight.B), 0f);
        }

        public static MapFeedPush From(in MapFeedSettings feed, int width, int height, bool targetEncodes) =>
            new(feed, width, height, targetEncodes);

        // A display colour's channel as linear light, which the pass works in.
        private static float Linear(float display)
        {
            float c = Math.Clamp(display, 0f, 1f);
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
    }
}

using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>The frame half of the device: acquiring a frame, presenting it, and the offscreen rotation that stands in for a surface.</summary>
public sealed unsafe partial class WebGpuRhiDevice
{
    private WebGpuTexture? _frameTexture;

    // Where a frame is drawn when the surface's own texture will not last until it is presented: see
    // WebGpuSurface.TexturesLastOneTask. Copied into the surface's texture as the frame is presented.
    // The one the frame being recorded was handed is kept apart, because a resize replaces the texture
    // while that frame may still be drawing into the old one; the old one goes once it has been copied.
    private WebGpuTexture? _drawnFrame;
    private WebGpuTexture? _frameDrawnInto;
    private readonly List<WebGpuTexture> _replacedFrames = [];
    private int _frameIndex;

    /// <inheritdoc/>
    /// <remarks>
    /// The acquired texture is owned by this frame and released when it ends: a surface hands one out
    /// anew each time, and holding one past its present keeps the whole rotation alive.
    /// </remarks>
    public bool BeginFrame(out RhiFrame frame)
    {
        ThrowIfDisposed();
        frame = default;

        if (_offscreen.Count > 0)
        {
            frame = new RhiFrame(
                _offscreen[_frameIndex % _offscreen.Count].View,
                _frameIndex,
                _frameIndex % FramesInFlight);

            return true;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface, so it presents no frames.");

        // The engine presents a frame at the start of the next one, so a frame is recorded across two
        // tasks -- and a canvas's texture does not outlive the first. Drawn here, it is copied into the
        // canvas's texture of the task that presents it (EndFrame).
        if (surface.TexturesLastOneTask)
        {
            _drawnFrame ??= (WebGpuTexture)CreateTexture(new RhiTextureDescriptor(
                surface.Width,
                surface.Height,
                surface.Format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: "frame"));

            _frameDrawnInto = _drawnFrame;
            frame = new RhiFrame(_drawnFrame.View, _frameIndex, _frameIndex % FramesInFlight);
            return true;
        }

        WGPUSurfaceGetCurrentTextureStatus status = surface.Acquire(out WebGpuTexture? texture);

        if (texture is null)
        {
            // Outdated is what a resize looks like from here, and the answer to it is to reconfigure and
            // ask again rather than to report a failure.
            NeedsReconfigure = status is WGPUSurfaceGetCurrentTextureStatus.Outdated
                or WGPUSurfaceGetCurrentTextureStatus.Lost;

            return false;
        }

        // Suboptimal presents correctly and says this configuration has been outgrown, so the frame is
        // drawn and the surface reconfigured after it.
        NeedsReconfigure = status == WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal;

        _frameTexture = texture;
        frame = new RhiFrame(texture.View, _frameIndex, _frameIndex % FramesInFlight);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        ThrowIfDisposed();

        if (_offscreen.Count > 0)
        {
            // Nothing is presented and nothing waits on a display; the work is already submitted.
            _frameIndex++;
            _pushes?.Reset();
            return true;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface, so it presents no frames.");

        if (surface.TexturesLastOneTask)
        {
            CopyIntoSurface(surface);
            _frameDrawnInto = null;

            // Copied, and the copy submitted: nothing will be recorded into these again.
            foreach (WebGpuTexture replaced in _replacedFrames)
            {
                replaced.Dispose();
            }

            _replacedFrames.Clear();
        }

        bool presented = surface.Present();

        _frameTexture?.Dispose();
        _frameTexture = null;
        _frameIndex++;

        // The ring is spent for the frame just recorded and free for the next, which is only true
        // because a frame is presented before the next one is begun.
        _pushes?.Reset();

        return presented && !NeedsReconfigure;
    }

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        ThrowIfDisposed();

        if (_offscreen.Count > 0)
        {
            AttachOffscreen(width, height, _offscreen[0].Format);
            return;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface to configure.");

        surface.Configure(_context.Device, _support, width, height, vsync);
        NeedsReconfigure = false;

        // Made again at the new size by the next frame. The old one may be what the frame being recorded
        // draws into, so it goes once that frame has been copied out of it.
        if (_drawnFrame is { } replaced)
        {
            _replacedFrames.Add(replaced);
            _drawnFrame = null;
        }
    }

    /// <summary>Copies the frame drawn for a surface whose textures last one task into its texture now.</summary>
    private void CopyIntoSurface(WebGpuSurface surface)
    {
        if (_frameDrawnInto is not { } drawn)
        {
            return;
        }

        WGPUSurfaceGetCurrentTextureStatus status = surface.Acquire(out WebGpuTexture? shown);
        if (shown is null)
        {
            NeedsReconfigure = status is WGPUSurfaceGetCurrentTextureStatus.Outdated
                or WGPUSurfaceGetCurrentTextureStatus.Lost;
            return;
        }

        using (shown)
        {
            using IRhiCommandEncoder encoder = CreateCommandEncoder("present");
            encoder.CopyTextureToTexture(
                new RhiTextureRegion(drawn),
                new RhiTextureRegion(shown),
                new RhiExtent3D(Math.Min(drawn.Width, shown.Width), Math.Min(drawn.Height, shown.Height)));
            Submit([encoder.Finish("present")]);
        }
    }

    /// <summary>Gets whether the surface has to be reconfigured before the next frame.</summary>
    /// <remarks>
    /// Set when the surface says its textures are outdated, which is what a resize looks like from here.
    /// The host answers it by configuring again at the new size.
    /// </remarks>
    public bool NeedsReconfigure { get; private set; }

    /// <summary>Gets the surface this presents to, or null for a headless device.</summary>
    public WebGpuSurface? Surface => _surface;

    /// <summary>Gives this device somewhere to draw when there is no surface to present to.</summary>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    /// <remarks>
    /// A rotation of ordinary textures in place of a surface's. Everything above sees the same frame,
    /// and what changes is that nothing is presented — which is what a headless capture wants, and what
    /// lets the whole renderer be run and read back on a machine with no window.
    /// </remarks>
    public void AttachOffscreen(int width, int height, RhiTextureFormat format)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        ReleaseOffscreen();

        for (int i = 0; i < FramesInFlight; i++)
        {
            _offscreen.Add((WebGpuTexture)CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: $"offscreen-{i}")));
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index, as it was handed out.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _offscreen[index % _offscreen.Count];

    private readonly List<WebGpuTexture> _offscreen = [];

    private void ReleaseOffscreen()
    {
        foreach (WebGpuTexture texture in _offscreen)
        {
            texture.Dispose();
        }

        _offscreen.Clear();
    }

    /// <summary>How many frames may be recorded before one completes.</summary>
    /// <remarks>
    /// Two, which is what the per-frame resources are sized for. WebGPU does not expose the surface's
    /// own image count and does not need to: what this number decides is how many sets of host-written
    /// resources exist, not how many images the surface rotates through.
    /// </remarks>
    public const int FramesInFlight = 2;
}

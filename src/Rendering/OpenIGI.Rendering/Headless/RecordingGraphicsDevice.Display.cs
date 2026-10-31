using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>Gets the colours the frame was cleared to, in order.</summary>
    public List<Bgra32> Clears { get; } = [];

    /// <summary>The number of foreground depth resets requested.</summary>
    public int DepthClearCount { get; private set; }

    /// <summary>Gets how many frames were presented.</summary>
    public int SwapCount { get; private set; }

    /// <inheritdoc/>
    public GraphicsCapabilities Capabilities { get; } = new(2, 4096, true, "recording")
    {
        SupportsSceneCapture = true,
        SupportsFloatRenderTargets = true,
        MaxSampleCount = 4,
    };

    /// <inheritdoc/>
    public DisplayMode CurrentMode { get; private set; } = DisplayMode.Default;

    /// <inheritdoc/>
    public bool VSyncEnabled { get; set; }

    /// <inheritdoc/>
    public void SetMode(in DisplayMode mode) => CurrentMode = mode;

    /// <inheritdoc/>
    public void Clear(Bgra32 color) => Record(Clears, color);

    /// <inheritdoc/>
    public void ClearDepth() => DepthClearCount++;

    /// <inheritdoc/>
    public void SwapBuffers() => SwapCount++;

    /// <inheritdoc/>
    public void SetViewport(in Viewport viewport)
    {
    }

    /// <inheritdoc/>
    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
    }

    /// <inheritdoc/>
    public void CaptureFrame(int width, int height, Span<Bgra32> destination) => destination.Clear();

    /// <inheritdoc/>
    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
    }

    /// <inheritdoc/>
    public void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
    }
}

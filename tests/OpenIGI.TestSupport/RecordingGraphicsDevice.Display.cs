using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    public List<Bgra32> Clears { get; } = [];

    /// <summary>The number of foreground depth resets requested.</summary>
    public int DepthClearCount { get; private set; }

    public int SwapCount { get; private set; }

    public GraphicsCapabilities Capabilities { get; } = new(2, 4096, true, "recording")
    {
        SupportsSceneCapture = true,
        SupportsFloatRenderTargets = true,
        MaxSampleCount = 4,
    };

    public DisplayMode CurrentMode { get; private set; } = DisplayMode.Default;

    public bool VSyncEnabled { get; set; }

    public void SetMode(in DisplayMode mode) => CurrentMode = mode;

    public void Clear(Bgra32 color) => Clears.Add(color);

    public void ClearDepth() => DepthClearCount++;

    public void SwapBuffers() => SwapCount++;

    public void SetViewport(in Viewport viewport)
    {
    }

    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
    }

    public void CaptureFrame(int width, int height, Span<Bgra32> destination) => destination.Clear();

    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
    }

    public void DrawLoadingBackdrop(TextureHandle texture, in LoadingBackdropSettings settings)
    {
    }
}

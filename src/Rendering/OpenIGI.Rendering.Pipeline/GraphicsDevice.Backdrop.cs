using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The interface's frosted glass: the frame drawn so far, copied and blurred into a pyramid.</summary>
public sealed partial class GraphicsDevice
{
    private BackdropBlurPass? _backdropBlur;
    private IRhiTexture? _backdropSource;
    private TextureHandle[] _backdropHandles = [];
    private BackdropBlur _backdropResult = BackdropBlur.None;

    /// <inheritdoc/>
    /// <remarks>
    /// The frame is copied into an sRGB texture the size of the screen — so the pyramid built from it holds
    /// linear light — and the bloom kernels blur it into <see cref="BackdropBlurPass.Levels"/> levels in one
    /// compute pass. The levels are adopted into the texture table once per size, so the 2D pass binds them
    /// by handle like any texture. A frame the device cannot read from blurs to nothing, and the glass is
    /// drawn as its tint alone.
    /// </remarks>
    public BackdropBlur BlurFrameForBackdrop()
    {
        ThrowIfDisposed();

        int width = CurrentMode.Width;
        int height = CurrentMode.Height;
        if (!_inFrame || width <= 1 || height <= 1 || !FrameReadable)
        {
            return BackdropBlur.None;
        }

        // Opened and closed, so a clear still pending lands before the copy reads the image.
        Pass();
        ClosePass();

        // The picture as it stands: the back buffer, or under a layered frame the picture's own image -- the
        // interface layer is not composited until the frame ends.
        IRhiTexture frameColour = FrameColourSource;
        RhiTextureFormat format = FrameCopyFormat(frameColour.Format);
        if (_backdropSource is null || _backdropSource.Width != width || _backdropSource.Height != height || _backdropSource.Format != format)
        {
            if (_backdropSource is not null)
            {
                _retired.Retire(_backdropSource);
            }

            _backdropSource = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
                Label: "backdrop.source"));
        }

        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(frameColour),
            new RhiTextureRegion(_backdropSource),
            new RhiExtent3D(Math.Min(width, frameColour.Width), Math.Min(height, frameColour.Height)));

        if (_backdropBlur is null)
        {
            _backdropBlur = new BackdropBlurPass(_device, FramesInFlight, _retired);
            _backdropBlur.Resized += ReleaseBackdropHandles;
        }

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(Label: "backdrop.blur")))
        {
            _backdropBlur.Record(pass, _backdropSource.View, width, height, FrameSlot);
        }

        if (_backdropHandles.Length == 0)
        {
            IReadOnlyList<IRhiTexture> chain = _backdropBlur.Chain;
            _backdropHandles = new TextureHandle[chain.Count];
            var levels = new BackdropLevel[chain.Count];
            for (int level = 0; level < chain.Count; level++)
            {
                _backdropHandles[level] = _textures.Adopt(chain[level], new TextureDescription(chain[level].Width, chain[level].Height));
                levels[level] = new BackdropLevel(_backdropHandles[level], BackdropBlurPass.SigmaOf(level));
            }

            _backdropResult = new BackdropBlur(levels, width, height);
        }

        return _backdropResult;
    }

    /// <summary>Lets go of the pyramid's handles when it is remade at a new size; the table retires the textures.</summary>
    private void ReleaseBackdropHandles()
    {
        foreach (TextureHandle handle in _backdropHandles)
        {
            DestroyTexture(handle);
        }

        _backdropHandles = [];
        _backdropResult = BackdropBlur.None;
    }

    /// <summary>Releases the backdrop's own resources on disposal; the table disposes the adopted levels.</summary>
    private void DisposeBackdrop()
    {
        _backdropBlur?.Dispose();
        _backdropBlur = null;
        _backdropSource?.Dispose();
        _backdropSource = null;
        _backdropHandles = [];
    }
}

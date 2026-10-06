using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Reading a frame back to the host.</summary>
/// <remarks>
/// <para>
/// What a screenshot is, what a save's thumbnail is, and what every capture harness the port has reads.
/// Asked inside a frame, the answer is the frame as drawn so far; asked between frames, it is the frame
/// last presented.
/// </para>
/// <para>
/// The frame last presented cannot be read out of the image it was drawn into: that image belongs to the
/// presentation engine once it has gone out, which may still be scanning it out. So every frame leaves a
/// copy of itself behind, recorded INSIDE the frame after everything has drawn and before it is submitted:
/// </para>
/// <list type="bullet">
/// <item>While somebody keeps asking -- a capture session, a screenshot loop, the renderer switch -- into a
/// buffer the host can read, so a request costs only a wait for the frame to finish.</item>
/// <item>Otherwise into a texture of the device's own, which a request copies out on a submission of its
/// own. One copy of the frame on the GPU is the whole standing cost of a one-off screenshot being
/// possible at all.</item>
/// </list>
/// <para>
/// Exactly one of the two holds the frame last presented, so a request reads whichever that is.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>How many frames the readable copy stays armed after somebody asks for one.</summary>
    private const int CaptureFrames = 3;

    private readonly IRhiBuffer?[] _captureBuffers = new IRhiBuffer?[FramesInFlight];
    private readonly bool _captureAlways;

    private int _captureCountdown;
    private int _captureWidth;
    private int _captureHeight;
    private long _captured = -1;

    private IRhiTexture? _retainedFrame;
    private IRhiBuffer? _retainedReadback;
    private long _retained = -1;

    /// <summary>Gets whether this frame should be copied back as it is submitted.</summary>
    private bool CapturingFrames => _captureAlways || _captureCountdown > 0;

    /// <summary>Gets whether the frame's image can be copied from at all.</summary>
    /// <remarks>
    /// A browser's canvas is readable only where the adapter offers it, and a frame that is not reads as
    /// black rather than as a copy the device would refuse.
    /// </remarks>
    private bool FrameReadable => (_frame.Target.Texture.Usage & RhiTextureUsage.CopySource) != 0;

    /// <inheritdoc/>
    public void CaptureFrame(int width, int height, Span<Bgra32> destination)
    {
        ThrowIfDisposed();

        if (destination.Length < width * height)
        {
            throw new ArgumentException(
                "The destination is shorter than the capture rectangle.",
                nameof(destination));
        }

        Span<Bgra32> target = destination[..(width * height)];
        target.Fill(Bgra32.Black);

        // Asked for, so keep the cheap copy going for a few frames: a menu dissolve and a screenshot loop
        // both ask every frame, and re-arming is cheaper than deciding twice.
        _captureCountdown = CaptureFrames;

        if (_inFrame)
        {
            ReadCurrentFrame(target, width, height);
            return;
        }

        // Every presented frame left itself in exactly one of the two; before the first there is none.
        long lastPresented = (long)_frameCounter - 1;

        if (_captured == lastPresented)
        {
            ReadCapturedFrame(target, width, height);
        }
        else if (_retained == lastPresented)
        {
            ReadRetainedFrame(target, width, height);
        }
    }

    /// <summary>Leaves the frame about to be submitted behind, where a request between frames reads it.</summary>
    /// <remarks>
    /// Recorded with the frame rather than after it: the image is the swapchain's, and once the frame is
    /// presented the presentation engine owns it again.
    /// </remarks>
    private void RecordCapture()
    {
        if (!_inFrame || !FrameReadable)
        {
            return;
        }

        int width = CurrentMode.Width;
        int height = CurrentMode.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        ClosePass();

        if (CapturingFrames)
        {
            CopyFrameToCaptureBuffer(width, height);
        }
        else
        {
            RetainFrame(width, height);
        }
    }

    /// <summary>Copies the frame into this slot's host-readable buffer.</summary>
    private void CopyFrameToCaptureBuffer(int width, int height)
    {
        int slot = FrameSlot;
        int row = RhiBufferTextureCopy.AlignRow(width * 4);
        IRhiBuffer buffer = ReadableFrameBuffer(ref _captureBuffers[slot], row, height, $"capture{slot}");

        Encoder().CopyTextureToBuffer(
            new RhiTextureRegion(_frame.Target.Texture),
            new RhiBufferTextureCopy(buffer, 0, row, height),
            new RhiExtent3D(width, height));

        _captureWidth = width;
        _captureHeight = height;
        _captured = (long)_frameCounter;

        if (_captureCountdown > 0)
        {
            _captureCountdown--;
        }
    }

    /// <summary>Copies the frame into the device's own texture, for a one-off request to read later.</summary>
    private void RetainFrame(int width, int height)
    {
        if (_retainedFrame is not { } retained
            || retained.Width != width
            || retained.Height != height
            || retained.Format != _frame.Target.Texture.Format)
        {
            if (_retainedFrame is { } outgrown)
            {
                _retired.Retire(outgrown);
            }

            retained = _device.CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                _frame.Target.Texture.Format,
                RhiTextureUsage.CopyDestination | RhiTextureUsage.CopySource,
                Label: "capture.retained"));

            _retainedFrame = retained;
        }

        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(_frame.Target.Texture),
            new RhiTextureRegion(retained),
            new RhiExtent3D(width, height));

        _retained = (long)_frameCounter;
    }

    /// <summary>Reads the frame as drawn so far, by flushing it with a copy of its image on the end.</summary>
    private void ReadCurrentFrame(Span<Bgra32> target, int width, int height)
    {
        // A frame drawn with its interface apart has nothing in the back buffer yet: the picture and the
        // interface are laid together only at the frame's end, and until then the image still holds whatever
        // a frame several presents ago left in it. Laid together now instead; anything drawn after the capture
        // then lands directly on top, which is the same picture.
        ComposeLayers();

        int frameWidth = CurrentMode.Width;
        int frameHeight = CurrentMode.Height;

        if (frameWidth <= 0 || frameHeight <= 0 || !FrameReadable)
        {
            return;
        }

        // A clear still pending lands before the copy reads the image. Only when the frame's own pass is
        // what would open next: opening it closes a scene pass or a cascade, which the flush could then
        // no longer pick up again -- and while one of those is open the frame's image is not what the
        // frame is drawing into anyway.
        if (_clearPending && _scenePass is null && _shadowPass is null)
        {
            Pass();
        }

        int row = RhiBufferTextureCopy.AlignRow(frameWidth * 4);
        using IRhiBuffer buffer = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * frameHeight),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "capture.now"));

        FlushFrame(encoder => encoder.CopyTextureToBuffer(
            new RhiTextureRegion(_frame.Target.Texture),
            new RhiBufferTextureCopy(buffer, 0, row, frameHeight),
            new RhiExtent3D(frameWidth, frameHeight)));

        Read(buffer, frameWidth, frameHeight, target, width, height);
    }

    /// <summary>Reads the last presented frame out of the buffer it was copied into as it was submitted.</summary>
    private void ReadCapturedFrame(Span<Bgra32> target, int width, int height)
    {
        int slot = (int)((ulong)_captured % FramesInFlight);

        if (_captureBuffers[slot] is { } buffer)
        {
            Read(buffer, _captureWidth, _captureHeight, target, width, height);
        }
    }

    /// <summary>Reads the last presented frame out of the device's own copy, on a submission of its own.</summary>
    private void ReadRetainedFrame(Span<Bgra32> target, int width, int height)
    {
        if (_retainedFrame is not { } retained)
        {
            return;
        }

        int row = RhiBufferTextureCopy.AlignRow(retained.Width * 4);
        IRhiBuffer buffer = ReadableFrameBuffer(ref _retainedReadback, row, retained.Height, "capture.retained");

        using (IRhiCommandEncoder encoder = _device.CreateCommandEncoder("capture.retained"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(retained),
                new RhiBufferTextureCopy(buffer, 0, row, retained.Height),
                new RhiExtent3D(retained.Width, retained.Height));

            _device.Submit([encoder.Finish("capture.retained")]);
        }

        Read(buffer, retained.Width, retained.Height, target, width, height);
    }

    /// <summary>A host-readable buffer for a frame of this size, remade when the frame outgrows it.</summary>
    private IRhiBuffer ReadableFrameBuffer(ref IRhiBuffer? held, int row, int height, string label)
    {
        ulong bytes = (ulong)(row * height);

        if (held is { } buffer && buffer.SizeInBytes == bytes)
        {
            return buffer;
        }

        if (held is { } outgrown)
        {
            _retired.Retire(outgrown);
        }

        held = _device.CreateBuffer(new RhiBufferDescriptor(
            bytes,
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            label));

        return held;
    }

    /// <summary>Waits for a copied frame and unpacks it, top row first and blue-green-red-alpha.</summary>
    /// <param name="buffer">The buffer the frame was copied into, rows padded to the copy alignment.</param>
    /// <param name="frameWidth">The copied frame's width.</param>
    /// <param name="frameHeight">Its height.</param>
    /// <param name="target">Where to unpack it; what the frame does not cover is left as it is.</param>
    /// <param name="width">The target's width.</param>
    /// <param name="height">Its height.</param>
    private void Read(IRhiBuffer buffer, int frameWidth, int frameHeight, Span<Bgra32> target, int width, int height)
    {
        using IRhiReadback readback = _device.ReadBuffer(buffer);

        // Where the platform will not block, a frame that has not come back yet reads as black.
        if (!readback.Wait(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        ReadOnlySpan<byte> padded = readback.GetData();
        int row = RhiBufferTextureCopy.AlignRow(frameWidth * 4);
        bool swizzle = PresentFormat is RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb;

        int rows = Math.Min(height, frameHeight);
        int columns = Math.Min(width, frameWidth);

        for (int y = 0; y < rows; y++)
        {
            ReadOnlySpan<byte> line = padded.Slice(y * row, columns * 4);

            for (int x = 0; x < columns; x++)
            {
                int at = x * 4;

                // The frame is stored in whatever order the surface chose. A capture is written out as
                // an image, so it is always blue-green-red-alpha by the time it leaves here.
                target[(y * width) + x] = swizzle
                    ? new Bgra32(line[at + 2], line[at + 1], line[at], line[at + 3])
                    : new Bgra32(line[at], line[at + 1], line[at + 2], line[at + 3]);
            }
        }
    }

    private void DisposeCapture()
    {
        for (int i = 0; i < _captureBuffers.Length; i++)
        {
            _captureBuffers[i]?.Dispose();
            _captureBuffers[i] = null;
        }

        _retainedFrame?.Dispose();
        _retainedFrame = null;
        _retainedReadback?.Dispose();
        _retainedReadback = null;
    }
}

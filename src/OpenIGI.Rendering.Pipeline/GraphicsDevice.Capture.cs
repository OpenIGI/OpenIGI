using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Reading a presented frame back to the host.</summary>
/// <remarks>
/// <para>
/// What a screenshot is, what the menu's dissolve holds, and what every capture harness the port has
/// reads. It cannot be done by copying out of the frame's image after the fact: the presentation engine
/// may still be scanning that image out, and the view a frame was drawn through belongs to that frame.
/// So the copy is recorded INSIDE the frame, after everything has drawn and before it is submitted, and
/// the host reads the buffer it landed in once that frame has finished.
/// </para>
/// <para>
/// The copy only happens while somebody is asking. A session started for capture says so and copies
/// every frame; anything else arms a few frames when it first asks, so a caller that keeps asking keeps
/// the cheap path and a caller that asked once stops paying for it.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>How many frames a copy stays armed after somebody asks for one.</summary>
    private const int CaptureFrames = 3;

    private readonly IRhiBuffer?[] _captureBuffers = new IRhiBuffer?[FramesInFlight];
    private readonly bool[] _captureFilled = new bool[FramesInFlight];
    private readonly bool _captureAlways;

    private int _captureCountdown;
    private int _captureWidth;
    private int _captureHeight;
    private long _captured = -1;

    /// <summary>Gets whether this frame should be copied back as it is submitted.</summary>
    private bool CapturingFrames => _captureAlways || _captureCountdown > 0;

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

        // Asked for, so keep copying for a few frames: a menu dissolve and a screenshot loop both ask
        // every frame, and re-arming is cheaper than deciding twice.
        _captureCountdown = CaptureFrames;

        if (_captured < 0 || _captureWidth <= 0 || _captureHeight <= 0)
        {
            // Nothing has been copied yet — the first frame of a session that did not say it would
            // capture. Black rather than stale, and the next frame will have one.
            target.Fill(Bgra32.Black);
            return;
        }

        int slot = (int)((ulong)_captured % FramesInFlight);

        if (_captureBuffers[slot] is not { } buffer || !_captureFilled[slot])
        {
            target.Fill(Bgra32.Black);
            return;
        }

        using IRhiReadback readback = _device.ReadBuffer(buffer);

        if (!readback.Wait(TimeSpan.FromSeconds(5)))
        {
            target.Fill(Bgra32.Black);
            return;
        }

        ReadOnlySpan<byte> padded = readback.GetData();
        int row = RhiBufferTextureCopy.AlignRow(_captureWidth * 4);
        bool swizzle = PresentFormat is RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb;

        int rows = Math.Min(height, _captureHeight);
        int columns = Math.Min(width, _captureWidth);

        target.Fill(Bgra32.Black);

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

    /// <summary>Records the copy that makes this frame readable, if anything is asking.</summary>
    /// <remarks>
    /// Recorded with the frame rather than after it: the image is the swapchain's, and once the frame is
    /// presented the presentation engine owns it again.
    /// </remarks>
    private void RecordCapture()
    {
        if (!CapturingFrames || !_inFrame)
        {
            return;
        }

        int width = CurrentMode.Width;
        int height = CurrentMode.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        int slot = FrameSlot;
        int row = RhiBufferTextureCopy.AlignRow(width * 4);
        ulong bytes = (ulong)(row * height);

        if (_captureBuffers[slot] is not { } buffer
            || buffer.SizeInBytes != bytes
            || _captureWidth != width
            || _captureHeight != height)
        {
            if (_captureBuffers[slot] is { } outgrown)
            {
                _retired.Retire(outgrown);
            }

            buffer = _device.CreateBuffer(new RhiBufferDescriptor(
                bytes,
                RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
                $"capture{slot}"));

            _captureBuffers[slot] = buffer;
            _captureFilled[slot] = false;
        }

        ClosePass();

        Encoder().CopyTextureToBuffer(
            new RhiTextureRegion(_frame.Target.Texture),
            new RhiBufferTextureCopy(buffer, 0, row, height),
            new RhiExtent3D(width, height));

        _captureWidth = width;
        _captureHeight = height;
        _captureFilled[slot] = true;
        _captured = (long)_frameCounter;

        if (_captureCountdown > 0)
        {
            _captureCountdown--;
        }
    }

    private void DisposeCapture()
    {
        for (int i = 0; i < _captureBuffers.Length; i++)
        {
            _captureBuffers[i]?.Dispose();
            _captureBuffers[i] = null;
        }
    }
}

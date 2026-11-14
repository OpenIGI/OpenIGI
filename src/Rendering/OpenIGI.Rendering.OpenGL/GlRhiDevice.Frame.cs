using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Submission, the frame, and presenting it.</summary>
/// <remarks>
/// Submitting and presenting hand the work to the OpenGL thread and return. The caller may record the next frame while
/// that thread replays this one, and no further: a frame waits to be presented until the one before it has been.
/// </remarks>
public sealed partial class GlRhiDevice
{
    private const int FramesInFlight = 2;

    // One frame may be queued behind the one the thread is presenting; the next waits. More would add a frame of input
    // lag each, and buy nothing once the thread is the slower of the two.
    private readonly SemaphoreSlim _framesQueued = new(1, 1);

    private GlTexture? _frame;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameIndex;
    private bool _offscreen;

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        ThrowIfDisposed();
        Thread.ThrowIfFaulted();
        if (commands.Length == 0)
        {
            return;
        }

        // The pushes this submission's draws read, uploaded ahead of it in one write a chunk.
        _pushes?.Flush();
        foreach (Rhi.Push.RhiInstancePushRing ring in _instancePushes.Values)
        {
            ring.Flush();
        }

        GlCommandList[] lists = new GlCommandList[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            lists[i] = ((GlCommandBuffer)commands[i]).Take();
        }

        Thread.Run(() => Execute(lists));
    }

    /// <inheritdoc/>
    public void Flush()
    {
        ThrowIfDisposed();
        Thread.Invoke(Finish);
        Thread.ThrowIfFaulted();
    }

    /// <inheritdoc/>
    public bool BeginFrame(out RhiFrame frame)
    {
        ThrowIfDisposed();
        Thread.ThrowIfFaulted();

        if (_frame is null)
        {
            frame = default;
            return false;
        }

        frame = new RhiFrame(_frame.View, 0, _frameIndex % FramesInFlight);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        ThrowIfDisposed();
        Thread.ThrowIfFaulted();

        GlTexture? shown = _frame;
        bool offscreen = _offscreen;
        (int Width, int Height) source = (_frameWidth, _frameHeight);

        // The window's size is asked for here: the window system answers it only on the thread that made the window.
        (int Width, int Height) window = offscreen ? source : _surface.FramebufferSize;

        _framesQueued.Wait();
        Thread.Run(() =>
        {
            try
            {
                Show(shown, offscreen, source, window);
            }
            finally
            {
                _framesQueued.Release();
            }
        });

        // The rings start over: what this frame pushed was copied out as it was submitted, and OpenGL orders a later
        // write to a buffer after the draws that read it, so nothing waits here.
        _pushes?.Reset();
        foreach (Rhi.Push.RhiInstancePushRing ring in _instancePushes.Values)
        {
            ring.Reset();
        }

        _frameIndex++;
        return offscreen || window == source;
    }

    /// <summary>Draws frames into a texture of the given format that is never shown, for a test to read back.</summary>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What it is drawn in.</param>
    public void AttachOffscreen(int width, int height, RhiTextureFormat format)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        ReleaseSurface();
        _offscreen = true;
        _frameWidth = width;
        _frameHeight = height;
        _frame = CreateFrame(width, height, format, "offscreen");
    }

    /// <summary>Gets the texture an offscreen frame lands in.</summary>
    /// <param name="index">Which frame in flight; there is one texture, which every frame draws into in turn.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) =>
        _offscreen && _frame is not null ? _frame : throw new InvalidOperationException("The device is not drawing offscreen.");

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        ThrowIfDisposed();

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int interval = vsync ? 1 : 0;
        Thread.Run(() => _surface.SetSwapInterval(interval));

        if (_offscreen || (_frame is not null && width == _frameWidth && height == _frameHeight))
        {
            return;
        }

        ReleaseSurface();
        _frameWidth = width;
        _frameHeight = height;
        _frame = CreateFrame(width, height, SurfaceFormat, "frame");
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        if (_disposed)
        {
            return;
        }

        Thread.Invoke(Finish);
    }

    /// <inheritdoc/>
    public IRhiWorkMarker MarkSubmittedWork()
    {
        ThrowIfDisposed();
        return new GlFence(this);
    }

    // Replays a submission. The OpenGL thread only.
    private void Execute(GlCommandList[] lists)
    {
        Executor.Reset();
        foreach (GlCommandList list in lists)
        {
            Executor.Execute(list);
            ReturnCommands(list);
        }

        PollFences();
    }

    // Puts a frame on the window, or, drawing offscreen, makes it finish. The OpenGL thread only.
    private void Show(GlTexture? frame, bool offscreen, (int Width, int Height) source, (int Width, int Height) window)
    {
        if (offscreen)
        {
            // Nothing is shown, so nothing paces the frames: what was drawn is made to finish here instead, as a
            // swap would, so a test reading the target after a frame finds it drawn.
            Gl.Flush();
        }
        else
        {
            if (frame is not null)
            {
                Present(frame, source, window);
            }

            _surface.SwapBuffers();
        }

        PollFences();
    }

    // Waits for everything submitted to finish. The OpenGL thread only.
    private void Finish()
    {
        Gl.Finish();
        PollFences();
    }

    /// <summary>Copies the frame to the window, flipped, since the window's rows run bottom-up.</summary>
    private void Present(GlTexture frame, (int Width, int Height) source, (int Width, int Height) window)
    {
        uint framebuffer = Framebuffers.Single((GlTextureView)frame.View);

        // The frame's bytes are already what the display shows; the window's framebuffer converts nothing.
        Gl.Disable(EnableCap.FramebufferSrgb);
        Gl.Disable(EnableCap.ScissorTest);
        Gl.BindFramebuffer(GLEnum.ReadFramebuffer, framebuffer);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0);

        GLEnum filter = window == source ? GLEnum.Nearest : GLEnum.Linear;
        Gl.BlitFramebuffer(
            0, 0, source.Width, source.Height,
            0, window.Height, window.Width, 0,
            (uint)GLEnum.ColorBufferBit,
            filter);

        Gl.BindFramebuffer(GLEnum.ReadFramebuffer, 0);
        Gl.Enable(EnableCap.FramebufferSrgb);
    }

    private GlTexture CreateFrame(int width, int height, RhiTextureFormat format, string label) => new(this, new RhiTextureDescriptor(
        width,
        height,
        format,
        RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.CopyDestination | RhiTextureUsage.Sampled,
        Label: label));

    private void ReleaseSurface()
    {
        if (_frame is not { } frame)
        {
            return;
        }

        // The view's name is read on the OpenGL thread, where it was given; the frame is let go after, in order.
        Thread.Run(() => State.ForgetTexture(((GlTextureView)frame.View).Name));
        frame.Dispose();
        _frame = null;
    }
}

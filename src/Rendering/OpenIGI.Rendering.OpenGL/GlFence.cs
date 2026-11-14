using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>A sync object after everything submitted so far: the backend's work marker, and what a readback waits on.</summary>
/// <remarks>
/// A sync object may only be asked about on the OpenGL thread, so a caller on another thread reads the answer that
/// thread last found: it polls every outstanding fence after each submission and frame, and whenever it is idle.
/// </remarks>
internal class GlFence : IRhiWorkMarker
{
    private readonly GlRhiDevice _device;
    private nint _sync;
    private volatile bool _signalled;

    public GlFence(GlRhiDevice device)
    {
        _device = device;
        device.Thread.Run(() =>
        {
            _sync = device.Gl.FenceSync(GLEnum.SyncGpuCommandsComplete, 0u);
            device.Track(this);
        });
    }

    public bool IsComplete
    {
        get
        {
            if (!_signalled && _device.Thread.IsOwner)
            {
                Poll(0);
            }

            return _signalled;
        }
    }

    /// <summary>Waits for the fence: on the OpenGL thread by asking the driver, elsewhere by watching the answer.</summary>
    /// <param name="timeout">How long to wait; nought only looks, and a negative span waits as long as it takes.</param>
    /// <returns>Whether it signalled in time.</returns>
    /// <remarks>
    /// A caller on another thread never has the OpenGL thread wait for it: that thread would stop feeding the GPU the
    /// very work the fence is behind. It is told someone is waiting instead, and watches its fences closely until
    /// nobody is.
    /// </remarks>
    public bool Wait(TimeSpan timeout)
    {
        if (_signalled)
        {
            return true;
        }

        if (_device.Thread.IsOwner)
        {
            ulong nanoseconds = timeout < TimeSpan.Zero || timeout.Ticks > (long)(ulong.MaxValue / 100)
                ? ulong.MaxValue
                : (ulong)timeout.Ticks * 100;
            return Poll(nanoseconds);
        }

        if (timeout == TimeSpan.Zero)
        {
            return false;
        }

        using (_device.WaitingOnFences())
        {
            return SpinWait.SpinUntil(() => _signalled, timeout < TimeSpan.Zero ? Timeout.InfiniteTimeSpan : timeout);
        }
    }

    /// <summary>Asks the driver, on the owning thread, and settles the fence when it has signalled.</summary>
    /// <param name="nanoseconds">How long to wait; nought only asks.</param>
    /// <returns>Whether it has signalled.</returns>
    internal bool Poll(ulong nanoseconds)
    {
        if (_signalled)
        {
            return true;
        }

        if (_sync == 0)
        {
            // Not yet made: its creation is held for the owning thread.
            return false;
        }

        GLEnum result = _device.Gl.ClientWaitSync(_sync, (uint)GLEnum.SyncFlushCommandsBit, nanoseconds);
        if (result is not (GLEnum.AlreadySignaled or GLEnum.ConditionSatisfied))
        {
            return false;
        }

        _device.Gl.DeleteSync(_sync);
        _sync = 0;
        OnSignalled();
        _signalled = true;
        return true;
    }

    /// <summary>Runs on the owning thread as the fence signals, before anyone else can see that it has.</summary>
    protected virtual void OnSignalled()
    {
    }
}

/// <summary>A buffer's contents on their way back to the host.</summary>
/// <remarks>Copied out as the fence signals, so <see cref="GetData"/> is a plain read on any thread.</remarks>
internal sealed class GlReadback : GlFence, IRhiReadback
{
    private readonly GlBuffer _buffer;
    private readonly ulong _offset;
    private byte[] _data = [];

    public GlReadback(GlRhiDevice device, GlBuffer buffer, ulong offset, ulong size)
        : base(device)
    {
        _buffer = buffer;
        _offset = offset;
        SizeInBytes = size;
        Device = device;
    }

    public ulong SizeInBytes { get; }

    private GlRhiDevice Device { get; }

    public ReadOnlySpan<byte> GetData() =>
        IsComplete ? _data : throw new InvalidOperationException("The readback has not completed; wait for it first.");

    public void Dispose()
    {
    }

    protected override unsafe void OnSignalled()
    {
        GL gl = Device.Gl;
        byte[] data = new byte[SizeInBytes];
        gl.BindBuffer(GLEnum.CopyReadBuffer, _buffer.Name);
        void* mapped = gl.MapBufferRange(GLEnum.CopyReadBuffer, (nint)_offset, (nuint)SizeInBytes, (uint)GLEnum.MapReadBit);
        if (mapped is not null)
        {
            new ReadOnlySpan<byte>(mapped, data.Length).CopyTo(data);
            gl.UnmapBuffer(GLEnum.CopyReadBuffer);
        }

        _data = data;
    }
}

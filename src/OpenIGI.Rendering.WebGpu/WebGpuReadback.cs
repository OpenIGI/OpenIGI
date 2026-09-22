using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// A buffer's contents on their way back to the host.
/// </summary>
/// <remarks>
/// <para>
/// The map completes through a callback, so this is a future rather than a value even on the desktop
/// where it could have been waited on. <see cref="Wait"/> pumps the instance until the callback fires —
/// never WaitAny, which wgpu-native panics on.
/// </para>
/// <para>
/// The mapped range belongs to the library and is valid only until the buffer is unmapped, which
/// happens when this is disposed. A caller copies out what it means to keep.
/// </para>
/// </remarks>
public sealed unsafe class WebGpuReadback : IRhiReadback
{
    private readonly WebGpuContext _context;
    private readonly WGPUBuffer _buffer;
    private readonly nuint _offset;
    private readonly State _state;
    private GCHandle _handle;
    private bool _mapped;
    private bool _disposed;

    /// <summary>What the callback writes.</summary>
    private sealed class State
    {
        public WGPUMapAsyncStatus Status { get; set; }

        public string Message { get; set; } = string.Empty;

        public bool Completed { get; set; }
    }

    /// <summary>Initialises a new instance of the <see cref="WebGpuReadback"/> class.</summary>
    /// <param name="context">The context, whose event loop the map completes through.</param>
    /// <param name="buffer">The buffer to read, which must be mappable for reading.</param>
    /// <param name="offset">Where the read starts.</param>
    /// <param name="sizeInBytes">How many bytes to read.</param>
    public WebGpuReadback(WebGpuContext context, WebGpuBuffer buffer, ulong offset, ulong sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buffer);

        if (!buffer.Usage.HasFlag(RhiBufferUsage.MapRead))
        {
            throw new InvalidOperationException(
                $"The buffer '{buffer.Label}' was not created to be mapped for reading, so it cannot be "
                + "read back. Copy into one that was.");
        }

        _context = context;
        _buffer = buffer.Handle;
        _offset = (nuint)offset;
        SizeInBytes = sizeInBytes;
        _state = new State();
        _handle = GCHandle.Alloc(_state);

        WGPUBufferMapCallbackInfo info = new()
        {
            mode = WGPUCallbackMode.AllowProcessEvents,
            callback = &OnMapped,
            userdata1 = (void*)GCHandle.ToIntPtr(_handle),
        };

        Wgpu.wgpuBufferMapAsync(_buffer, WGPUMapMode.Read, _offset, (nuint)sizeInBytes, info);
    }

    /// <inheritdoc/>
    public bool IsComplete => _state.Completed;

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <summary>Gets why the map failed, or an empty string.</summary>
    public string Message => _state.Message;

    /// <inheritdoc/>
    public bool Wait(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long deadline = Environment.TickCount64 + (long)Math.Max(0, timeout.TotalMilliseconds);
        bool forever = timeout == Timeout.InfiniteTimeSpan;

        while (!_state.Completed)
        {
            // The queue has to drain before the map can complete, and polling the device is what makes
            // that happen without a frame being presented.
            _context.Poll(wait: false);
            _context.ProcessEvents();

            if (_state.Completed)
            {
                break;
            }

            if (!forever && Environment.TickCount64 > deadline)
            {
                return false;
            }

            Thread.Sleep(0);
        }

        _mapped = _state.Status == WGPUMapAsyncStatus.Success;
        return _mapped;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_state.Completed)
        {
            throw new InvalidOperationException("The readback has not completed.");
        }

        if (_state.Status != WGPUMapAsyncStatus.Success)
        {
            throw new InvalidOperationException($"The readback failed: {_state.Status}. {_state.Message}".TrimEnd());
        }

        void* data = Wgpu.wgpuBufferGetConstMappedRange(_buffer, _offset, (nuint)SizeInBytes);
        if (data is null)
        {
            throw new InvalidOperationException("The readback completed but the range could not be mapped.");
        }

        _mapped = true;
        return new ReadOnlySpan<byte>(data, (int)SizeInBytes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mapped)
        {
            Wgpu.wgpuBufferUnmap(_buffer);
            _mapped = false;
        }

        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnMapped(
        WGPUMapAsyncStatus status,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        if (userdata1 is null
            || GCHandle.FromIntPtr((nint)userdata1).Target is not State state)
        {
            return;
        }

        state.Status = status;
        state.Message = WebGpuString.Read(message);
        state.Completed = true;
    }
}

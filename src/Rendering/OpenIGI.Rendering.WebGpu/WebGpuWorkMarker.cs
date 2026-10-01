using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// The queue's own <c>onSubmittedWorkDone</c>, as a marker the host can poll.
/// </summary>
/// <remarks>
/// The callback frees the handle it was registered with, because it always comes -- cancelled, if the
/// device goes first -- and nothing else knows when it has. Reading <see cref="IsComplete"/> polls the
/// device and pumps the instance once, as a readback's wait does, never WaitAny.
/// </remarks>
public sealed unsafe class WebGpuWorkMarker : IRhiWorkMarker
{
    private readonly WebGpuContext _context;
    private readonly State _state;

    /// <summary>What the callback writes.</summary>
    private sealed class State
    {
        public bool Completed { get; set; }
    }

    /// <summary>Initialises a new instance of the <see cref="WebGpuWorkMarker"/> class.</summary>
    /// <param name="context">The context whose queue this marks.</param>
    public WebGpuWorkMarker(WebGpuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _state = new State();
        GCHandle handle = GCHandle.Alloc(_state);

        WGPUQueueWorkDoneCallbackInfo info = new()
        {
            mode = WGPUCallbackMode.AllowProcessEvents,
            callback = &OnWorkDone,
            userdata1 = (void*)GCHandle.ToIntPtr(handle),
        };

        Wgpu.wgpuQueueOnSubmittedWorkDone(context.Queue, info);
    }

    /// <inheritdoc/>
    public bool IsComplete
    {
        get
        {
            if (!_state.Completed)
            {
                _context.Poll(wait: false);
                _context.ProcessEvents();
            }

            return _state.Completed;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnWorkDone(
        WGPUQueueWorkDoneStatus status,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        if (userdata1 is null)
        {
            return;
        }

        GCHandle handle = GCHandle.FromIntPtr((nint)userdata1);

        // Cancelled counts as done: the queue it marked is gone, and nothing is left to wait for.
        if (handle.Target is State state)
        {
            state.Completed = true;
        }

        handle.Free();
    }
}

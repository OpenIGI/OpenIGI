using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.D3D12.Streamline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>The device's side of Streamline: the capability, and the frame's end it hooks into.</summary>
/// <remarks>
/// What the Vulkan device does at its frame's end, for the same reasons: textures frame generation reads at
/// present time are put back into the state their tags named, the present is bracketed by the latency
/// markers, and frame generation's signal that it has finished with a frame's inputs is waited on by the next
/// submission -- here a queue wait on its fence rather than a semaphore in the submit.
/// </remarks>
internal sealed unsafe partial class D3D12RhiDevice
{
    private StreamlineSession? _streamline;
    private string _streamlineUnavailable = "the NVIDIA DLSS runtime is not installed";
    private nint _frameWait;
    private ulong _frameWaitValue;
    private nint _window;

    /// <inheritdoc/>
    public IRhiStreamline? Streamline => _streamline;

    /// <inheritdoc/>
    public string StreamlineUnavailableReason => _streamline is null ? _streamlineUnavailable : string.Empty;

    /// <summary>Gives this device the Streamline session its device was handed to.</summary>
    /// <param name="streamline">The session.</param>
    internal void AttachStreamline(StreamlineSession streamline)
    {
        ArgumentNullException.ThrowIfNull(streamline);
        _streamline = streamline;
    }

    /// <summary>Detaches the session, ahead of shutting it down.</summary>
    /// <param name="reason">Why there is none now, for the options screen.</param>
    internal void DetachStreamline(string reason)
    {
        _streamline = null;
        _streamlineUnavailable = reason;
    }

    /// <summary>Makes the next submission wait for a fence to reach a value.</summary>
    /// <param name="fence">The <c>ID3D12Fence</c>.</param>
    /// <param name="value">The value.</param>
    internal void AddFrameWait(nint fence, ulong value)
    {
        _frameWait = fence;
        _frameWaitValue = value;
    }

    /// <summary>Rebuilds the swapchain at its current size, running something while none exists.</summary>
    /// <param name="between">What to run between releasing the old swapchain and creating the new one.</param>
    internal void RebuildSwapchain(Action between)
    {
        ArgumentNullException.ThrowIfNull(between);
        if (_swapchain is null)
        {
            throw new GraphicsDeviceException("This device has no swapchain to rebuild.");
        }

        if (_inFrame)
        {
            throw new GraphicsDeviceException("The swapchain cannot be rebuilt in the middle of a frame.");
        }

        Silk.NET.DXGI.SwapChainDesc1 description;
        _swapchain->GetDesc1(&description);
        Context.WaitIdle();
        ReleaseRetired();
        ReleaseSwapchain();
        between();
        AttachSwapchain(_window, (int)description.Width, (int)description.Height, _vsync);
    }

    /// <summary>Makes the queue wait for frame generation's fence, if it handed one over.</summary>
    private void ApplyFrameWait()
    {
        if (_frameWait == 0)
        {
            return;
        }

        D3D12Check.Ok(Context.Queue->Wait((ID3D12Fence*)_frameWait, _frameWaitValue), "ID3D12CommandQueue::Wait");
        _frameWait = 0;
        _frameWaitValue = 0;
    }

    /// <summary>The textures frame generation reads at the present, put back in the state their tags named.</summary>
    /// <param name="encoder">The encoder recorded last before the present.</param>
    private void RestoreTagsForPresent(D3D12RhiCommandEncoder encoder) =>
        _streamline?.PrepareForPresent(texture => D3D12StreamlineAdapter.RestoreForPresent(encoder, texture));
}

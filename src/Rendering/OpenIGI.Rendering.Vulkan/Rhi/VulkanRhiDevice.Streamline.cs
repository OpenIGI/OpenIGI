using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;
using OpenIGI.Rendering.Vulkan.Streamline;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>The device's side of Streamline: the capability, and the frame's end it hooks into.</summary>
/// <remarks>
/// Three things Streamline needs of the frame that nothing else does. Textures frame generation reads at
/// present time are put back into the layout their tags named before the image is presented; the present
/// is bracketed by the latency markers and carries the frame's index as its id; and frame generation's
/// signal that it has finished with a frame's inputs is waited on by the next submit.
/// </remarks>
internal sealed unsafe partial class VulkanRhiDevice
{
    private StreamlineSession? _streamline;
    private VulkanStreamlineAdapter? _streamlineAdapter;

    // A timeline semaphore and value the next frame's submit must wait for, once: frame generation's
    // "inputs consumed" fence. Zero when there is none.
    private Semaphore _frameWait;
    private ulong _frameWaitValue;

    /// <inheritdoc/>
    public IRhiStreamline? Streamline => _streamline;

    /// <inheritdoc/>
    public string StreamlineUnavailableReason => _streamline is null ? "the NVIDIA DLSS runtime is not installed" : string.Empty;

    /// <summary>Gives this device the Streamline session its instance and device were created through.</summary>
    /// <param name="streamline">The capability.</param>
    /// <param name="adapter">Its native side, which puts tagged textures back before the present.</param>
    internal void AttachStreamline(StreamlineSession streamline, VulkanStreamlineAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(streamline);
        ArgumentNullException.ThrowIfNull(adapter);
        _streamline = streamline;
        _streamlineAdapter = adapter;
    }

    /// <summary>Detaches the session, ahead of shutting it down.</summary>
    internal void DetachStreamline()
    {
        _streamline = null;
        _streamlineAdapter = null;
    }

    /// <summary>Rebuilds the swapchain at its current size, running something while none exists.</summary>
    /// <param name="between">What to run between destroying the old swapchain and creating the new one.</param>
    internal void RebuildSwapchain(Action between)
    {
        ArgumentNullException.ThrowIfNull(between);
        VulkanSwapchain swapchain = _swapchain
            ?? throw new GraphicsDeviceException("This device has no swapchain to rebuild.");

        if (_inFrame)
        {
            throw new GraphicsDeviceException("The swapchain cannot be rebuilt in the middle of a frame.");
        }

        _vk.DeviceWaitIdle(_device.Device);

        for (int i = 0; i < _frameTextures.Length; i++)
        {
            RetireFrameTexture(i);
        }

        swapchain.Recreate((int)swapchain.Extent.Width, (int)swapchain.Extent.Height, swapchain.VSync, between);
        CreatePresentSemaphores(swapchain.Images.Length);
        NeedsReconfigure = false;
    }

    /// <summary>Makes the next frame's submit wait for a timeline semaphore to reach a value.</summary>
    /// <param name="semaphore">The <c>VkSemaphore</c> handle, a timeline semaphore.</param>
    /// <param name="value">The value to wait for.</param>
    internal void AddFrameWait(nint semaphore, ulong value)
    {
        if (semaphore == 0 || !HasTimeline)
        {
            return;
        }

        _frameWait = new Semaphore((ulong)semaphore);
        _frameWaitValue = value;
    }

    /// <summary>Takes the pending extra wait, if there is one.</summary>
    /// <param name="semaphore">The semaphore.</param>
    /// <param name="value">The value it must reach.</param>
    /// <returns><see langword="true"/> when there was one.</returns>
    private bool TakeFrameWait(out Semaphore semaphore, out ulong value)
    {
        semaphore = _frameWait;
        value = _frameWaitValue;
        _frameWait = default;
        _frameWaitValue = 0;
        return semaphore.Handle != 0;
    }
}

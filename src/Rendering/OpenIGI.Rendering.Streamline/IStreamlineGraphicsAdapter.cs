using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Streamline;

/// <summary>What Streamline needs of a backend that the RHI does not say: the native side of a session.</summary>
/// <remarks>
/// <para>
/// Small on purpose. Everything a session decides -- which features run, what options and constants are
/// sent, which frame a call belongs to, how failures are reported -- is <see cref="StreamlineSession"/>'s,
/// once, for every API. A backend answers only the questions whose answers are native handles or native
/// synchronisation, so a second API is a second adapter and nothing more.
/// </para>
/// <para>
/// Evaluation happens between passes, never inside one: the RHI's encoders open each pass as an object of
/// their own and bind everything that pass uses when it opens, so no pipeline or descriptor state survives
/// across Streamline's work for it to disturb. That is what lets a session run with Streamline's own state
/// tracking switched off on either API.
/// </para>
/// </remarks>
internal interface IStreamlineGraphicsAdapter
{
    /// <summary>Gets the adapter as Streamline identifies it: a <c>VkPhysicalDevice</c>, or a LUID.</summary>
    /// <remarks>Any pointer it carries stays valid for the adapter's lifetime.</remarks>
    SlAdapterInfo Adapter { get; }

    /// <summary>Gets the native command list an encoder records into.</summary>
    /// <param name="encoder">The encoder, outside any pass.</param>
    /// <returns>The <c>VkCommandBuffer</c> or <c>ID3D12GraphicsCommandList</c>.</returns>
    nint CommandList(IRhiCommandEncoder encoder);

    /// <summary>Puts a texture into the state its tag will name, and describes it in that state.</summary>
    /// <param name="texture">The texture being tagged.</param>
    /// <param name="encoder">The encoder the transition, if one is needed, is recorded into.</param>
    /// <returns>The description Streamline reads the texture through.</returns>
    SlResource PrepareTag(IRhiTexture texture, IRhiCommandEncoder encoder);

    /// <summary>Records a barrier making everything written so far visible to Streamline, or what it wrote to what follows.</summary>
    /// <param name="encoder">The encoder, outside any pass.</param>
    void ExternalBarrier(IRhiCommandEncoder encoder);

    /// <summary>Rebuilds the swapchain at its current size, running something while none exists.</summary>
    /// <param name="between">What to run between releasing the old swapchain and creating the new one.</param>
    void RebuildSwapchain(Action between);

    /// <summary>Makes the next frame's submission wait for a native fence to reach a value.</summary>
    /// <param name="fence">A timeline <c>VkSemaphore</c> or an <c>ID3D12Fence</c>.</param>
    /// <param name="value">The value to wait for.</param>
    void AddFrameWait(nint fence, ulong value);

    /// <summary>Waits until the device has finished everything submitted.</summary>
    void WaitIdle();
}

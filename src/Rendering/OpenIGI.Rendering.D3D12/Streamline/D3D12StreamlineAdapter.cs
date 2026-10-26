using System.Runtime.InteropServices;
using OpenIGI.Rendering.D3D12.Rhi;
using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Streamline;

/// <summary>A Streamline session's native side on a Direct3D 12 device handed over with <c>slSetD3DDevice</c>.</summary>
/// <remarks>
/// <para>
/// Streamline records LEGACY barriers on what it is given, from the state its tag names and back to it, and
/// only the plain common layout has a legacy state of its own. So a tagged texture is moved into that layout
/// -- legacy <c>COMMON</c> -- and its tag says so; the barrier tracker moves it on from there at its next use,
/// as from any other layout. A texture frame generation reads at the present is put back into common before it.
/// </para>
/// <para>
/// Direct3D lets Streamline query a resource for its size and format, so a description carries only the
/// resource and its state.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12StreamlineAdapter : IStreamlineGraphicsAdapter, IDisposable
{
    /// <summary><c>D3D12_RESOURCE_STATE_COMMON</c>.</summary>
    private const uint CommonState = 0;

    private readonly D3D12RhiDevice _device;
    private nint _luid;

    /// <summary>Initialises a new instance of the <see cref="D3D12StreamlineAdapter"/> class.</summary>
    /// <param name="device">The RHI device.</param>
    public D3D12StreamlineAdapter(D3D12RhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;

        // Streamline keeps the pointer for every later support query, so the LUID lives as long as this does.
        _luid = Marshal.AllocHGlobal(8);
        *(Silk.NET.Core.Native.Luid*)_luid = device.Context.AdapterLuid;
        Adapter = SlAdapterInfo.ForLuid(_luid);
    }

    /// <inheritdoc/>
    public SlAdapterInfo Adapter { get; }

    /// <inheritdoc/>
    public nint CommandList(IRhiCommandEncoder encoder) => (nint)((D3D12RhiCommandEncoder)encoder).LendList();

    /// <inheritdoc/>
    public SlResource PrepareTag(IRhiTexture texture, IRhiCommandEncoder encoder)
    {
        D3D12RhiTexture d3d12 = (D3D12RhiTexture)texture;
        D3D12RhiCommandEncoder recorder = (D3D12RhiCommandEncoder)encoder;
        recorder.Use(d3d12, D3D12Use.External);
        recorder.FlushBarriers();
        return Describe(d3d12);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Streamline's own work is invisible to the tracker, so on either side of it everything waits for
    /// everything: what came before is visible to Streamline, and what Streamline wrote to what follows.
    /// </remarks>
    public void ExternalBarrier(IRhiCommandEncoder encoder, string label) => ((D3D12RhiCommandEncoder)encoder).GlobalBarrier();

    /// <inheritdoc/>
    public void RebuildSwapchain(Action between) => _device.RebuildSwapchain(between);

    /// <inheritdoc/>
    public void AddFrameWait(nint fence, ulong value) => _device.AddFrameWait(fence, value);

    /// <inheritdoc/>
    public void WaitIdle() => _device.WaitIdle();

    /// <summary>Puts a texture tagged until the present back into the state its tag named.</summary>
    /// <param name="encoder">The encoder recorded last before the present.</param>
    /// <param name="texture">The texture.</param>
    internal static void RestoreForPresent(D3D12RhiCommandEncoder encoder, IRhiTexture texture)
    {
        encoder.Use((D3D12RhiTexture)texture, D3D12Use.External);
        encoder.FlushBarriers();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_luid != 0)
        {
            Marshal.FreeHGlobal(_luid);
            _luid = 0;
        }
    }

    private static SlResource Describe(D3D12RhiTexture texture)
    {
        SlResource resource = SlResource.Image2D(
            (nint)texture.Resource,
            0,
            CommonState,
            (uint)D3D12Translation.ResourceFormat(texture.Format),
            (uint)texture.Width,
            (uint)texture.Height,
            0);
        resource.MipLevels = (uint)texture.MipLevelCount;
        resource.ArrayLayers = (uint)texture.ArraySlices;
        return resource;
    }
}

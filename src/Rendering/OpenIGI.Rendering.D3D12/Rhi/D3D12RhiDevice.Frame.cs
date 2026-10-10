using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>The frame: a DXGI flip-model swapchain, or a rotation of textures when there is no window.</summary>
/// <remarks>
/// <para>
/// Flip-discard with three buffers and the frame-latency waitable object: a frame begins by waiting until
/// DXGI will take another, which is the lowest-latency pacing the flip model offers and keeps the CPU from
/// queueing frames the display will never show. Tearing is allowed where the display supports it, so a
/// frame without vsync really is unsynchronised.
/// </para>
/// <para>
/// A swapchain's buffers rest in the common layout, which is the present layout; a frame's render pass
/// moves its buffer to render target and back like any other attachment, and a copy reads it from there.
/// </para>
/// </remarks>
internal sealed unsafe partial class D3D12RhiDevice
{
    private const int SwapchainBuffers = 3;

    private readonly ulong[] _slotTickets = new ulong[FramesInFlight];
    private IDXGISwapChain3* _swapchain;
    private nint _latencyWaitable;
    private D3D12RhiTexture?[] _backBuffers = [];
    private D3D12RhiTexture[] _offscreen = [];
    private RhiTextureFormat _surfaceFormat = RhiTextureFormat.Bgra8Unorm;
    private bool _tearing;
    private bool _vsync = true;
    private int _frameSlot;
    private bool _inFrame;

    /// <inheritdoc/>
    public RhiTextureFormat SurfaceFormat => _offscreen.Length > 0 ? _offscreen[0].Format : _surfaceFormat;

    /// <summary>Gets the swapchain, or null when drawing offscreen.</summary>
    public IDXGISwapChain3* Swapchain => _swapchain;

    /// <summary>Gives the device a window to present to.</summary>
    /// <param name="window">The window's handle.</param>
    /// <param name="width">Its client width.</param>
    /// <param name="height">Its client height.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    public void AttachSwapchain(nint window, int width, int height, bool vsync)
    {
        ThrowIfDisposed();
        ReleaseSwapchain();

        int allowTearing = 0;
        IDXGIFactory5* factory5 = null;
        Guid factoryIid = IDXGIFactory5.Guid;
        if (((IUnknown*)Context.Dxgi)->QueryInterface(&factoryIid, (void**)&factory5) >= 0 && factory5 is not null)
        {
            factory5->CheckFeatureSupport(Silk.NET.DXGI.Feature.PresentAllowTearing, &allowTearing, sizeof(int));
            factory5->Release();
        }

        _tearing = allowTearing != 0;
        _vsync = vsync;
        _window = window;
        _surfaceFormat = RhiTextureFormat.Bgra8Unorm;

        SwapChainDesc1 description = new()
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            Format = D3D12Translation.SwapchainFormat(_surfaceFormat),
            SampleDesc = new SampleDesc(1, 0),
            BufferUsage = DXGI.UsageRenderTargetOutput,
            BufferCount = SwapchainBuffers,
            // No stretching: a frame drawn at the old size shows unscaled for the moment a resize takes.
            Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapchainFlags,
        };

        IDXGISwapChain1* swapchain1 = null;
        D3D12Check.Ok(
            Context.Dxgi->CreateSwapChainForHwnd((IUnknown*)Context.Queue, window, &description, null, null, &swapchain1),
            "IDXGIFactory::CreateSwapChainForHwnd");

        // Alt+Enter would switch to exclusive fullscreen behind the window's back; the window does that.
        Context.Dxgi->MakeWindowAssociation(window, 1u << 1);

        IDXGISwapChain3* swapchain = null;
        Guid iid = IDXGISwapChain3.Guid;
        D3D12Check.Ok(((IUnknown*)swapchain1)->QueryInterface(&iid, (void**)&swapchain), "QueryInterface (IDXGISwapChain3)");
        swapchain1->Release();
        _swapchain = swapchain;

        D3D12Check.Ok(_swapchain->SetMaximumFrameLatency(FramesInFlight), "IDXGISwapChain2::SetMaximumFrameLatency");
        _latencyWaitable = (nint)_swapchain->GetFrameLatencyWaitableObject();
        WrapBackBuffers();
    }

    /// <summary>Gives the device somewhere to draw when there is no window.</summary>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    public void AttachOffscreen(int width, int height, RhiTextureFormat format)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        foreach (D3D12RhiTexture texture in _offscreen)
        {
            texture.Dispose();
        }

        _offscreen = new D3D12RhiTexture[FramesInFlight];
        for (int i = 0; i < _offscreen.Length; i++)
        {
            _offscreen[i] = (D3D12RhiTexture)CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: $"offscreen-{i}"));
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index, as it was handed out.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _offscreen[index % _offscreen.Length];

    /// <inheritdoc/>
    public bool BeginFrame(out RhiFrame frame)
    {
        ThrowIfDisposed();
        frame = default;
        if (_inFrame)
        {
            throw new GraphicsDeviceException("A frame is already begun; end it before beginning another.");
        }

        if (_offscreen.Length == 0 && _swapchain is null)
        {
            throw new GraphicsDeviceException("This device has no swapchain, so it presents no frames.");
        }

        // DXGI says when it will take another frame; then the slot's own resources must be free.
        if (_latencyWaitable != 0)
        {
            _ = Kernel32.WaitForSingleObject(_latencyWaitable, 1000);
        }

        Context.WaitFor(_slotTickets[_frameSlot], Timeout.InfiniteTimeSpan);
        ReleaseRetired();
        ReleaseFinishedStaging();

        _inFrame = true;
        if (_offscreen.Length > 0)
        {
            frame = new RhiFrame(_offscreen[_frameSlot].View, _frameSlot, _frameSlot);
            return true;
        }

        // The buffer DXGI hands out next. With Streamline's swapchain in front of ours -- frame generation
        // adds buffers of its own -- that may be one the last wrap never saw, so the buffers are wrapped again.
        int index = (int)_swapchain->GetCurrentBackBufferIndex();
        if (index >= _backBuffers.Length || _backBuffers[index] is null)
        {
            ReleaseBackBuffers();
            WrapBackBuffers();
            if (index >= _backBuffers.Length)
            {
                _inFrame = false;
                throw new GraphicsDeviceException(
                    $"The swapchain handed out buffer {index} and says it has {_backBuffers.Length}.");
            }
        }

        frame = new RhiFrame(_backBuffers[index]!.View, index, _frameSlot);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        ThrowIfDisposed();
        if (!_inFrame)
        {
            throw new GraphicsDeviceException("No frame is begun, so there is none to end.");
        }

        _inFrame = false;
        bool presenting = _swapchain is not null && _offscreen.Length == 0;
        if (presenting)
        {
            // What frame generation reads at the present goes back into the state its tag named, and the
            // buffer into the plain common layout a present wants: both recorded after everything else.
            using D3D12RhiCommandEncoder encoder = (D3D12RhiCommandEncoder)CreateCommandEncoder("present");
            RestoreTagsForPresent(encoder);
            if (_backBuffers[frame.Index] is { } target)
            {
                encoder.Use(target, Sync.D3D12Use.Present);
                encoder.FlushBarriers();
            }

            Submit([encoder.Finish()]);
        }
        else
        {
            Execute([]);
        }

        int presented = 0;
        if (presenting)
        {
            uint flags = !_vsync && _tearing ? PresentAllowTearing : 0u;
            _streamline?.BeforePresent();
            presented = _swapchain->Present(_vsync ? 1u : 0u, flags);
            _streamline?.AfterPresent();
        }

        // Signalled even with nothing recorded, so the slot has something to wait on next time.
        _slotTickets[_frameSlot] = Context.Signal();
        _frameSlot = (_frameSlot + 1) % FramesInFlight;

        if (presented < 0)
        {
            Context.CheckRemoved();
            D3D12Check.Ok(presented, "IDXGISwapChain::Present");
        }

        return true;
    }

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        ThrowIfDisposed();
        Context.WaitIdle();
        ReleaseRetired();

        if (_offscreen.Length > 0)
        {
            AttachOffscreen(width, height, _offscreen[0].Format);
            return;
        }

        if (_swapchain is null)
        {
            throw new GraphicsDeviceException("This device has no swapchain to configure.");
        }

        _vsync = vsync;
        ReleaseBackBuffers();
        D3D12Check.Ok(
            // A count of nought keeps the count the swapchain has, which Streamline may have changed.
            _swapchain->ResizeBuffers(0, (uint)Math.Max(1, width), (uint)Math.Max(1, height), Format.FormatUnknown, SwapchainFlags),
            "IDXGISwapChain::ResizeBuffers");
        WrapBackBuffers();
    }

    private uint SwapchainFlags =>
        (uint)SwapChainFlag.FrameLatencyWaitableObject | (_tearing ? (uint)SwapChainFlag.AllowTearing : 0u);

    /// <summary><c>DXGI_PRESENT_ALLOW_TEARING</c>.</summary>
    private const uint PresentAllowTearing = 0x200;

    private void WrapBackBuffers()
    {
        // As many as the swapchain says it has, which is not always the count it was created with.
        SwapChainDesc1 description;
        _swapchain->GetDesc1(&description);
        _backBuffers = new D3D12RhiTexture?[description.BufferCount];

        for (uint i = 0; i < description.BufferCount; i++)
        {
            ID3D12Resource* buffer = null;
            Guid iid = ID3D12Resource.Guid;
            D3D12Check.Ok(_swapchain->GetBuffer(i, &iid, (void**)&buffer), "IDXGISwapChain::GetBuffer");
            D3D12Names.Set((ID3D12Object*)buffer, $"swapchain-{i}");

            RhiTextureDescriptor descriptor = new(
                (int)description.Width,
                (int)description.Height,
                _surfaceFormat,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.CopyDestination,
                Label: "swapchain");

            // The texture does not own the buffer's reference; it is released with the swapchain's buffers.
            _backBuffers[i] = new D3D12RhiTexture(this, buffer, descriptor, BarrierLayout.Common, owned: false);
        }
    }

    private void ReleaseBackBuffers()
    {
        foreach (D3D12RhiTexture? texture in _backBuffers)
        {
            if (texture is null)
            {
                continue;
            }

            ID3D12Resource* resource = texture.Resource;
            texture.Dispose();

            // The device is idle, and ResizeBuffers wants every reference gone now, not in a frame's time.
            ReleaseRetired();
            ((IUnknown*)resource)->Release();
        }

        _backBuffers = [];
    }

    /// <summary>Releases the swapchain and the offscreen frames, with the device idle.</summary>
    /// <remarks>For a shutdown where whatever hooked the swapchain goes before the device does.</remarks>
    internal void ReleaseFrames()
    {
        if (_disposed)
        {
            return;
        }

        Context.WaitIdle();
        ReleaseRetired();
        ReleaseSwapchain();
    }

    private void ReleaseSwapchain()
    {
        ReleaseBackBuffers();
        foreach (D3D12RhiTexture texture in _offscreen)
        {
            texture.Dispose();
        }

        _offscreen = [];
        if (_latencyWaitable != 0)
        {
            _ = Kernel32.CloseHandle(_latencyWaitable);
            _latencyWaitable = 0;
        }

        D3D12Check.Release(ref _swapchain);
    }
}

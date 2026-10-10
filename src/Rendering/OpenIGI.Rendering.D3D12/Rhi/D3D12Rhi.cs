using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.D3D12.Streamline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Streamline;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Brings up the RHI over Direct3D 12, for a window or for drawing offscreen.</summary>
/// <remarks>
/// The counterpart of <c>VulkanRhi</c>: the application asks for one of these, and everything it hands back
/// is the interface. A machine that cannot run it -- not Windows, no Agility runtime, no adapter at the
/// baseline -- is told with <see cref="D3D12UnavailableException"/>, and the application runs on Vulkan.
/// </remarks>
public sealed unsafe class D3D12Rhi : IDisposable
{
    private static readonly uint[] StreamlineFeatures =
    [
        SlFeature.Dlss,
        SlFeature.DlssRr,
        SlFeature.DlssG,
        SlFeature.Reflex,
        SlFeature.Pcl,
    ];

    private readonly D3D12RhiDevice _rhi;
    private readonly StreamlineRuntime? _streamlineRuntime;
    private readonly StreamlineSession? _streamline;
    private readonly D3D12StreamlineAdapter? _streamlineAdapter;
    private bool _disposed;

    private D3D12Rhi(
        D3D12RhiDevice rhi,
        StreamlineRuntime? streamlineRuntime = null,
        StreamlineSession? streamline = null,
        D3D12StreamlineAdapter? streamlineAdapter = null)
    {
        _rhi = rhi;
        _streamlineRuntime = streamlineRuntime;
        _streamline = streamline;
        _streamlineAdapter = streamlineAdapter;
    }

    /// <summary>Gets the device.</summary>
    public IRhiDevice Device => _rhi;

    /// <summary>Gets how much checking the environment asks for: <c>OPENIGI_D3D12_VALIDATION</c>.</summary>
    /// <remarks><c>1</c> for the debug layer, <c>gbv</c> for GPU-based validation as well.</remarks>
    public static D3D12Validation ValidationFromEnvironment =>
        Environment.GetEnvironmentVariable("OPENIGI_D3D12_VALIDATION")?.Trim().ToUpperInvariant() switch
        {
            "GBV" or "2" => D3D12Validation.GpuBased,
            "1" or "TRUE" or "ON" => D3D12Validation.DebugLayer,
            _ => D3D12Validation.None,
        };

    /// <summary>Creates a device that draws into textures rather than a window.</summary>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What it is drawn in.</param>
    /// <param name="logger">Where it reports.</param>
    /// <param name="validation">How much checking to create it with.</param>
    /// <returns>The device.</returns>
    /// <exception cref="D3D12UnavailableException">This machine cannot run the backend.</exception>
    public static D3D12Rhi CreateOffscreen(
        int width,
        int height,
        RhiTextureFormat format,
        IOpenIgiLogger? logger = null,
        D3D12Validation validation = D3D12Validation.None)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        D3D12RhiDevice rhi = Create(logger, validation);
        try
        {
            rhi.AttachOffscreen(width, height, format);
            return new D3D12Rhi(rhi);
        }
        catch
        {
            rhi.Dispose();
            throw;
        }
    }

    /// <summary>Creates a device that presents to a window.</summary>
    /// <param name="window">The window's handle.</param>
    /// <param name="width">Its client width.</param>
    /// <param name="height">Its client height.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <param name="logger">Where it reports.</param>
    /// <param name="validation">How much checking to create it with.</param>
    /// <param name="streamline">Where NVIDIA's runtime is, or null to run without it.</param>
    /// <returns>The device.</returns>
    /// <exception cref="D3D12UnavailableException">This machine cannot run the backend.</exception>
    public static D3D12Rhi CreateForWindow(
        nint window,
        int width,
        int height,
        bool vsync,
        IOpenIgiLogger? logger = null,
        D3D12Validation validation = D3D12Validation.None,
        StreamlineOptions? streamline = null)
    {
        if (window == 0)
        {
            throw new ArgumentException("A window handle is needed.", nameof(window));
        }

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        // Before the device, as Streamline asks: slInit precedes every DXGI and Direct3D call it may hook.
        StreamlineRuntime? runtime = streamline is null
            ? null
            : StreamlineRuntime.TryCreate(streamline, StreamlineFeatures, log, SlRenderApi.D3D12);

        D3D12RhiDevice rhi;
        try
        {
            rhi = Create(log, validation, context =>
            {
                if (runtime is null)
                {
                    return;
                }

                // The device is handed over, and the factory the swapchain comes from is swapped for the
                // proxy that lets Reflex, PCL and frame generation hook its present.
                SlResult attached = runtime.SetD3DDevice((nint)context.Device);
                if (attached != SlResult.Ok)
                {
                    log.Warning(StreamlineRuntime.LogCategory, $"slSetD3DDevice refused the device: {StreamlineRuntime.Describe(attached)}.");
                    return;
                }

                context.UpgradeDxgi(factory =>
                {
                    SlResult upgraded = runtime.UpgradeInterface(ref factory);
                    if (upgraded != SlResult.Ok)
                    {
                        log.Warning(StreamlineRuntime.LogCategory, $"The DXGI factory was not upgraded ({StreamlineRuntime.Describe(upgraded)}); Reflex and frame generation will not see the present.");
                    }

                    return factory;
                });
            });
        }
        catch
        {
            runtime?.Dispose();
            throw;
        }

        try
        {
            rhi.AttachSwapchain(window, width, height, vsync);

            StreamlineSession? session = null;
            D3D12StreamlineAdapter? adapter = null;
            if (runtime is not null)
            {
                adapter = new D3D12StreamlineAdapter(rhi);

                // Frame generation's plugin goes before the first frame unless it was asked for: while it is
                // loaded the host pays a copy every frame even with it off. The swapchain is rebuilt around it.
                if (streamline is { FrameGeneration: false }
                    && runtime.IsFeatureSupported(SlFeature.DlssG, adapter.Adapter) == SlResult.Ok)
                {
                    _ = runtime.SetFeatureLoaded(SlFeature.DlssG, false);
                }

                session = new StreamlineSession(runtime, adapter, log);
                rhi.AttachStreamline(session);
            }

            return new D3D12Rhi(rhi, runtime, session, adapter);
        }
        catch
        {
            rhi.Dispose();
            runtime?.Dispose();
            throw;
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _rhi.OffscreenTarget(index);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Streamline shuts down before the device it attached to, or NGX faults inside the driver; the
        // session first, then the runtime once the swapchain it hooked is gone.
        _rhi.DetachStreamline("the NVIDIA DLSS runtime was shut down");
        _streamline?.Dispose();
        _rhi.ReleaseFrames();
        _streamlineRuntime?.Dispose();
        _streamlineAdapter?.Dispose();
        _rhi.Dispose();
    }

    private static D3D12RhiDevice Create(IOpenIgiLogger? logger, D3D12Validation validation, Action<D3D12Context>? beforeDevice = null)
    {
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        if (Environment.GetEnvironmentVariable("OPENIGI_D3D12_FORCE_FAIL") is { Length: > 0 })
        {
            throw new D3D12UnavailableException("OPENIGI_D3D12_FORCE_FAIL is set.");
        }

        D3D12Context context = new(log, validation);
        try
        {
            beforeDevice?.Invoke(context);
            return new D3D12RhiDevice(context, log);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}

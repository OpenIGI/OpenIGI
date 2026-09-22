using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>Which window system a native handle belongs to.</summary>
/// <remarks>
/// The four desktop platforms wgpu-native can make a surface from. A browser has none of these — its
/// surface comes from a canvas the page owns — so the web host makes one its own way and nothing here
/// grows a fifth case for it.
/// </remarks>
public enum WebGpuWindowSystem
{
    /// <summary>A Win32 window: an instance and a window handle.</summary>
    Win32,

    /// <summary>An X11 window through Xlib: a display and a window identifier.</summary>
    Xlib,

    /// <summary>A Wayland surface: a display and a surface.</summary>
    Wayland,

    /// <summary>A Metal layer, which is what a Cocoa window's view is backed by.</summary>
    Metal,
}

/// <summary>A window's native handles, as its own platform names them.</summary>
/// <param name="System">Which window system they belong to.</param>
/// <param name="Display">
/// The display, instance or connection: the Win32 module handle, the Xlib display, the Wayland display.
/// Ignored for Metal.
/// </param>
/// <param name="Window">
/// The window itself: the Win32 window handle, the X11 window identifier, the Wayland surface, or the
/// Metal layer.
/// </param>
public readonly record struct WebGpuWindowHandles(WebGpuWindowSystem System, nint Display, nint Window);

/// <summary>
/// The surface a frame is presented to, and the swapchain behind it.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU has no swapchain object. A surface is configured with a size and a format, and each frame asks
/// it for the texture to draw into; the rotation, the image count and the waiting are the
/// implementation's business. That is a smaller interface than Vulkan's and it is the one both backends
/// can be written against.
/// </para>
/// <para>
/// A surface outlives the device it was configured for, because the adapter is chosen to be compatible
/// with it: it has to exist first.
/// </para>
/// </remarks>
public sealed unsafe class WebGpuSurface : IDisposable
{
    private WGPUSurface _handle;
    private WGPUDevice _device;
    private bool _configured;
    private bool _disposed;

    private WebGpuSurface(WGPUSurface handle) => _handle = handle;

    /// <summary>Gets the WebGPU handle.</summary>
    public WGPUSurface Handle => _handle;

    /// <summary>Gets the format the surface was configured with.</summary>
    public RhiTextureFormat Format { get; private set; } = RhiTextureFormat.Undefined;

    /// <summary>Gets the width it was configured at.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height it was configured at.</summary>
    public int Height { get; private set; }

    /// <summary>Makes a surface from a window's native handles.</summary>
    /// <param name="instance">The instance the surface belongs to.</param>
    /// <param name="handles">The window's handles.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="InvalidOperationException">The surface could not be created.</exception>
    public static WebGpuSurface Create(WGPUInstance instance, in WebGpuWindowHandles handles, string label = "")
    {
        using WebGpuString name = new(label);

        // One of four chained structures, chosen by the window system. Each is a different shape and the
        // chain's type tag is what tells wgpu-native which it was handed.
        WGPUSurfaceSourceWindowsHWND win32 = default;
        WGPUSurfaceSourceXlibWindow xlib = default;
        WGPUSurfaceSourceWaylandSurface wayland = default;
        WGPUSurfaceSourceMetalLayer metal = default;
        WGPUChainedStruct* chain;

        switch (handles.System)
        {
            case WebGpuWindowSystem.Win32:
                win32 = new WGPUSurfaceSourceWindowsHWND
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWindowsHWND },
                    hinstance = (void*)handles.Display,
                    hwnd = (void*)handles.Window,
                };
                chain = (WGPUChainedStruct*)&win32;
                break;

            case WebGpuWindowSystem.Xlib:
                xlib = new WGPUSurfaceSourceXlibWindow
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceXlibWindow },
                    display = (void*)handles.Display,
                    window = (ulong)handles.Window,
                };
                chain = (WGPUChainedStruct*)&xlib;
                break;

            case WebGpuWindowSystem.Wayland:
                wayland = new WGPUSurfaceSourceWaylandSurface
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceWaylandSurface },
                    display = (void*)handles.Display,
                    surface = (void*)handles.Window,
                };
                chain = (WGPUChainedStruct*)&wayland;
                break;

            case WebGpuWindowSystem.Metal:
                metal = new WGPUSurfaceSourceMetalLayer
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceSourceMetalLayer },
                    layer = (void*)handles.Window,
                };
                chain = (WGPUChainedStruct*)&metal;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(handles), handles.System, "Not a window system this can make a surface from.");
        }

        WGPUSurfaceDescriptor descriptor = new() { nextInChain = chain, label = name.View };
        WGPUSurface handle = Wgpu.wgpuInstanceCreateSurface(instance, &descriptor);

        return handle.IsValid
            ? new WebGpuSurface(handle)
            : throw new InvalidOperationException(
                $"No WebGPU surface could be made from a {handles.System} window.");
    }

    /// <summary>The formats and present modes the adapter offers for this surface.</summary>
    /// <param name="adapter">The adapter to ask.</param>
    /// <returns>What it offers.</returns>
    public WebGpuSurfaceSupport Describe(WGPUAdapter adapter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        WGPUSurfaceCapabilities capabilities = new();
        if (Wgpu.wgpuSurfaceGetCapabilities(_handle, adapter, &capabilities) != WGPUStatus.Success)
        {
            return new WebGpuSurfaceSupport([], [], WGPUCompositeAlphaMode.Auto);
        }

        List<RhiTextureFormat> formats = [];
        for (nuint i = 0; i < capabilities.formatCount; i++)
        {
            RhiTextureFormat format = WebGpuTranslation.Format(capabilities.formats[i]);
            if (format != RhiTextureFormat.Undefined)
            {
                formats.Add(format);
            }
        }

        List<WGPUPresentMode> modes = [];
        for (nuint i = 0; i < capabilities.presentModeCount; i++)
        {
            modes.Add(capabilities.presentModes[i]);
        }

        WGPUCompositeAlphaMode alpha = capabilities.alphaModeCount > 0
            ? capabilities.alphaModes[0]
            : WGPUCompositeAlphaMode.Auto;

        Wgpu.wgpuSurfaceCapabilitiesFreeMembers(capabilities);
        return new WebGpuSurfaceSupport(formats, modes, alpha);
    }

    /// <summary>Configures the surface for a size and a way of waiting for the display.</summary>
    /// <param name="device">The device that will draw into it.</param>
    /// <param name="support">What the adapter offers.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <exception cref="InvalidOperationException">The adapter offers no format this renderer can present.</exception>
    /// <remarks>
    /// Reconfiguring is how a resize is handled; there is no separate recreate. A size of nought in
    /// either axis is a minimised window, and configuring one is a validation failure, so it is refused
    /// here with a sentence rather than passed through.
    /// </remarks>
    public void Configure(
        WGPUDevice device,
        in WebGpuSurfaceSupport support,
        int width,
        int height,
        bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        RhiTextureFormat format = support.PreferredFormat
            ?? throw new InvalidOperationException(
                "The adapter offers no surface format this renderer knows how to present.");

        WGPUSurfaceConfiguration configuration = new()
        {
            device = device,
            format = WebGpuTranslation.Format(format),
            usage = WGPUTextureUsage.RenderAttachment,
            width = (uint)width,
            height = (uint)height,
            alphaMode = support.AlphaMode,
            presentMode = support.PresentMode(vsync),
        };

        Wgpu.wgpuSurfaceConfigure(_handle, &configuration);

        _device = device;
        _configured = true;
        Format = format;
        Width = width;
        Height = height;
    }

    /// <summary>Takes the texture this frame draws into.</summary>
    /// <param name="texture">The texture, when one was acquired.</param>
    /// <returns>What the surface said about it.</returns>
    /// <remarks>
    /// Outdated is the ordinary answer to a window that has just been resized, and it means reconfigure
    /// and try again rather than anything is wrong. Suboptimal is a frame that will present correctly
    /// and should be the last one at this configuration.
    /// </remarks>
    public WGPUSurfaceGetCurrentTextureStatus Acquire(out WebGpuTexture? texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        texture = null;

        if (!_configured)
        {
            throw new InvalidOperationException("The surface has not been configured, so it has no textures.");
        }

        WGPUSurfaceTexture current = new();
        Wgpu.wgpuSurfaceGetCurrentTexture(_handle, &current);

        if (current.status is not (WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal
            or WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal))
        {
            return current.status;
        }

        texture = new WebGpuTexture(
            current.texture,
            new RhiTextureDescriptor(
                Width,
                Height,
                Format,
                RhiTextureUsage.RenderTarget,
                Label: "surface"),
            Format);

        return current.status;
    }

    /// <summary>Presents what was drawn.</summary>
    /// <returns><see langword="true"/> when it was presented.</returns>
    public bool Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Wgpu.wgpuSurfacePresent(_handle) == WGPUStatus.Success;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle.IsValid)
        {
            if (_configured)
            {
                Wgpu.wgpuSurfaceUnconfigure(_handle);
                _configured = false;
            }

            Wgpu.wgpuSurfaceRelease(_handle);
            _handle = default;
        }

        _device = default;
    }
}

/// <summary>What an adapter offers for one surface.</summary>
/// <param name="Formats">The formats it will present, in the adapter's own order of preference.</param>
/// <param name="PresentModes">The ways of waiting for the display it offers.</param>
/// <param name="AlphaMode">How the surface is composited with what is behind it.</param>
public readonly record struct WebGpuSurfaceSupport(
    IReadOnlyList<RhiTextureFormat> Formats,
    IReadOnlyList<WGPUPresentMode> PresentModes,
    WGPUCompositeAlphaMode AlphaMode)
{
    /// <summary>Gets the format to present in, or null when the adapter offers none this knows.</summary>
    /// <remarks>
    /// <para>
    /// The adapter's first choice, which is the one its own composition path is cheapest for. Not an
    /// sRGB preference either way: what the renderer writes is decided by the view it attaches, and
    /// picking the adapter's own order keeps the present free of a conversion nobody asked for.
    /// </para>
    /// <para>
    /// The null check is not defensive padding. A headless device holds the DEFAULT of this structure,
    /// where both lists are null -- and a positional record struct's property initialisers do not run
    /// for a default value, so there is no way to normalise them at construction. "This device has no
    /// surface" is an ordinary state, not a fault, and it has to read as one.
    /// </para>
    /// </remarks>
    public RhiTextureFormat? PreferredFormat => Formats is { Count: > 0 } ? Formats[0] : null;

    /// <summary>The way of waiting for the display that matches what was asked for.</summary>
    /// <param name="vsync">Whether to wait.</param>
    /// <returns>The present mode to configure.</returns>
    /// <remarks>
    /// Fifo is the only mode WebGPU requires of every implementation, so it is what waiting means and
    /// what anything unavailable falls back to. Not waiting asks for Immediate and settles for Mailbox,
    /// which tears less and is the better of the two where both exist.
    /// </remarks>
    public WGPUPresentMode PresentMode(bool vsync)
    {
        if (vsync || PresentModes is null)
        {
            return WGPUPresentMode.Fifo;
        }

        if (PresentModes.Contains(WGPUPresentMode.Immediate))
        {
            return WGPUPresentMode.Immediate;
        }

        return PresentModes.Contains(WGPUPresentMode.Mailbox)
            ? WGPUPresentMode.Mailbox
            : WGPUPresentMode.Fifo;
    }
}

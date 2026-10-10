namespace OpenIGI.Desktop;

/// <summary>The graphics APIs the renderer can run over on the desktop.</summary>
internal enum RendererBackend
{
    /// <summary>Vulkan: every platform, the default off Windows, and the reference the others are compared with.</summary>
    Vulkan,

    /// <summary>WebGPU through wgpu-native: the browser's API, run on the desktop to compare against Vulkan.</summary>
    WebGpu,

    /// <summary>Direct3D 12: the default on Windows.</summary>
    D3D12,
}

/// <summary>Which backend a game starts on, and which one F10 moves to from the one running.</summary>
/// <remarks>
/// Named as <c>OPENIGI_RENDERER</c> names them. The switch steps through every backend this platform has,
/// so the same view can be compared on each API without restarting the game by hand.
/// </remarks>
internal static class RendererSwitchOrder
{
    /// <summary>The backend a game starts on when <c>OPENIGI_RENDERER</c> names none.</summary>
    /// <remarks>
    /// Direct3D 12 on Windows, Vulkan everywhere else. A Windows machine short of Direct3D 12's baseline still
    /// draws: the composition root falls back to Vulkan and logs why.
    /// </remarks>
    public static RendererBackend Default => OperatingSystem.IsWindows() ? RendererBackend.D3D12 : RendererBackend.Vulkan;

    /// <summary>Reads the backend <c>OPENIGI_RENDERER</c> asks for.</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    /// <returns>The backend it names, or <see cref="Default"/>.</returns>
    /// <remarks>
    /// <c>rhi</c> is read as Vulkan: it named this renderer over Vulkan while another renderer was the
    /// default, and a script that still sets it means the same thing now. Direct3D 12 off Windows is
    /// <see cref="Default"/>, since there is none to draw over.
    /// </remarks>
    public static RendererBackend Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "WEBGPU" => RendererBackend.WebGpu,
        "D3D12" or "DX12" or "DIRECT3D12" when OperatingSystem.IsWindows() => RendererBackend.D3D12,
        "VULKAN" or "RHI" => RendererBackend.Vulkan,
        _ => Default,
    };

    /// <summary>Whether a value of <c>OPENIGI_RENDERER</c> names a backend at all.</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    /// <returns><see langword="true"/> when it is unset or names one.</returns>
    public static bool IsKnown(string? value) =>
        string.IsNullOrEmpty(value)
        || value.Trim().ToUpperInvariant() is "VULKAN" or "RHI" or "WEBGPU" or "D3D12" or "DX12" or "DIRECT3D12";

    /// <summary>The backend to switch to.</summary>
    /// <param name="current">The running one.</param>
    /// <returns>The next one this platform has.</returns>
    public static RendererBackend Next(RendererBackend current) => Next(current, OperatingSystem.IsWindows());

    /// <summary>The backend to switch to, on a platform that does or does not have Direct3D 12.</summary>
    /// <param name="current">The running one.</param>
    /// <param name="hasD3D12">Whether Direct3D 12 is one of the steps.</param>
    /// <returns>The next one.</returns>
    public static RendererBackend Next(RendererBackend current, bool hasD3D12) => current switch
    {
        RendererBackend.D3D12 => RendererBackend.Vulkan,
        RendererBackend.Vulkan => RendererBackend.WebGpu,
        _ => hasD3D12 ? RendererBackend.D3D12 : RendererBackend.Vulkan,
    };

    /// <summary>A backend's name for the log.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>A word.</returns>
    public static string Describe(RendererBackend backend) => backend switch
    {
        RendererBackend.WebGpu => "WebGPU",
        RendererBackend.D3D12 => "Direct3D 12",
        _ => "Vulkan",
    };
}

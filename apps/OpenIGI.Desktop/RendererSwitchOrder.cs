using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Which backend a game starts on, and which one F10 moves to from the one running.</summary>
/// <remarks>
/// Named as <c>OPENIGI_RENDERER</c> and <c>r_renderer</c> name them. The switch steps through every backend
/// this platform has, so the same view can be compared on each API without restarting the game by hand.
/// </remarks>
internal static class RendererSwitchOrder
{
    /// <summary>Gets the backend a game starts on when nothing names one.</summary>
    /// <remarks>See <see cref="RendererBackends.Default"/>.</remarks>
    public static RendererBackend Default => RendererBackends.Default;

    /// <summary>Reads the backend <c>OPENIGI_RENDERER</c> or <c>r_renderer</c> asks for.</summary>
    /// <param name="value">The value, or null when nothing set it.</param>
    /// <returns>The backend it names, or <see cref="Default"/>.</returns>
    /// <remarks>
    /// <c>rhi</c> is read as Vulkan: it named this renderer over Vulkan while another renderer was the
    /// default, and a script that still sets it means the same thing now. Direct3D 12 off Windows is
    /// <see cref="Default"/>, since there is none to draw over, and so is <c>auto</c>.
    /// </remarks>
    public static RendererBackend Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "WEBGPU" => RendererBackend.WebGpu,
        "D3D12" or "DX12" or "DIRECT3D12" => RendererBackends.Resolve(RendererBackend.D3D12),
        "VULKAN" or "RHI" => RendererBackend.Vulkan,
        _ => Default,
    };

    /// <summary>Whether a value of <c>OPENIGI_RENDERER</c> or <c>r_renderer</c> names a backend at all.</summary>
    /// <param name="value">The value, or null when nothing set it.</param>
    /// <returns><see langword="true"/> when it is unset or names one.</returns>
    public static bool IsKnown(string? value) =>
        string.IsNullOrEmpty(value)
        || value.Trim().ToUpperInvariant() is "AUTO" or "VULKAN" or "RHI" or "WEBGPU" or "D3D12" or "DX12" or "DIRECT3D12";

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
        RendererBackend.Vulkan => "Vulkan",
        _ => Describe(Default),
    };
}

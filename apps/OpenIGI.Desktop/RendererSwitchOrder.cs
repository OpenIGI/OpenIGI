namespace OpenIGI.Desktop;

/// <summary>The graphics APIs the renderer can run over on the desktop.</summary>
internal enum RendererBackend
{
    /// <summary>Vulkan, the default.</summary>
    Vulkan,

    /// <summary>WebGPU through wgpu-native: the browser's API, run on the desktop to compare against Vulkan.</summary>
    WebGpu,
}

/// <summary>Which backend a game starts on, and which one F10 moves to from the one running.</summary>
/// <remarks>
/// Named as <c>OPENIGI_RENDERER</c> names them. The switch toggles between the two, so the same view can be
/// compared on both APIs without restarting the game by hand.
/// </remarks>
internal static class RendererSwitchOrder
{
    /// <summary>Reads the backend <c>OPENIGI_RENDERER</c> asks for.</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    /// <returns>WebGPU when it says so, Vulkan otherwise.</returns>
    /// <remarks>
    /// <c>rhi</c> is read as Vulkan: it named this renderer over Vulkan while another renderer was the
    /// default, and a script that still sets it means the same thing now.
    /// </remarks>
    public static RendererBackend Parse(string? value) =>
        string.Equals(value, "webgpu", StringComparison.OrdinalIgnoreCase)
            ? RendererBackend.WebGpu
            : RendererBackend.Vulkan;

    /// <summary>Whether a value of <c>OPENIGI_RENDERER</c> names a backend at all.</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    /// <returns><see langword="true"/> when it is unset or names one.</returns>
    public static bool IsKnown(string? value) =>
        string.IsNullOrEmpty(value)
        || string.Equals(value, "vulkan", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "rhi", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "webgpu", StringComparison.OrdinalIgnoreCase);

    /// <summary>The backend to switch to.</summary>
    /// <param name="current">The running one.</param>
    /// <returns>The other one.</returns>
    public static RendererBackend Next(RendererBackend current) =>
        current == RendererBackend.Vulkan ? RendererBackend.WebGpu : RendererBackend.Vulkan;

    /// <summary>A backend's name for the log.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>A word.</returns>
    public static string Describe(RendererBackend backend) =>
        backend == RendererBackend.WebGpu ? "WebGPU" : "Vulkan";
}

namespace OpenIGI.Rendering;

/// <summary>The graphics APIs the renderer can run over on the desktop.</summary>
/// <remarks>
/// Named as <c>OPENIGI_RENDERER</c> and <c>r_renderer</c> name them. Declared here, beside the settings,
/// so the setting the player chooses and the backend the composition root builds are one type.
/// </remarks>
public enum RendererBackend
{
    /// <summary>Whichever this platform draws best over: Direct3D 12 on Windows, Vulkan elsewhere.</summary>
    Auto,

    /// <summary>Direct3D 12: the default on Windows.</summary>
    D3D12,

    /// <summary>Vulkan: every platform, the default off Windows, and the reference the others are compared with.</summary>
    Vulkan,

    /// <summary>WebGPU through wgpu-native: the browser's API, run on the desktop to compare against Vulkan.</summary>
    WebGpu,
}

/// <summary>What <see cref="RendererBackend.Auto"/> and a backend this platform lacks come to.</summary>
public static class RendererBackends
{
    /// <summary>Gets the backend a game starts on when nothing names one.</summary>
    /// <remarks>
    /// Direct3D 12 on Windows, Vulkan everywhere else. A Windows machine short of Direct3D 12's baseline still
    /// draws: the composition root falls back to Vulkan and logs why.
    /// </remarks>
    public static RendererBackend Default => OperatingSystem.IsWindows() ? RendererBackend.D3D12 : RendererBackend.Vulkan;

    /// <summary>The backend a choice draws over on this platform.</summary>
    /// <param name="choice">The choice.</param>
    /// <returns><see cref="Default"/> for automatic, or for Direct3D 12 off Windows; otherwise the choice.</returns>
    public static RendererBackend Resolve(RendererBackend choice) =>
        choice == RendererBackend.Auto || (choice == RendererBackend.D3D12 && !OperatingSystem.IsWindows())
            ? Default
            : choice;
}

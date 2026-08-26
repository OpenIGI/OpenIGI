namespace OpenIGI.Platform;

/// <summary>Which graphics API a window is created for, and a device is built against.</summary>
/// <remarks>
/// The window's context is chosen before it exists — an OpenGL window carries a GL context, a Vulkan
/// window carries a presentable surface instead — so the backend cannot be decided after the fact. It
/// travels with <see cref="WindowSettings"/> so the one place that creates the window also knows which
/// to make.
/// </remarks>
public enum GraphicsBackend
{
    /// <summary>The OpenGL backend, the legacy path every platform ran on before Vulkan.</summary>
    OpenGL,

    /// <summary>The Vulkan backend: the explicit-API path, the hardware ray tracing it enables, and
    /// what the desktop shell boots on unless asked otherwise.</summary>
    Vulkan,
}

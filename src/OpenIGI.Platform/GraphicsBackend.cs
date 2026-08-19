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
    /// <summary>The OpenGL backend. The default, and what every platform has run on so far.</summary>
    OpenGL,

    /// <summary>The Vulkan backend, for the explicit-API path and the hardware ray tracing it enables.</summary>
    Vulkan,
}

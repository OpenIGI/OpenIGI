namespace OpenIGI.Rendering.OpenGL;

/// <summary>The window-system side of an OpenGL context: what the backend asks of whoever made it.</summary>
/// <remarks>
/// <para>
/// The context belongs to the window, and the window belongs to the platform layer, so the backend is handed this
/// rather than a window: it loads the entry points through it, presents through it, and asks it the size of the
/// default framebuffer. The desktop platform implements it over GLFW; a test harness over a hidden window.
/// </para>
/// <para>
/// A context is current on one thread at a time. The device is made on a thread with the context current, and then
/// moves the context to a thread of its own, which issues every call from there on: the surface is asked to make it
/// current, swap and set the interval only on that thread, and for its size only on the thread that made the device.
/// </para>
/// </remarks>
public interface IGlSurface
{
    /// <summary>Finds an OpenGL entry point by name.</summary>
    /// <param name="name">The function's name, such as <c>glClipControl</c>.</param>
    /// <returns>Its address, or nought where the driver has none.</returns>
    nint GetProcAddress(string name);

    /// <summary>Makes the context current on the calling thread.</summary>
    void MakeCurrent();

    /// <summary>Lets the context go from the calling thread, so another may make it current.</summary>
    void ReleaseCurrent();

    /// <summary>Presents the default framebuffer.</summary>
    void SwapBuffers();

    /// <summary>Sets how many vertical blanks a present waits for: one for vsync, nought for none.</summary>
    /// <param name="interval">The interval.</param>
    void SetSwapInterval(int interval);

    /// <summary>Gets the default framebuffer's size in pixels, which a window's size in points may not be.</summary>
    (int Width, int Height) FramebufferSize { get; }
}

using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering.OpenGL;
using Silk.NET.Core.Contexts;

namespace OpenIGI.Desktop;

/// <summary>The OpenGL renderer's view of a desktop window made with an OpenGL context.</summary>
/// <param name="window">The window, made with <see cref="Platform.WindowGraphics.OpenGL"/>.</param>
internal sealed class SilkGlSurface(SilkGameWindow window) : IGlSurface
{
    private readonly IGLContext _context = window.GLContext
        ?? throw new InvalidOperationException("The window was made without an OpenGL context.");

    /// <inheritdoc/>
    public (int Width, int Height) FramebufferSize => window.FramebufferSize;

    /// <inheritdoc/>
    public nint GetProcAddress(string name) => _context.TryGetProcAddress(name, out nint address) ? address : 0;

    /// <inheritdoc/>
    public void MakeCurrent() => _context.MakeCurrent();

    /// <inheritdoc/>
    public void ReleaseCurrent() => _context.Clear();

    /// <inheritdoc/>
    public void SwapBuffers() => _context.SwapBuffers();

    /// <inheritdoc/>
    public void SetSwapInterval(int interval) => _context.SwapInterval(interval);
}

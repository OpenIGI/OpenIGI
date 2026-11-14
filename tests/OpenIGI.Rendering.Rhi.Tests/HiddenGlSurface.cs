using OpenIGI.Rendering.OpenGL;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>An OpenGL 4.3 core context on a window that is never shown, for drawing offscreen in a test.</summary>
/// <remarks>
/// OpenGL has no context without a window system behind it, so the test opens the smallest window it can and keeps it
/// hidden. On a Linux runner with no GPU, Mesa's llvmpipe gives the same context in software.
/// </remarks>
internal sealed class HiddenGlSurface : IGlSurface, IDisposable
{
    private readonly IWindow _window;
    private readonly IGLContext _context;

    public HiddenGlSurface()
    {
        GlfwWindowing.RegisterPlatform();
        _window = Window.Create(WindowOptions.Default with
        {
            Title = "OpenIGI OpenGL test",
            Size = new Vector2D<int>(16, 16),
            IsVisible = false,
            ShouldSwapAutomatically = false,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible | (GlRhiDevice.ValidationFromEnvironment ? ContextFlags.Debug : ContextFlags.Default),
                new APIVersion(4, 3)),
        });

        try
        {
            _window.Initialize();
        }
        catch
        {
            _window.Dispose();
            throw;
        }

        _context = _window.GLContext ?? throw new InvalidOperationException("The hidden window has no OpenGL context.");
    }

    public (int Width, int Height) FramebufferSize => (_window.FramebufferSize.X, _window.FramebufferSize.Y);

    public nint GetProcAddress(string name) => _context.TryGetProcAddress(name, out nint address) ? address : 0;

    public void MakeCurrent() => _context.MakeCurrent();

    public void ReleaseCurrent() => _context.Clear();

    public void SwapBuffers() => _context.SwapBuffers();

    public void SetSwapInterval(int interval) => _context.SwapInterval(interval);

    public void Dispose() => _window.Dispose();
}

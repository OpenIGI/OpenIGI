using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using SkiaSharp;

namespace OpenIGI.Launcher;

/// <summary>
/// Hands Skia the OpenGL entry points of the window's context, and only the EGL ones an EGL context really has.
/// </summary>
/// <remarks>
/// <para>
/// Skia asks for <c>eglQueryString</c> and <c>eglGetCurrentDisplay</c> whatever the context is, and calls them
/// whenever the lookup returns an address. On X11 the window's context is GLX, and <c>glXGetProcAddress</c>
/// returns a non-null do-nothing stub for every name it is asked for, EGL names included. Skia then took the
/// stub's leftover return value as the EGL extension string and read through it, which crashed the launcher
/// before its window appeared (issues #26 and #61). Any driver crashed, software rendering included.
/// </para>
/// <para>
/// Wayland's contexts are EGL, where the same names resolve to the real functions. So an EGL name is looked
/// up only while an EGL context is current. Otherwise Skia is told it is missing, which is how Windows and macOS
/// already answer.
/// </para>
/// </remarks>
internal static class SkiaGlProcs
{
    /// <summary>Builds Skia's interface to the context current on this thread, or null when Skia cannot.</summary>
    public static GRGlInterface? CreateInterface(IGLContext context)
    {
        bool egl = EglContextCurrent();

        // Skia expects null for an entry point the context lacks. Silk's GetProcAddress throws instead, so the
        // lookup goes through the non-throwing form.
        return GRGlInterface.Create(name => Resolve(name, egl, n => context.TryGetProcAddress(n, out nint address) ? address : 0));
    }

    /// <summary>The address Skia is given for <paramref name="name"/>: an EGL name only while EGL is current.</summary>
    internal static nint Resolve(string name, bool eglCurrent, Func<string, nint> lookup) =>
        !eglCurrent && name.StartsWith("egl", StringComparison.Ordinal) ? 0 : lookup(name);

    /// <summary>Whether an EGL context is current on this thread.</summary>
    /// <remarks>
    /// Asked of the system's EGL library itself, since a GLX lookup answers for EGL names too. GLFW's EGL
    /// contexts are only on Linux and the BSDs, and elsewhere the answer is no without loading anything.
    /// </remarks>
    internal static unsafe bool EglContextCurrent()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            return false;
        }

        if (!NativeLibrary.TryLoad("libEGL.so.1", out nint library) && !NativeLibrary.TryLoad("libEGL.so", out library))
        {
            return false;
        }

        try
        {
            return NativeLibrary.TryGetExport(library, "eglGetCurrentContext", out nint getCurrentContext)
                && ((delegate* unmanaged<nint>)getCurrentContext)() != 0;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}

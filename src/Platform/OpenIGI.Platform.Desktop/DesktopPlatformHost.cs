namespace OpenIGI.Platform.Desktop;

/// <summary>
/// The Windows, macOS and Linux platform host: Silk.NET windowing with an OpenGL 3.3 core context.
/// </summary>
public sealed class DesktopPlatformHost : IPlatformHost
{
    /// <inheritdoc/>
    public string Name => "Desktop (Silk.NET)";

    /// <inheritdoc/>
    /// <returns>A <see cref="SilkGameWindow"/>, already initialised so its GL context can be used.</returns>
    /// <remarks>
    /// A zero width or height is not an error: it means "whatever the monitor is", which is what
    /// borderless always uses and what the other modes fall back to. A negative one is a caller bug.
    /// </remarks>
    public IGameWindow CreateWindow(in WindowSettings settings)
    {
        if (settings.Width < 0 || settings.Height < 0)
        {
            throw new PlatformException($"Window size {settings.Width}x{settings.Height} is negative.");
        }

        return new SilkGameWindow(settings);
    }

    /// <inheritdoc/>
    /// <remarks>A sleep for the coarse part of the wait and a spin for the rest; see <see cref="DesktopFramePacer"/>.</remarks>
    public IFramePacer CreateFramePacer() => new DesktopFramePacer();
}

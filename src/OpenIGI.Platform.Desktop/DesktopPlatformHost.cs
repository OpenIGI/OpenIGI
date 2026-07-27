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
    public IGameWindow CreateWindow(in WindowSettings settings)
    {
        if (settings.Width <= 0 || settings.Height <= 0)
        {
            throw new PlatformException($"Window size {settings.Width}x{settings.Height} is not positive.");
        }

        return new SilkGameWindow(settings);
    }
}

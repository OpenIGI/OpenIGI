namespace OpenIGI.Platform;

/// <summary>
/// Creates the windows a platform can host.
/// </summary>
/// <remarks>
/// One implementation per platform family — desktop, mobile, web. The composition root picks one and
/// nothing above it knows which.
/// </remarks>
public interface IPlatformHost
{
    /// <summary>Gets a human-readable name for the platform, for logs and the options screen.</summary>
    string Name { get; }

    /// <summary>Creates a window.</summary>
    /// <param name="settings">How the window should be created.</param>
    /// <returns>The new window, not yet visible. Call <see cref="IGameWindow.Show"/> to display it.</returns>
    /// <exception cref="PlatformException">The window could not be created.</exception>
    IGameWindow CreateWindow(in WindowSettings settings);

    /// <summary>Creates the pacer a loop on this platform holds itself to a rate with.</summary>
    /// <returns>A pacer that waits, or one that never does where the platform paces the loop itself.</returns>
    IFramePacer CreateFramePacer();
}

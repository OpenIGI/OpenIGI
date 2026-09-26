namespace OpenIGI.Platform;

/// <summary>Holds a loop to a frame rate, where the platform leaves that to the loop.</summary>
/// <remarks>
/// A desktop loop renders as fast as it is let, and a screen that changes no faster than the display
/// shows it -- the menu -- is held to the display's rate by waiting out each frame's remainder. A browser
/// paces every frame itself, from its animation callback, and a loop that also waited would halve its
/// own rate at best and hang the page at worst; there the pacer does nothing.
/// </remarks>
public interface IFramePacer
{
    /// <summary>Waits until the current frame's interval has elapsed, then schedules the next.</summary>
    /// <param name="targetHertz">The rate to hold to; nothing happens at or below zero.</param>
    void Pace(int targetHertz);

    /// <summary>Forgets the cadence, so the next <see cref="Pace"/> starts fresh rather than catching up.</summary>
    void Reset();
}

/// <summary>A pacer for a platform that paces the loop itself: it never waits.</summary>
public sealed class NoOpFramePacer : IFramePacer
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static NoOpFramePacer Instance { get; } = new();

    /// <inheritdoc/>
    public void Pace(int targetHertz)
    {
    }

    /// <inheritdoc/>
    public void Reset()
    {
    }
}

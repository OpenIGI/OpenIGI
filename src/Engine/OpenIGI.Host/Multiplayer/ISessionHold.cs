namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// A session that holds a level everyone goes on to until everyone is there: loaded together, it is begun together, so its
/// opening scene plays on every screen at once.
/// </summary>
public interface ISessionHold
{
    /// <summary>Gets whether the level now loaded is to be held, still behind the loading screen, until the others are there.</summary>
    bool HoldsLevel { get; }

    /// <summary>Gets whether nobody sits at this end to play or choose: a dedicated server's, whose own player is nobody's.</summary>
    bool Unattended => false;

    /// <summary>Tells the others the mission this end has begun loading, for them to load alongside it: the host's to say.</summary>
    /// <param name="map">The mission's map.</param>
    void AnnounceGoing(string map)
    {
    }

    /// <summary>Tells the others the mission likely next, for those whose files differ to fetch them while they play: the host's to say.</summary>
    /// <param name="map">The mission's map.</param>
    void Foresee(string map)
    {
    }

    /// <summary>Keeps the session talking on a tick the held level doesn't run: what came is read, what waits is sent.</summary>
    /// <param name="title">What would have ticked, or <see langword="null"/> between levels.</param>
    void Pump(object? title);
}

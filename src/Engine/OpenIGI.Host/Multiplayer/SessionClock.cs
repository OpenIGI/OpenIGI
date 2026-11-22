using System.Diagnostics;

namespace OpenIGI.Host.Multiplayer;

/// <summary>The time a session keeps: the wall's, unless it is given another -- a lockstep run's frames, which its line keeps too.</summary>
internal sealed class SessionClock
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();

    /// <summary>Gets or sets what tells the time in seconds in the wall's place; <see langword="null"/> for the wall's.</summary>
    public Func<double>? Source { get; set; }

    /// <summary>Gets the time since the session began.</summary>
    public TimeSpan Elapsed => Source is { } source ? TimeSpan.FromSeconds(source()) : _wall.Elapsed;
}

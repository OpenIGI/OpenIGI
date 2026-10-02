namespace OpenIGI.Launcher.Services;

/// <summary>When the launcher asks the update service again.</summary>
/// <remarks>
/// <para>
/// Every launcher that is open asks once a minute; an unchanged answer is a bodiless 304, so a check costs a
/// few hundred bytes. Two things keep that gentle on the service. A few seconds of random jitter on every
/// interval stops launchers that started together (after an outage, say) from asking in lockstep forever. And
/// a launcher that cannot reach the service backs off, doubling up to a quarter of an hour, rather than
/// hammering a service that is down.
/// </para>
/// <para>
/// Coming back to the window asks at once, which is when a player expects to see what came out meanwhile,
/// but never more often than every few seconds however fast the window is switched to and from.
/// </para>
/// </remarks>
internal sealed class PollSchedule(Func<double> random)
{
    /// <summary>The interval while the service answers.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>The longest wait after failures.</summary>
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(15);

    /// <summary>The shortest time between two checks the window asked for.</summary>
    public static readonly TimeSpan MinimumSpacing = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Jitter = TimeSpan.FromSeconds(10);

    private DateTime _lastCheck = DateTime.MinValue;

    /// <summary>Initializes a new instance of the <see cref="PollSchedule"/> class with a shared random source.</summary>
    public PollSchedule()
        : this(Random.Shared.NextDouble)
    {
    }

    /// <summary>Gets when the next check is due.</summary>
    public DateTime Next { get; private set; } = DateTime.MinValue;

    /// <summary>Gets how many checks in a row have failed.</summary>
    public int Failures { get; private set; }

    /// <summary>Records that a check is starting.</summary>
    /// <param name="now">The current time.</param>
    public void Started(DateTime now) => _lastCheck = now;

    /// <summary>Records a check's outcome and schedules the next one.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="succeeded">Whether the service answered.</param>
    public void Finished(DateTime now, bool succeeded)
    {
        Failures = succeeded ? 0 : Failures + 1;
        TimeSpan wait = Failures == 0
            ? Interval
            : TimeSpan.FromTicks(Math.Min(MaximumBackoff.Ticks, Interval.Ticks << Math.Min(Failures - 1, 10)));
        Next = now + wait + (Jitter * random());
    }

    /// <summary>Asks for a check as soon as the spacing allows.</summary>
    /// <param name="now">The current time.</param>
    public void Soon(DateTime now)
    {
        DateTime earliest = _lastCheck + MinimumSpacing;
        if (earliest < Next)
        {
            Next = earliest > now ? earliest : now;
        }
    }
}

/// <summary>Which updates the player has already been told about, so each is announced once.</summary>
/// <remarks>
/// The versions are kept in the settings, so a launcher restarted (or a machine rebooted) does not announce
/// the same release again; a newer one is announced as it is seen.
/// </remarks>
internal static class UpdateNotices
{
    /// <summary>Decides whether a version should be announced, and remembers that it has been.</summary>
    /// <param name="settings">The settings holding what was announced.</param>
    /// <param name="key">What the version is of: <c>game</c> or <c>launcher</c>.</param>
    /// <param name="version">The version now available.</param>
    /// <returns>Whether to show a notification.</returns>
    public static bool ShouldAnnounce(LauncherSettings settings, string key, string version)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Notifications
            || (settings.Announced.TryGetValue(key, out string? told) && string.Equals(told, version, StringComparison.Ordinal)))
        {
            return false;
        }

        settings.Announced[key] = version;
        return true;
    }
}

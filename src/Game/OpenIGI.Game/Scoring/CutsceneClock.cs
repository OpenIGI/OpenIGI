namespace OpenIGI.Game.Scoring;

/// <summary>
/// The ticks of a mission its cutscenes hold, which its time leaves out: every tick of a scene no one can skip, and of one that
/// can be skipped, only as long as skipping it takes. A scene watched past that is the player's own choice, and their time: the
/// level does not stand still under every scene, and a scene watched for nothing would let it go on for free.
/// </summary>
public sealed class CutsceneClock
{
    /// <summary>
    /// The ticks a skippable scene is held for nothing: a second, the skip's hold (0.9 s) and a little over, so a player who skips
    /// at once loses no time to a race, which skips with no hold.
    /// </summary>
    public const int SkipAllowanceTicks = 30;

    // The ticks the scene playing has been skippable for.
    private int _skippable;

    /// <summary>Gets or sets the ticks held so far.</summary>
    public long Held { get; set; }

    /// <summary>Counts one of the level's ticks.</summary>
    /// <param name="playing">Whether a cutscene played on it.</param>
    /// <param name="skippable">Whether the player could skip it.</param>
    /// <returns>Whether the tick was held: one the mission's time leaves out.</returns>
    public bool Tick(bool playing, bool skippable)
    {
        if (!playing)
        {
            _skippable = 0;
            return false;
        }

        bool held = !skippable || _skippable++ < SkipAllowanceTicks;
        Held += held ? 1 : 0;
        return held;
    }

    /// <summary>Starts the count again, as a level does.</summary>
    public void Reset()
    {
        Held = 0;
        _skippable = 0;
    }
}

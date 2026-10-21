namespace OpenIGI.Engine.Tasks;

/// <summary>
/// The root of a loaded level's subtree, and the owner of the clock level logic reads.
/// </summary>
/// <remarks>
/// <para>
/// The original registers this as <c>Level</c> with an instance size of 56 (<c>0x4EE970</c>); its update
/// method is <c>0x4F0580</c> and <c>Level_Load</c> (<c>0x4F0D40</c>) writes the timing fields.
/// </para>
/// <para>
/// This clock is not the frame arbiter's. The arbiter schedules ticks against an exact
/// <c>1000.0 / tickRate</c> deadline; the level advances its own elapsed time by the <em>truncated</em>
/// <see cref="MillisecondsPerTick"/>, which at 30 Hz is 33 rather than 33.333. Level time therefore runs
/// about 1 % slow, and every timer expressed in it inherits that. It is a property of the original and
/// has to be kept.
/// </para>
/// </remarks>
public class LevelTask : GameTask
{
    /// <summary>Gets the simulation rate the level was loaded at, in hertz.</summary>
    /// <remarks>Zero until a level load sets it, matching the zero-initialized original instance.</remarks>
    public int TickRate { get; private set; }

    /// <summary>Gets the whole number of milliseconds each tick adds to <see cref="ElapsedMilliseconds"/>.</summary>
    /// <remarks><c>1000 / tickRate</c> in integer arithmetic — 33 at 30 Hz, not 33.333.</remarks>
    public int MillisecondsPerTick { get; private set; }

    /// <summary>Gets the level's own elapsed time, in milliseconds.</summary>
    public int ElapsedMilliseconds { get; private set; }

    /// <summary>Gets the number of ticks the level has run.</summary>
    public int TickCount { get; private set; }

    /// <summary>Gets or sets a value indicating whether the level's subtree and clock are frozen.</summary>
    /// <remarks>
    /// <para>
    /// The pause menu's half of the pause. The original's in-game task (<c>0x416260</c>) forwards the
    /// update to its subtree only while the field at <c>+224</c> is clear, so opening the menu leaves
    /// everything under it — the scripts, the doors, the timers — exactly where it was. This port has no
    /// in-game task of its own, so the gate lives on the level root, which is what that subtree hangs from.
    /// </para>
    /// <para>
    /// The frame schedule is deliberately untouched: ticks keep arriving and doing nothing, which is what
    /// stops the clock building a backlog the first frame after the menu closes would have to catch up on.
    /// </para>
    /// </remarks>
    public bool IsPaused { get; set; }

    /// <summary>Sets the level's tick rate and restarts its clock, as the end of a level load does.</summary>
    /// <param name="tickRate">The simulation rate in hertz.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    public void ResetClock(int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        ElapsedMilliseconds = 0;
        TickCount = 0;
        TickRate = tickRate;
        MillisecondsPerTick = 1000 / tickRate;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The original updates the children first and advances the clock afterwards, so a task reading the
    /// level clock during its own update sees the previous tick's value.
    /// </remarks>
    protected override void OnUpdate()
    {
        if (IsPaused)
        {
            // Nothing is forwarded and the clock is not advanced, so a paused level is a level that is
            // not being ticked at all rather than one being ticked with nothing to do.
            return;
        }

        ForwardToChildren(TaskMessage.Update);

        // The original writes through the global level-root pointer rather than through `self`, so a
        // second Level task would advance the first one's counters twice. Writing through `this` is the
        // only sane reading of the intent and is indistinguishable while one level is loaded at a time.
        ElapsedMilliseconds = unchecked(ElapsedMilliseconds + MillisecondsPerTick);
        TickCount++;
    }
}

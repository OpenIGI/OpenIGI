namespace OpenIGI.Game.World;

/// <summary>
/// A minefield's wait between blasts, <c>MineField_Run</c> in both games (Project I.G.I.'s <c>0x43ABC0</c>): while it waits it
/// counts down and does nothing; once it has waited, its condition is asked, and a blast starts the wait again.
/// </summary>
/// <remarks>
/// A wait of <c>-1</c> is for ever. The "explode once" setting of <c>-1</c> seconds makes a wait of <c>-30</c> ticks, which counts
/// down past nought without ever reaching it, so it is for ever too.
/// </remarks>
public sealed class MineFieldPause
{
    /// <summary>Gets or sets the ticks left to wait.</summary>
    public int Ticks { get; set; }

    /// <summary>Takes one tick: whether the minefield goes off now.</summary>
    /// <param name="condition">Its condition, asked only once it has waited.</param>
    /// <returns><see langword="true"/> when it has waited and its condition holds.</returns>
    public bool Due(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (Ticks == -1)
        {
            return false;
        }

        if (Ticks != 0)
        {
            Ticks--;
            return false;
        }

        return condition();
    }
}

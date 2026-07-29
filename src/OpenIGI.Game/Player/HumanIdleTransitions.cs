namespace OpenIGI.Game.Player;

/// <summary>
/// The transitions out of locomotion state 0 — the machine's initial state.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x4087B0</c>, the per-tick function state 0's entry installs. It is nothing but an ordered
/// chain of flag tests, each of which does the same five things: record the previous state, set the new
/// one, install its function, and <b>call that function immediately</b> — a transition takes effect
/// within the same tick, not the next.
/// </para>
/// <para>
/// Order is the whole specification: losing the ground beats everything, death beats movement, and the
/// jump edge is tested near the end.
/// </para>
/// </remarks>
public static class HumanIdleTransitions
{
    /// <summary>The state a human with no ground under it falls into.</summary>
    public const int Falling = 29;

    /// <summary>The state the jump edge leads to.</summary>
    public const int Jump = 28;

    /// <summary>The state <see cref="HumanMotionState.Turn"/> leads to.</summary>
    public const int Turn = 19;

    /// <summary>The value <see cref="Next"/> returns when nothing fires.</summary>
    public const int Stay = 0;

    /// <summary>Picks the state to move to from idle.</summary>
    /// <param name="flags">The machine's flag word.</param>
    /// <param name="jumpEdge">
    /// Whether input slot 11 rose this tick — the original tests
    /// <c>current[11] != 0 &amp;&amp; previous[11] == 0</c> directly on the human's two input frames.
    /// </param>
    /// <returns>The new state, or <see cref="Stay"/> to remain idle.</returns>
    /// <remarks>
    /// The order below is the original's, and it matters: a human that has just lost the ground never
    /// gets to jump, and a dying one never gets to move.
    /// </remarks>
    public static int Next(HumanMotionState flags, bool jumpEdge)
    {
        if ((flags & HumanMotionState.OnGround) == 0)
        {
            return Falling;
        }

        if ((flags & HumanMotionState.Death46) != 0)
        {
            return 46;
        }

        if ((flags & HumanMotionState.Death47) != 0)
        {
            return 47;
        }

        bool crouched = (flags & HumanMotionState.Crouched) != 0;

        // The four directions are tested in the same order either way; only the target changes.
        if ((flags & HumanMotionState.MoveForward) != 0)
        {
            return crouched ? 9 : 1;
        }

        if ((flags & HumanMotionState.MoveBackward) != 0)
        {
            return crouched ? 12 : 4;
        }

        if ((flags & HumanMotionState.MoveRight) != 0)
        {
            return crouched ? 15 : 7;
        }

        if ((flags & HumanMotionState.MoveLeft) != 0)
        {
            return crouched ? 16 : 8;
        }

        // The jump edge is tested after movement, so walking into a jump keeps the walk state for the
        // tick the button goes down and only jumps once the direction keys are released.
        if (jumpEdge)
        {
            return Jump;
        }

        if ((flags & HumanMotionState.Turn) != 0)
        {
            return Turn;
        }

        return Stay;
    }

    /// <summary>Gets the state a direction bit leads to.</summary>
    /// <param name="direction">One of the four movement bits.</param>
    /// <param name="crouched">Whether <see cref="HumanMotionState.Crouched"/> is set.</param>
    /// <returns>The state id.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a movement bit.</exception>
    public static int ForDirection(HumanMotionState direction, bool crouched) => direction switch
    {
        HumanMotionState.MoveForward => crouched ? 9 : 1,
        HumanMotionState.MoveBackward => crouched ? 12 : 4,
        HumanMotionState.MoveRight => crouched ? 15 : 7,
        HumanMotionState.MoveLeft => crouched ? 16 : 8,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Not a movement bit."),
    };
}

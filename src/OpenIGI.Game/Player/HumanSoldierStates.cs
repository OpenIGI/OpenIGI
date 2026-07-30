namespace OpenIGI.Game.Player;

/// <summary>
/// What clip each of a soldier's thirty-eight locomotion states plays — the table at <c>0x53ED10</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanSoldier</c>'s constructor (<c>0x45CC50</c>) builds a state machine at <c>Human+748</c> over
/// the pointer table <c>off_53EC10</c> and installs <b>this</b> table at <c>Human+824</c> — the same
/// pair of fields the player has, with the player's own tables at <c>0x538870</c>. Every state entry
/// function then calls <c>0x489E70(human, stateId, 0.15f)</c>, which indexes it.
/// </para>
/// <para>
/// <b>Animation id 0 means "this state names its own clip"</b>, not animation 0 — clip ids start at 2.
/// It is the same marker the player's table uses, and it is what the three death states carry
/// (<see cref="SoldierDeath"/>).
/// </para>
/// <para>
/// <b>These ids are a soldier's, not the player's.</b> The same number in the player's animation set is
/// a different clip: playing the weapon table's ids on a soldier's rig made guards fall flat on the
/// floor, get up and do it again. A body clip for a soldier comes from here.
/// </para>
/// </remarks>
public static class HumanSoldierStates
{
    /// <summary>How many states a soldier's machine has.</summary>
    public const int StateCount = 38;

    /// <summary>The animation id that means "this state names its own clip".</summary>
    public const int SelfDriven = 0;

    /// <summary>The blend time every state entry passes, in seconds.</summary>
    /// <remarks>The literal third argument of <c>0x489E70</c>, shared by all thirty-eight.</remarks>
    public const float BlendSeconds = 0.15f;

    // {animation, mode} per state, transcribed from 0x53ED10 -- 38 pairs, 304 bytes.
    private static readonly (int Animation, int Mode)[] Table =
    [
        (2, 1), (34, 1), (3, 1), (57, 1), (10, 0), (11, 0), (56, 1), (14, 0), (58, 0), (70, 0),
        (54, 0), (53, 0), (55, 0), (7, 0), (19, 0), (12, 0), (13, 0), (48, 1), (52, 1), (18, 0),
        (33, 0), (32, 0), (9, 1), (15, 0), (8, 0), (20, 0), (16, 0), (17, 0), (21, 0), (2, 1),
        (0, 0), (26, 0), (0, 0), (0, 0), (0, 0), (0, 0), (31, 1), (2, 0),
    ];

    /// <summary>The clip a state plays.</summary>
    /// <param name="state">The locomotion state — <c>Human+764</c>.</param>
    /// <returns>The animation id, or <c>-1</c> for a state outside the table.</returns>
    /// <remarks>
    /// A state carrying <see cref="SelfDriven"/> is returned as <c>-1</c> too: the caller has to name
    /// the clip itself, and handing back 0 would play whatever clip happened to be first.
    /// </remarks>
    public static int AnimationOf(int state)
    {
        if (state < 0 || state >= StateCount)
        {
            return -1;
        }

        int animation = Table[state].Animation;
        return animation == SelfDriven ? -1 : animation;
    }

    /// <summary>The mode a state's clip plays in.</summary>
    /// <param name="state">The locomotion state.</param>
    /// <returns>The mode, or <c>-1</c> for a state outside the table.</returns>
    /// <remarks>
    /// The second field of each pair. Its meaning is <b>not read</b> — it is 1 for eleven states and 0
    /// for the rest, and no consumer of it has been followed. Exposed because it is measured; named
    /// nothing because it is not understood.
    /// </remarks>
    public static int ModeOf(int state) => state >= 0 && state < StateCount ? Table[state].Mode : -1;

    /// <summary>Whether a state names its own clip rather than taking one from the table.</summary>
    /// <param name="state">The locomotion state.</param>
    /// <returns><see langword="true"/> when the table's entry is <see cref="SelfDriven"/>.</returns>
    public static bool IsSelfDriven(int state) =>
        state >= 0 && state < StateCount && Table[state].Animation == SelfDriven;
}

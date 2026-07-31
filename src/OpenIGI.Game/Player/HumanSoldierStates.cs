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

    /// <summary>The blend time a soldier's state entry passes, in seconds.</summary>
    /// <remarks>
    /// <b>Zero, measured.</b> Forty-four state lookups against this table in a live retail mission all
    /// passed <c>0.000</c>; the player's table <c>0x538870</c> is looked up with <c>0.200</c> and
    /// sometimes <c>0.000</c>. <c>player.md</c> §16 records <c>0.15f</c> for the player, read statically
    /// — whatever that literal is, it is not what reaches this argument, and a soldier blends nothing.
    /// </remarks>
    public const float BlendSeconds = 0f;

    /// <summary>The state a soldier fires from — <b>and the only state it can fire from</b>.</summary>
    /// <remarks>
    /// <para>
    /// Measured first, then read. A live capture showed a fighting guard sitting here on every tick its
    /// burst counter was draining; <c>0x45B060</c>, the state's per-tick, then said why. It is the only
    /// one of the four combat states that touches the weapon, and what it does is
    /// <c>if (*(float *)(human + 456) != 0) human[+1233] = 1</c> — <c>Human+456</c> is <b>input-frame
    /// slot 6</b> (the newest frame lives at <c>+432</c>) and <c>Human+1233</c> is what the weapon tick
    /// hands the gun. So the chain that starts with the AI writing <c>HumanAI+15500</c> ends here, and
    /// a soldier holding the Fire slot up in any other state fires nothing at all.
    /// </para>
    /// <para>
    /// <b>A firing soldier is crouched.</b> State 6 is the only one of the four that sets
    /// <c>Human+829</c>. It is not the AI's kneel — that is state 19.
    /// </para>
    /// </remarks>
    public const int FiringState = 6;

    /// <summary>The state a soldier's body is in once its burst is spent.</summary>
    /// <remarks>
    /// <para>
    /// The combat cycle is <c>0 → 12 → 10 → 6</c>, and <b>12 is the only way back to 0</b> — its
    /// per-tick <c>0x45B820</c> leaves for state 0 and nothing else. So a guard between bursts is here,
    /// not standing. The live capture agreed: state 12 on the tick the burst counter reached zero, and
    /// state 0 only once the target was out of sight.
    /// </para>
    /// <para>
    /// Reading a spent burst as standing put a tick of the idle clip between every two bursts, and
    /// because bursts are back to back that was a visible flicker.
    /// </para>
    /// </remarks>
    public const int ReloadState = 12;

    /// <summary>The state a soldier stands in.</summary>
    public const int StandState = 0;

    /// <summary>The state a soldier <b>runs</b> in — what the move-and-fight arm's Follow puts it in.</summary>
    /// <remarks>
    /// <para>
    /// Measured by hooking <c>"Combat - Follow"</c> (<c>0x48C860</c>) in the running game: a chasing guard
    /// sits in locomotion state 17 (its diagonal twin is 18), at <b>4.0–5.0 m/s</b> — about three and a half
    /// times the 1.28 m/s patrol walk. Through the table (<c>0x53ED10</c>) that reads as <b>animation 48</b>
    /// (state 18 is 52), whose own root motion carries the run; the walk it was drawn with instead is why a
    /// chasing guard looked like it was strolling.
    /// </para>
    /// <para>
    /// <b>Which of 17/18 is straight-ahead versus diagonal is not yet pinned</b> — both were seen at the same
    /// speed — so 17 is taken as the forward run and the distinction is recorded as open.
    /// </para>
    /// </remarks>
    public const int RunState = 17;

    /// <summary>The state a soldier falls into when it is not on the ground.</summary>
    /// <remarks>Every combat state's per-tick leaves for this the moment flag <c>0x4</c> clears.</remarks>
    public const int AirborneState = 29;

    /// <summary>The state a soldier flinches into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x10000</c> — which is exactly the bit <c>0x45D3C0</c> raises when the running
    /// damage accumulator passes the settings row's <c>Recoil threshold</c>. The damage handler and the
    /// state machine meet on that bit.
    /// </remarks>
    public const int FlinchState = 30;

    /// <summary>The state a soldier dies into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x20000</c>, the other bit <c>0x45D3C0</c> raises, and its entry is
    /// <c>0x45C440</c> — the general death chooser <see cref="SoldierDeath"/> already read from the
    /// other direction.
    /// </remarks>
    public const int DeathState = 32;

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

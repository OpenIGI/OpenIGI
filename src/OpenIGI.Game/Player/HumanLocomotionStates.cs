namespace OpenIGI.Game.Player;

/// <summary>
/// One locomotion state's animation binding — what a <c>Human</c> plays on entering it.
/// </summary>
/// <param name="AnimationId">
/// The animation to play, or <see cref="HumanLocomotionStates.NoAnimation"/> for a state that drives
/// itself.
/// </param>
/// <param name="Mode">The playback mode; <c>0</c>, <c>1</c> or <c>2</c> in retail data.</param>
public readonly record struct HumanLocomotionState(int AnimationId, int Mode);

/// <summary>
/// The table binding each of the 49 locomotion states to an animation.
/// </summary>
/// <remarks>
/// <para>
/// Every state's entry function has the same three lines: clear the crouch flag, call
/// <c>0x489E70(human, stateId, 0.15f)</c>, and install the state's per-tick function. That middle call
/// is the whole binding — it indexes <c>Human+824</c> by the <b>state id</b> and pulls out an
/// <c>{ animationId, mode }</c> pair.
/// </para>
/// <para>
/// The table is at <c>0x538870</c> and runs to <c>0x5389F8</c> — <b>exactly 49 × 8 bytes</b>, ending
/// precisely where the 17-row tables from <c>DefineHumanPlayerGeneral</c> begin
/// (<see cref="HumanPlayerParameters"/>). That boundary is what pins the entry size and the count.
/// </para>
/// </remarks>
public static class HumanLocomotionStates
{
    /// <summary>The number of locomotion states.</summary>
    public const int Count = 49;

    /// <summary>The animation id used by a state that plays no clip from the set.</summary>
    public const int NoAnimation = 0;

    /// <summary>The blend time every state entry passes, in seconds.</summary>
    public const float BlendSeconds = 0.15f;

    /// <summary>The developer fly state, entered by the Ctrl+Shift+F chord.</summary>
    public const int DeveloperState = 48;

    private static readonly HumanLocomotionState[] Table =
    [
        new(2, 1), new(4, 1), new(37, 1), new(35, 1), new(64, 1), new(70, 1), new(68, 1),
        new(11, 1), new(10, 1), new(57, 1), new(74, 1), new(72, 1), new(75, 1), new(77, 1),
        new(76, 1), new(81, 1), new(80, 1), new(14, 0), new(15, 0), new(19, 1), new(17, 1),
        new(41, 1), new(39, 1), new(27, 1), new(49, 1), new(47, 1), new(29, 1), new(28, 1),
        new(25, 1), new(2, 1),
        new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0),
        new(168, 1), new(170, 2), new(170, 0), new(169, 1), new(171, 0), new(172, 1),
        new(173, 1), new(2, 1), new(19, 1), new(0, 0), new(0, 0), new(2, 1),
    ];

    /// <summary>Gets the bindings, indexed by state id.</summary>
    public static IReadOnlyList<HumanLocomotionState> Bindings => Table;

    /// <summary>Gets one state's binding.</summary>
    /// <param name="state">The locomotion state id.</param>
    /// <returns>The animation and mode the state plays.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a state.</exception>
    public static HumanLocomotionState For(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, Count);
        return Table[state];
    }

    /// <summary>Reports whether a state drives itself instead of playing a clip from the set.</summary>
    /// <param name="state">The locomotion state id.</param>
    /// <returns><see langword="true"/> when the state's animation id is zero.</returns>
    /// <remarks>
    /// The seven consecutive states <b>30..36</b>, plus 46 and 47, carry animation id 0. Those are
    /// exactly the states whose entry functions name a clip <em>by string</em> instead —
    /// <c>picklock_loop</c> at 30, <c>typecomp_loop</c> at 32, <c>wire_slide_1</c> at 40's neighbours
    /// and <c>player_death_</c> at 47 — so a zero here means "this state does its own thing", not
    /// "this state is unused".
    /// </remarks>
    public static bool DrivesItself(int state) => For(state).AnimationId == NoAnimation;
}

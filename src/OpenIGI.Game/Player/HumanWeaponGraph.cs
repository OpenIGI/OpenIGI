using System.Collections.ObjectModel;

namespace OpenIGI.Game.Player;

/// <summary>
/// Which states each weapon state can move to, in the order its per-tick tests them.
/// </summary>
/// <remarks>
/// <para>
/// Extracted the same mechanical way as <see cref="HumanLocomotionGraph"/>: every transition in this
/// code compiles to the identical five-instruction shape, so the immediates written to the machine's
/// current-state field <b>are</b> the targets, and their order in the function is the order the
/// branches test them.
/// </para>
/// <para>
/// The shape is worth stating because it is the opposite of the locomotion machine's. This one is a
/// <b>star</b>: <see cref="HumanWeaponState.Ready"/> branches thirteen ways and every other state
/// returns to it and to almost nothing else. That is why the weapon side reads cleanly and the
/// locomotion side needed a mechanical extraction to be trusted at all.
/// </para>
/// </remarks>
public static class HumanWeaponGraph
{
    private static readonly ReadOnlyDictionary<HumanWeaponState, ReadOnlyCollection<HumanWeaponState>> Table =
        Build();

    /// <summary>Gets the number of states in the machine.</summary>
    /// <remarks>Thirteen — the length of <c>off_538678</c>, fixed by where the next table begins.</remarks>
    public const int StateCount = 13;

    /// <summary>Gets the state the machine is registered in.</summary>
    /// <remarks>Argument 4 of the constructor's <c>0x4EC190</c> call is a literal <c>1</c>.</remarks>
    public const HumanWeaponState InitialState = HumanWeaponState.Ready;

    /// <summary>Gets how many triggers the machine registers.</summary>
    /// <remarks>
    /// <b>None.</b> Arguments 5 and 6 are <c>0</c> and <c>NULL</c>, against the locomotion machine's two
    /// four-step double-taps — so no weapon action is a gesture.
    /// </remarks>
    public const int TriggerCount = 0;

    /// <summary>Gets the states a state may move to, in branch order.</summary>
    /// <param name="state">The state.</param>
    /// <returns>Its targets. Order is significant: the first matching test wins.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a state.</exception>
    public static ReadOnlyCollection<HumanWeaponState> TargetsOf(HumanWeaponState state) =>
        Table.TryGetValue(state, out ReadOnlyCollection<HumanWeaponState>? targets)
            ? targets
            : throw new ArgumentOutOfRangeException(nameof(state), state, "Not a weapon state.");

    /// <summary>Gets every state, in table order.</summary>
    public static IEnumerable<HumanWeaponState> States => Table.Keys;

    private static ReadOnlyDictionary<HumanWeaponState, ReadOnlyCollection<HumanWeaponState>> Build()
    {
        Dictionary<HumanWeaponState, ReadOnlyCollection<HumanWeaponState>> table = new()
        {
            // 0x40E980: waits for the suspend byte to clear.
            [HumanWeaponState.Suspended] = Targets(HumanWeaponState.Ready),

            // 0x40E9C0, the hub. Suspend, then the five weapon-category arms of the fire branch, then
            // the alternate-fire arms, then reload, peek, the pending weapon change, and the map.
            [HumanWeaponState.Ready] = Targets(
                HumanWeaponState.Suspended,
                HumanWeaponState.Striking,
                HumanWeaponState.Throwing,
                HumanWeaponState.Medipack,
                HumanWeaponState.Placing,
                HumanWeaponState.Firing,
                HumanWeaponState.DryFire,
                HumanWeaponState.AlternateFire,
                HumanWeaponState.Placing,
                HumanWeaponState.Reloading,
                HumanWeaponState.Peeking,
                HumanWeaponState.ChangingWeapon,
                HumanWeaponState.MapComputer),

            // 0x40EF70.
            [HumanWeaponState.Firing] = Targets(
                HumanWeaponState.Suspended,
                HumanWeaponState.DryFire,
                HumanWeaponState.Reloading,
                HumanWeaponState.Ready),

            [HumanWeaponState.Reloading] = Targets(HumanWeaponState.Ready),

            // 0x40F300 re-enters the change when another one is already pending, which is what makes
            // holding NextWeapon step rather than stall.
            [HumanWeaponState.ChangingWeapon] = Targets(
                HumanWeaponState.Ready,
                HumanWeaponState.ChangingWeapon),

            [HumanWeaponState.Peeking] = Targets(HumanWeaponState.Ready),
            [HumanWeaponState.AlternateFire] = Targets(HumanWeaponState.Ready),
            [HumanWeaponState.Throwing] = Targets(HumanWeaponState.Ready),

            // 0x40F950 falls into the weapon change when the last mine has been placed.
            [HumanWeaponState.Placing] = Targets(
                HumanWeaponState.Ready,
                HumanWeaponState.ChangingWeapon),

            [HumanWeaponState.Striking] = Targets(HumanWeaponState.Ready),
            [HumanWeaponState.Medipack] = Targets(HumanWeaponState.Ready),
            [HumanWeaponState.MapComputer] = Targets(HumanWeaponState.Ready),

            // 0x40F100.
            [HumanWeaponState.DryFire] = Targets(
                HumanWeaponState.Suspended,
                HumanWeaponState.Ready),
        };

        return table.AsReadOnly();
    }

    private static ReadOnlyCollection<HumanWeaponState> Targets(params HumanWeaponState[] states) =>
        states.AsReadOnly();
}

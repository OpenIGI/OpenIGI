using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Player;

/// <summary>
/// Which weapon action each locomotion state asks the torso to play.
/// </summary>
/// <remarks>
/// <para>
/// Twenty-six of the forty-nine locomotion state entries call <c>sub_413A60(human, action)</c>, which
/// writes <c>Human+3420</c> — the <i>requested</i> weapon action. The weapon machine's Ready state
/// reads it back and dispatches whenever it differs from what is playing (<c>Human+3416</c>), which is
/// how walking with a rifle plays the rifle's walk clip without the two machines knowing about each
/// other.
/// </para>
/// <para>
/// <b>This table is why states 9–16 are the walking set.</b> An earlier pass read the locomotion flag
/// <c>0x40000</c> as crouch, which would make 9–16 the crouched states. They declare
/// <see cref="WeaponAction.Walk"/> and 1–8 declare <see cref="WeaponAction.Run"/>, and the crouched
/// states are 19 through 27 — reached through a different bit entirely. The binding table names
/// <c>0x40000</c> <see cref="PlayerAction.ToggleWalkRun"/>, and this table agrees with it from a
/// completely different direction.
/// </para>
/// <para>10 and the original.</para>
/// </remarks>
public static class HumanLocomotionWeaponActions
{
    private static readonly Dictionary<int, WeaponAction> Table = Build();

    /// <summary>Finds the action a locomotion state declares.</summary>
    /// <param name="state">The locomotion state.</param>
    /// <param name="action">The action it asks for.</param>
    /// <returns>
    /// <see langword="false"/> for the twenty-three states that declare none — the jump, the falls, the
    /// climbs, the interactive set and the deaths, which leave the request where it was.
    /// </returns>
    public static bool TryGet(int state, out WeaponAction action) => Table.TryGetValue(state, out action);

    /// <summary>Gets every state that declares an action.</summary>
    public static IReadOnlyDictionary<int, WeaponAction> All => Table;

    private static Dictionary<int, WeaponAction> Build()
    {
        Dictionary<int, WeaponAction> table = new()
        {
            // Standing still.
            [0] = WeaponAction.Stand,
        };

        // The eight running directions, and the eight walking ones exactly eight higher.
        for (int state = 1; state <= 8; state++)
        {
            table[state] = WeaponAction.Run;
            table[state + 8] = WeaponAction.Walk;
        }

        // Crouched still, then the eight crouched directions.
        table[19] = WeaponAction.Crouch;
        for (int state = 20; state <= 27; state++)
        {
            table[state] = WeaponAction.CrouchRun;
        }

        // Reached from the interactive set rather than from movement.
        table[45] = WeaponAction.Crouch;

        return table;
    }
}

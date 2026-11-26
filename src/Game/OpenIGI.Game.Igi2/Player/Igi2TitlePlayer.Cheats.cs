using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The shared cheats, taken each tick, and what the weapon in hand's second trigger does, for the touch controls.</content>
public sealed partial class Igi2TitlePlayer
{
    /// <inheritdoc/>
    /// <remarks>A weapon whose second fire type is a <c>FireMode</c> (<c>FireMode_WeaponFireHandler</c>, <c>igi2 0x51BE00</c>).</remarks>
    public bool AlternateFireSwitchesMode => Weapons?.Inventory.Current?.WeaponType.FireType(1) is Igi2FireModeTask;

    // The cheats in force (none in a multiplayer session, PlayerCheatLock), laid on the player before the tick reads them.
    private void TakeCheats()
    {
        PlayerCheatOptions cheats = PlayerCheatOptions.Current;
        Human.TakeCheats(cheats, Weapons?.Firing);
        if (cheats.AllWeapons && !Human.IsDead)
        {
            Weapons?.Inventory.EnableEveryWeapon();
        }
    }
}

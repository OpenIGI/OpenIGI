namespace OpenIGI.Game.Igi2.Player;

/// <content>The thermal goggles, where another game's player stands for this one.</content>
/// <remarks>
/// The level hands its player thermal goggles, which I.G.I. 2's player puts on as it would raise a weapon. A player of a game
/// that has none wears the level's: while the level's player carries the goggles, the thermal key puts them on and takes them
/// off, and the view is drawn in infrared as the level's own player would see it (<see cref="IsInfraredView"/>). They come off
/// with the player's death.
/// </remarks>
public sealed partial class Igi2TitlePlayer
{
    // The actions pressed this tick, as the title's keys give them.
    private Igi2PlayerAction _pressedThisTick;

    // Whether the player standing in has the level's thermal goggles on.
    private bool _standInThermal;

    /// <summary>Gets a value indicating whether the player standing in for this one wears the level's thermal goggles.</summary>
    public bool StandInWearsThermal => _standInThermal;

    // The thermal key puts the goggles on the player standing in, or takes them off, where the level's player carries a pair.
    private void ToggleThermalForStandIn()
    {
        if ((_pressedThisTick & Igi2PlayerAction.Thermal) != 0
            && Inventory?.FindWeapon(Igi2HumanPlayer.ThermalWeaponId) is { IsActive: true })
        {
            _standInThermal = !_standInThermal;
        }
    }
}

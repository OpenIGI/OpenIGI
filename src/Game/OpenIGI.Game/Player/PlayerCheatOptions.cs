namespace OpenIGI.Game.Player;

/// <summary>The cheats in force for one player, as one immutable value.</summary>
/// <param name="SpeedMultiplier">Scales root-motion movement, air control and horizontal jump carry.</param>
/// <param name="JumpMultiplier">Scales the vertical jump launch speed.</param>
/// <param name="HealthMultiplier">Scales the maximum health humanplayer.qvm's argument 7 sets.</param>
/// <param name="InfiniteHealth">Whether hits, falls and fences leave the player's health untouched.</param>
/// <param name="InfiniteAmmo">Whether firing takes nothing: magazines stay full and reserves untouched.</param>
/// <param name="AllWeapons">Whether the player carries every weapon the mission loaded.</param>
/// <param name="UnlockAllLevels">Whether every campaign mission may be played.</param>
/// <param name="NoRecoil">Whether fired rounds leave the view where it was.</param>
/// <param name="NoReload">Whether the magazine refills from the reserve without a reload, until the reserve runs out.</param>
public readonly record struct PlayerCheatOptions(
    float SpeedMultiplier,
    float JumpMultiplier,
    float HealthMultiplier,
    bool InfiniteHealth,
    bool InfiniteAmmo,
    bool AllWeapons,
    bool UnlockAllLevels,
    bool NoRecoil,
    bool NoReload)
{
    /// <summary>Gets the set with every cheat off: the unmodified game.</summary>
    public static PlayerCheatOptions Retail { get; } = new(
        SpeedMultiplier: 1f,
        JumpMultiplier: 1f,
        HealthMultiplier: 1f,
        InfiniteHealth: false,
        InfiniteAmmo: false,
        AllWeapons: false,
        UnlockAllLevels: false,
        NoRecoil: false,
        NoReload: false);

    /// <summary>Gets the cheats the variables ask for, or <see cref="Retail"/> while cheats are not enabled.</summary>
    public static PlayerCheatOptions Current => Activate(
        new PlayerCheatOptions(
            PlayerCheatCVars.Speed.Value,
            PlayerCheatCVars.Jump.Value,
            PlayerCheatCVars.HealthScale.Value,
            PlayerCheatCVars.InfiniteHealth.Value,
            PlayerCheatCVars.InfiniteAmmo.Value,
            PlayerCheatCVars.AllWeapons.Value,
            PlayerCheatCVars.UnlockAllLevels.Value,
            PlayerCheatCVars.NoRecoil.Value,
            PlayerCheatCVars.NoReload.Value),
        PlayerCheatCVars.CheatsEnabled.Value);

    /// <summary>Applies the session's gate to a configured set.</summary>
    /// <param name="configured">The cheats the variables ask for.</param>
    /// <param name="cheatsEnabled">Whether cheats are enabled.</param>
    /// <returns><paramref name="configured"/> when cheats are enabled; otherwise <see cref="Retail"/>.</returns>
    public static PlayerCheatOptions Activate(PlayerCheatOptions configured, bool cheatsEnabled) =>
        cheatsEnabled ? configured : Retail;
}

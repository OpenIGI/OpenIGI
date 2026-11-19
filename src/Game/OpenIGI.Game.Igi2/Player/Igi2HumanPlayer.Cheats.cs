using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The shared cheats (<see cref="Game.Player.PlayerCheatOptions"/>) as I.G.I. 2's player takes them. <b>Not retail</b>, save the
/// unlimited ammunition the original keeps as <c>isUnlimitedAmmo</c>, which the infinite-ammunition cheat raises with them.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets what the jump's launch is scaled by: the jump cheat's, one unchanged.</summary>
    public float JumpScale { get; set; } = 1f;

    /// <summary>Gets or sets a value indicating whether a shot leaves the view where it was: the no-recoil cheat.</summary>
    public bool IsRecoilless { get; set; }

    /// <summary>Gets or sets a value indicating whether an emptied magazine fills from what is carried at once: the no-reload cheat.</summary>
    public bool IsReloadless { get; set; }

    /// <summary>Gets or sets a value indicating whether guards and cameras do not see the player: the unseen cheat.</summary>
    public bool IsUnseen { get; set; }

    /// <inheritdoc/>
    /// <remarks>An unseen player is left off every detector's lists, as a human out of the level is.</remarks>
    protected override bool IsDetectionActive => !IsUnseen;

    /// <summary>Takes the cheats in force.</summary>
    /// <param name="cheats">The cheats; <see cref="Game.Player.PlayerCheatOptions.Retail"/> for none.</param>
    /// <param name="firing">The player's weapons' firing, which the ammunition cheat keeps loaded, or <see langword="null"/>.</param>
    public void TakeCheats(in Game.Player.PlayerCheatOptions cheats, Igi2WeaponFiring? firing)
    {
        Damage.IsUnharmed = cheats.InfiniteHealth;
        Damage.Toughness = cheats.HealthMultiplier > 0f ? cheats.HealthMultiplier : 1f;
        MoveScale = cheats.SpeedMultiplier > 0f ? cheats.SpeedMultiplier : 1f;
        JumpScale = cheats.JumpMultiplier > 0f ? cheats.JumpMultiplier : 1f;
        IsRecoilless = cheats.NoRecoil;
        IsReloadless = cheats.NoReload;
        IsUnseen = cheats.Unseen;
        if (firing is not null)
        {
            firing.KeepsLoaded = cheats.InfiniteAmmo;
            firing.IsUnlimitedAmmo = cheats.InfiniteAmmo;
        }
    }

    // The no-reload cheat fills an emptied magazine straight from what is carried, while anything is.
    private bool ReloadsOnEmpty(Igi2Inventory inventory) =>
        IsReloadless && inventory.Current is { } item && inventory.FireTypeRounds(item, FireType).Available != 0;
}

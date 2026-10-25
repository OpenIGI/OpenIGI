namespace OpenIGI.Game.Player;

/// <summary>The crosshair a weapon in hand is aimed with, in the remastered display: one look for each kind of weapon.</summary>
public enum CrosshairShape
{
    /// <summary>No crosshair: binoculars, a medipack, a mine, empty hands.</summary>
    None,

    /// <summary>Four short arms, tight, and a dot.</summary>
    Pistol,

    /// <summary>Four arms and a dot.</summary>
    Rifle,

    /// <summary>Four arms set wider, with corner ticks, and a dot.</summary>
    Smg,

    /// <summary>Heavier arms within brackets, and a dot.</summary>
    MachineGun,

    /// <summary>A ring at the spread, and a dot.</summary>
    Shotgun,

    /// <summary>A scoped rifle fired from the hip: a dot and a faint ring.</summary>
    Sniper,

    /// <summary>A rocket launcher: a chevron with two drop ticks.</summary>
    Launcher,

    /// <summary>A grenade: a small arc and a dot.</summary>
    Grenade,

    /// <summary>A knife: a dot.</summary>
    Melee,
}

/// <summary>What the remastered crosshair shows of the player's aim this frame: either game fills it alike.</summary>
/// <param name="Shape">The crosshair of the weapon in hand.</param>
/// <param name="SpreadRadians">How wide the weapon's rounds stray now, the cone's half-angle in radians.</param>
/// <param name="OnEnemy">Whether a living enemy is under the crosshair in clear sight.</param>
/// <param name="Shots">How many rounds the player has fired, counted up as each goes.</param>
/// <param name="Hits">How many hits the player's rounds have scored on soldiers.</param>
/// <param name="Kills">How many soldiers the player's rounds have killed.</param>
/// <param name="LastHitHead">Whether the last hit was to the head.</param>
/// <param name="LastKillHead">Whether the last kill was by a round to the head.</param>
public readonly record struct PlayerAimFeedback(
    CrosshairShape Shape,
    float SpreadRadians,
    bool OnEnemy,
    long Shots,
    int Hits,
    int Kills,
    bool LastHitHead,
    bool LastKillHead)
{
    /// <summary>No crosshair and nothing to report.</summary>
    public static PlayerAimFeedback None => default;

    /// <summary>The feedback with a tally's hits and kills.</summary>
    /// <param name="shape">The crosshair.</param>
    /// <param name="spreadRadians">The cone's half-angle.</param>
    /// <param name="onEnemy">Whether an enemy is under the crosshair.</param>
    /// <param name="shots">The rounds fired so far.</param>
    /// <param name="tally">What the rounds have done.</param>
    /// <returns>The feedback.</returns>
    public static PlayerAimFeedback Of(CrosshairShape shape, float spreadRadians, bool onEnemy, long shots, ShotTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return new(shape, spreadRadians, onEnemy, shots, tally.Hits, tally.Kills, tally.LastHitHead, tally.LastKillHead);
    }
}

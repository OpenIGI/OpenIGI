namespace OpenIGI.Game.Player;

/// <summary>
/// What a player's rounds have done to the soldiers, as either game's soldiers report it: each hit and each kill, whether it
/// was to the head, counted as it happens. The display reads it to mark a hit and confirm a kill; it is carried with every
/// round the player fires (<see cref="Weapons.WeaponHit.Credit"/>), so a soldier of either game credits whichever player shot it.
/// </summary>
public sealed class ShotTally
{
    /// <summary>Gets how many hits the player's rounds have scored on soldiers.</summary>
    public int Hits { get; private set; }

    /// <summary>Gets how many soldiers the player's rounds have killed.</summary>
    public int Kills { get; private set; }

    /// <summary>Gets a value indicating whether the last hit was to the head.</summary>
    public bool LastHitHead { get; private set; }

    /// <summary>Gets a value indicating whether the last kill was by a round to the head.</summary>
    public bool LastKillHead { get; private set; }

    /// <summary>Counts a hit on a soldier.</summary>
    /// <param name="head">Whether the round struck the head.</param>
    public void Hit(bool head)
    {
        Hits++;
        LastHitHead = head;
    }

    /// <summary>Counts a kill.</summary>
    /// <param name="head">Whether the killing round struck the head.</param>
    public void Killed(bool head)
    {
        Kills++;
        LastKillHead = head;
    }
}

namespace OpenIGI.Game.Player;

/// <summary>
/// What a player's rounds have done to the soldiers, as either game's soldiers report it, and to a session's other players, as
/// its server tells of it: each hit and each kill, whether it was to the head, counted as it happens. The display reads it to
/// mark a hit and confirm a kill; it is carried with every round the player fires (<see cref="Weapons.WeaponHit.Credit"/>), so a
/// soldier of either game credits whichever player shot it.
/// </summary>
public sealed class ShotTally
{
    /// <summary>Gets how many hits the player's rounds have scored: on soldiers, and on a session's other players.</summary>
    public int Hits { get; private set; }

    /// <summary>Gets how many the player's rounds have killed: soldiers, and a session's other players.</summary>
    public int Kills { get; private set; }

    /// <summary>Gets how many of <see cref="Hits"/> were on soldiers, which a server reports to its players from this tally.</summary>
    public int SoldierHits { get; private set; }

    /// <summary>Gets how many of <see cref="Kills"/> were soldiers.</summary>
    public int SoldierKills { get; private set; }

    /// <summary>Gets how many rounds the player has fired from its guns: what its accuracy is reckoned against.</summary>
    public int RoundsFired { get; private set; }

    /// <summary>Gets a value indicating whether the last hit was to the head.</summary>
    public bool LastHitHead { get; private set; }

    /// <summary>Gets a value indicating whether the last kill was by a round to the head.</summary>
    public bool LastKillHead { get; private set; }

    /// <summary>Counts a hit on a soldier.</summary>
    /// <param name="head">Whether the round struck the head.</param>
    public void Hit(bool head)
    {
        SoldierHits++;
        Hits++;
        LastHitHead = head;
    }

    /// <summary>Counts rounds the player's gun let go.</summary>
    /// <param name="rounds">How many.</param>
    public void Fired(int rounds) => RoundsFired += Math.Max(0, rounds);

    /// <summary>
    /// Puts back what a save game kept of a single player's mission: the rounds fired, and the hits and kills on its soldiers,
    /// which are then all of its hits and kills.
    /// </summary>
    /// <param name="roundsFired">The rounds fired.</param>
    /// <param name="soldierHits">The hits on soldiers.</param>
    /// <param name="soldierKills">The soldiers killed.</param>
    public void Restore(int roundsFired, int soldierHits, int soldierKills)
    {
        RoundsFired = Math.Max(0, roundsFired);
        Hits = SoldierHits = Math.Max(0, soldierHits);
        Kills = SoldierKills = Math.Max(0, soldierKills);
    }

    /// <summary>Counts a hit on another player of the session, as the server told of it.</summary>
    /// <param name="head">Whether the round struck the head.</param>
    public void HitPlayer(bool head)
    {
        Hits++;
        LastHitHead = head;
    }

    /// <summary>Counts a kill of another player of the session, as the server told of it.</summary>
    /// <param name="head">Whether the killing round struck the head.</param>
    public void KilledPlayer(bool head)
    {
        Kills++;
        LastKillHead = head;
    }

    /// <summary>Counts a kill.</summary>
    /// <param name="head">Whether the killing round struck the head.</param>
    public void Killed(bool head)
    {
        SoldierKills++;
        Kills++;
        LastKillHead = head;
    }
}

/// <summary>Where the host finds what the player's rounds have done: a title's gameplay or its player.</summary>
public interface IShotTallySource
{
    /// <summary>Gets the player's tally, or <see langword="null"/> while there is no player.</summary>
    ShotTally? PlayerShots { get; }

    /// <summary>Gets the key of the weapon in the player's hands, as the kill feed draws it, or empty.</summary>
    string PlayerWeapon => string.Empty;
}

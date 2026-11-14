namespace OpenIGI.Game.Player;

/// <summary>What a title's player gives a match that the host runs around it: what it carries, and being held where it stands.</summary>
/// <remarks>
/// The buy menu prices ammunition against what is carried, a round's freeze time holds the player as the server holds it,
/// and a page over the level -- the result, a demo's watched player -- stops it and lets go of the mouse.
/// </remarks>
public interface ITitleMatchPlayer
{
    /// <summary>Gets what the player carries, or <see langword="null"/> before it is armed.</summary>
    PlayerWeapons? Weapons { get; }

    /// <summary>Gets or sets a value indicating whether the player takes nothing from its controls: it neither moves, turns nor fires.</summary>
    bool HeldStill { get; set; }

    /// <summary>Stops the player where it stands and lets go of the mouse, for a page drawn over the level.</summary>
    void HoldStill();
}

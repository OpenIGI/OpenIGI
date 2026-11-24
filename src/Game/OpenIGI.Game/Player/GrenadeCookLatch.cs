namespace OpenIGI.Game.Player;

/// <summary>Both games' rule for when the pin comes out of a grenade the player is throwing.</summary>
/// <remarks>
/// <para>
/// Each throw resets the latch, notes the cook key on every tick of the wind-up, and asks it whether the pin is due. Under
/// <see cref="GrenadeCook.Original"/> the pin is due when the game's own throw would pull it, whether or not the key was
/// pressed. Under <see cref="GrenadeCook.Manual"/> it is due only once the key has been pressed in this throw, and still not
/// before the game's own pin moment, so the hand never pulls a pin it has not yet reached.
/// </para>
/// <para>The key is the reload key: a grenade has nothing to reload, and neither game's throw reads it.</para>
/// </remarks>
public struct GrenadeCookLatch
{
    /// <summary>Gets whether the cook key has been pressed since the throw began.</summary>
    public bool Requested { get; private set; }

    /// <summary>Forgets the press, as a new throw begins.</summary>
    public void Reset() => Requested = false;

    /// <summary>Notes the cook key for this tick; once pressed, the press is kept to the end of the throw.</summary>
    /// <param name="cookKeyDown">Whether the cook key is down.</param>
    public void Note(bool cookKeyDown) => Requested |= cookKeyDown;

    /// <summary>Whether the pin is due this tick.</summary>
    /// <param name="mode">When the player's grenades cook.</param>
    /// <param name="gamePinDue">Whether the game's own throw has reached the moment it pulls the pin.</param>
    /// <returns><see langword="true"/> when the pin comes out (or stays out).</returns>
    public readonly bool PinDue(GrenadeCook mode, bool gamePinDue) =>
        gamePinDue && (mode == GrenadeCook.Original || Requested);
}

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>A title's pictures for the kill feed: each weapon a kill is credited to, by the key the session sends.</summary>
/// <remarks>
/// The feed is the host's, the same for both titles; the weapons and their sprites are each title's, so the session carries
/// only a weapon's key (an I.G.I. script id) and the title played turns it into a picture and a name.
/// </remarks>
public interface IKillFeedArt
{
    /// <summary>Gets the title's pictures, or <see langword="null"/> while they are not loaded.</summary>
    IHudIconSource? Icons { get; }

    /// <summary>Gets a value indicating whether the title's weapon pictures point left, so the feed turns them to point at the victim.</summary>
    bool WeaponsFaceLeft { get; }

    /// <summary>Gets a weapon's picture.</summary>
    /// <param name="weaponKey">The weapon's key.</param>
    /// <returns>The picture, or <see cref="HudIcon.None"/> where the title has none for it.</returns>
    HudIcon WeaponIcon(string weaponKey);

    /// <summary>Gets a weapon's name as the title shows it, for a weapon with no picture.</summary>
    /// <param name="weaponKey">The weapon's key.</param>
    /// <returns>The name, or empty.</returns>
    string WeaponName(string weaponKey);
}

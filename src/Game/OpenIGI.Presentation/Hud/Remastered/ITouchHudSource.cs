namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>What a title's display lends a touch screen's controls, so they are drawn in its look and with its pictures.</summary>
/// <remarks>
/// The controls are the host's, the same for both titles; the theme, the sprites and what the player holds are each title's.
/// </remarks>
public interface ITouchHudSource
{
    /// <summary>Gets the title's remastered theme: its colours and corners.</summary>
    HudTheme Theme { get; }

    /// <summary>Gets the title's pictures, or <see langword="null"/> while they are not loaded.</summary>
    IHudIconSource? Icons { get; }

    /// <summary>Gets the display's model as it was last built: the weapon in hand and the action on offer.</summary>
    HudModel Model { get; }
}

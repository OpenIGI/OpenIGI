using OpenIGI.Game.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What the mission's HUD draws from one frame.</summary>
/// <param name="Width">The window's width, in pixels.</param>
/// <param name="Height">The window's height, in pixels.</param>
/// <param name="SafeWidth">The width the 2D projection is set to, never nought.</param>
/// <param name="SafeHeight">The height the 2D projection is set to, never nought.</param>
/// <param name="UiScale">The interface's scale.</param>
/// <param name="TickCount">The game's tick, which the retail optics animate on.</param>
/// <param name="BinocularSeconds">How long the remaster's binoculars have been raised.</param>
/// <param name="RemasterHud">Whether the remaster's HUD is shown rather than the original's.</param>
/// <param name="RemasteredOptics">Whether the remaster's binoculars are shown.</param>
/// <param name="ShowFirstPersonHud">Whether the first-person HUD is shown at all this frame.</param>
/// <param name="ShowPlayerHud">Whether the player's status is shown this frame.</param>
/// <param name="TacticalMessages">Whether the status messages are the remaster's cards rather than the original's lines.</param>
/// <param name="WorldInteractionPrompt">Whether the interaction prompt stands at the object rather than in the HUD.</param>
/// <param name="BinocularTargets">What the binoculars have picked out.</param>
/// <param name="BinocularRangeMetres">The binoculars' range readout, or <see langword="null"/> when the glasses are down.</param>
/// <param name="Hud">The original's HUD, or <see langword="null"/>.</param>
/// <param name="RemasteredHud">The remaster's HUD, or <see langword="null"/>.</param>
public readonly record struct MissionHudFrame(
    int Width,
    int Height,
    int SafeWidth,
    int SafeHeight,
    int UiScale,
    int TickCount,
    float BinocularSeconds,
    bool RemasterHud,
    bool RemasteredOptics,
    bool ShowFirstPersonHud,
    bool ShowPlayerHud,
    bool TacticalMessages,
    bool WorldInteractionPrompt,
    IReadOnlyList<EnemyOverlayBox> BinocularTargets,
    float? BinocularRangeMetres,
    IHudPresenter? Hud,
    IRemasteredHudPresenter? RemasteredHud);

/// <summary>A title's mission HUD over the finished frame, drawn at four places in the host's overlay order.</summary>
public interface IMissionHudOverlay
{
    /// <summary>Draws the weapon readout, or the binoculars' or the scope's overlay that replaces it.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The frame.</param>
    void DrawWeaponHud(SpriteBatch batch, in MissionHudFrame frame);

    /// <summary>Draws the original's action sprite, status messages and player status.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The frame.</param>
    void DrawRetailStatus(SpriteBatch batch, in MissionHudFrame frame);

    /// <summary>Draws the red damage wash and the flashbang's white-out.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The frame.</param>
    void DrawWashes(SpriteBatch batch, in MissionHudFrame frame);

    /// <summary>Draws the remaster's HUD: the interaction bar, the scope, the binoculars and the readout.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="frame">The frame.</param>
    void DrawRemasteredHud(SpriteBatch batch, in MissionHudFrame frame);
}

namespace OpenIGI.Presentation.Hud;

/// <summary>Something a mod draws over the player's view: its own read-outs, markers or prompts beside the game's HUD.</summary>
/// <remarks>
/// Made for each level from what a level presenter is made from (<see cref="PresenterRegistry.ForHud"/>), drawn every frame
/// the player's view is up -- after the game's HUD and status, before a cut-scene's framing and the map computer -- and
/// disposed with the level. It draws in display colours, after the scene is resolved, so nothing of the picture's tone
/// mapping or bloom reaches it.
/// </remarks>
public interface IHudOverlay : IDisposable
{
    /// <summary>Draws the overlay for one frame.</summary>
    /// <param name="frame">What the frame offers to draw with and about.</param>
    void Draw(in HudOverlayFrame frame);
}

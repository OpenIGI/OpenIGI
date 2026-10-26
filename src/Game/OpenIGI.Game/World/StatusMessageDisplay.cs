namespace OpenIGI.Game.World;

/// <summary>One status message as it is on screen this tick.</summary>
/// <remarks>
/// A snapshot for a renderer, not the message itself: the state machine that produces it belongs to the
/// simulation and steps at 30 Hz, while whatever draws this may run several times per tick and must not
/// be able to advance it.
/// </remarks>
/// <param name="Text">The localized text in full.</param>
/// <param name="RevealedCharacters">
/// How many leading characters of <paramref name="Text"/> have been typed out. Renderers should draw
/// <c>Text.AsSpan(0, RevealedCharacters)</c>, which needs no allocation per frame.
/// </param>
/// <param name="DisplayFrame">
/// How many ticks the message has been up. The original spends the first nine of them fading its panel in
/// from three-quarters opacity to a sixth, and holds the category icon back until they have passed.
/// </param>
/// <param name="IsCutscene">
/// Whether this is a cutscene message — the flag the original calls <c>+454</c>. Those are drawn as a
/// subtitle low on the screen, are whole from their first frame, and are kept out of the computer log.
/// </param>
/// <param name="Portrait">
/// The sprite drawn beside the text, by its name in the status archive, or empty. Only a message the game posts
/// itself carries one -- the ammunition box's (<c>0x46D990</c>); <c>0x4860F0</c> draws it once the panel has opened,
/// sets the text after it and sizes the panel to it.
/// </param>
public readonly record struct StatusMessageDisplay(
    string Text,
    int RevealedCharacters,
    int DisplayFrame,
    bool IsCutscene,
    string Portrait = "");

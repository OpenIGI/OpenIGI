using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// How a game mode looks wherever the multiplayer page names it: a short code for its badge, and a colour of its own, so a
/// list of games reads by colour before it is read by word -- co-op green, the versus modes each their own.
/// </summary>
internal static class ModeLook
{
    /// <summary>A mode's code, as its badge says it: a few capitals.</summary>
    /// <param name="mode">The mode's id, as a line carries it.</param>
    /// <returns>The code; a dash for no mode.</returns>
    public static string Code(string mode) => mode switch
    {
        GameModes.Coop => "CO-OP",
        GameModes.TeamDeathmatch => "TDM",
        GameModes.FreeForAll => "FFA",
        GameModes.Elimination => "ELIM",
        GameModes.SearchAndDestroy => "S&D",
        GameModes.Objective => "OBJ",
        { Length: 0 } => "—",
        _ => mode.Length > 5 ? mode[..5].ToUpperInvariant() : mode.ToUpperInvariant(),
    };

    /// <summary>A mode's colour.</summary>
    /// <param name="mode">The mode's id, as a line carries it.</param>
    /// <returns>The colour; a quiet grey for a mode the page does not know.</returns>
    public static Bgra32 Colour(string mode) => mode switch
    {
        GameModes.Coop => UiPalette.Success,
        GameModes.TeamDeathmatch => Bgra32.FromRgb(118, 176, 240),
        GameModes.FreeForAll => UiPalette.Danger,
        GameModes.Elimination => Bgra32.FromRgb(186, 150, 240),
        GameModes.SearchAndDestroy or GameModes.Objective => UiPalette.Accent,
        _ => UiPalette.TextMuted,
    };
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Servers;

/// <summary>
/// How a game mode looks wherever the multiplayer page names it: a short code for its badge, and a colour of its own, so a
/// list of games reads by colour before it is read by word -- co-op green, the versus modes each their own.
/// </summary>
public static class ModeLook
{
    /// <summary>A mode's code, as its badge says it: a few capitals.</summary>
    /// <param name="mode">The mode's id, as a line carries it.</param>
    /// <returns>The code; a dash for no mode.</returns>
    public static string Code(string mode) => mode switch
    {
        MatchModes.Coop => "CO-OP",
        MatchModes.TeamDeathmatch => "TDM",
        MatchModes.FreeForAll => "FFA",
        MatchModes.Elimination => "ELIM",
        MatchModes.SearchAndDestroy => "S&D",
        MatchModes.Objective => "OBJ",
        { Length: 0 } => "—",
        _ => mode.Length > 5 ? mode[..5].ToUpperInvariant() : mode.ToUpperInvariant(),
    };

    /// <summary>A mode's colour.</summary>
    /// <param name="mode">The mode's id, as a line carries it.</param>
    /// <returns>The colour; a quiet grey for a mode the page does not know.</returns>
    public static Bgra32 Colour(string mode) => mode switch
    {
        MatchModes.Coop => UiPalette.Success,
        MatchModes.TeamDeathmatch => Bgra32.FromRgb(118, 176, 240),
        MatchModes.FreeForAll => UiPalette.Danger,
        MatchModes.Elimination => Bgra32.FromRgb(186, 150, 240),
        MatchModes.SearchAndDestroy or MatchModes.Objective => UiPalette.Accent,
        _ => UiPalette.TextMuted,
    };

    /// <summary>Draws a mode's badge: its code in its colour, in a box outlined in it, on a dark fill.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="topLeft">The badge's top-left corner, in pixels.</param>
    /// <param name="mode">The mode's id.</param>
    /// <param name="appear">How far it has faded in, 0 to 1.</param>
    /// <returns>The badge's box.</returns>
    public static UiRect DrawBadge(UiPainter painter, UiTheme theme, UiVec2 topLeft, string mode, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float size = context.Px(13f);
        string code = Code(mode);
        Bgra32 colour = UiPalette.Fade(Colour(mode), appear);
        float width = UiPainter.MeasureLabel(theme.Heading, code, size, 0.08f);
        var box = new UiRect(topLeft.X, topLeft.Y, width + context.Px(16f), context.Px(24f));
        painter.FillRect(box, Bgra32.FromRgba(10, 12, 13, (byte)(89f * appear)));
        painter.StrokeRect(box, context.Px(1f), colour);
        painter.DrawLabel(theme.Heading, code, new UiVec2(box.X + context.Px(8f), UiRowStyle.CapsCenterY(theme.Heading, size, box)), size, colour, 0.08f);
        return box;
    }
}

using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// The player's own match in numbers, across the card at its end: where it placed, its kills, deaths and kills a death -- each
/// figure large over its small label -- with MVP lit in the accent first for the one who finished on top.
/// </summary>
public static class MatchSummaryRow
{
    /// <summary>Draws the row, centred on a point across.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for its type.</param>
    /// <param name="centre">The row's middle, across.</param>
    /// <param name="top">The row's top.</param>
    /// <param name="place">Where the player placed, from 1.</param>
    /// <param name="players">How many played.</param>
    /// <param name="kills">Its kills.</param>
    /// <param name="deaths">Its deaths.</param>
    /// <param name="ratio">Its kills a death, as said.</param>
    /// <param name="best">Whether it finished on top: the match's most valuable player.</param>
    public static void Draw(UiPainter painter, UiTheme theme, float centre, float top, int place, int players, int kills, int deaths, string ratio, bool best)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        (string Label, string Value, bool Lit)[] figures =
        [
            ("PLACE", string.Create(CultureInfo.InvariantCulture, $"{Ordinal(place)} / {players}"), best),
            ("KILLS", kills.ToString(CultureInfo.InvariantCulture), false),
            ("DEATHS", deaths.ToString(CultureInfo.InvariantCulture), false),
            ("K/D", ratio, false),
        ];

        float valueSize = context.Px(26f);
        float labelSize = context.Px(12f);
        float column = context.Px(118f);
        float x = centre - (((figures.Length * column) + (best ? column : 0f)) * 0.5f);
        if (best)
        {
            var chip = new UiRect(x + context.Px(14f), top + context.Px(4f), column - context.Px(28f), context.Px(30f));
            painter.FillCutRect(chip, context.Px(7f), UiCorners.TopLeft | UiCorners.BottomRight, UiPalette.Accent);
            Centred(painter, theme.Heading, "MVP", chip.X + (chip.Width * 0.5f), UiRowStyle.CapsCenterY(theme.Heading, context.Px(16f), chip), context.Px(16f), Bgra32.FromRgb(20, 16, 10), 0.16f);
            x += column;
        }

        foreach ((string label, string value, bool lit) in figures)
        {
            float middle = x + (column * 0.5f);
            Centred(painter, theme.Display, value, middle, top, valueSize, lit ? UiPalette.Accent : UiPalette.TextBright, 0.02f);
            Centred(painter, theme.Mono, label, middle, top + context.Px(32f), labelSize, UiPalette.TextMuted, 0.18f);
            x += column;
        }
    }

    /// <summary>A place said as the card says it: 1ST, 2ND, 3RD, 4TH, 11TH.</summary>
    /// <param name="rank">The place, from 1.</param>
    /// <returns>The words.</returns>
    public static string Ordinal(int rank) => rank switch
    {
        <= 0 => "-",
        _ when rank % 100 is 11 or 12 or 13 => $"{rank}TH",
        _ => (rank % 10) switch { 1 => $"{rank}ST", 2 => $"{rank}ND", 3 => $"{rank}RD", _ => $"{rank}TH" },
    };

    private static void Centred(UiPainter painter, UiFont font, string text, float centre, float y, float size, Bgra32 colour, float tracking)
    {
        float width = UiPainter.MeasureLabel(font, text, size, tracking);
        painter.DrawLabel(font, text, new UiVec2(centre - (width * 0.5f), y), size, colour, tracking);
    }
}

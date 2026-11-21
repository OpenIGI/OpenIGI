using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// One frame's drawing of the session's overlays -- the scoreboard, the chat, the revive marks, the menus a match opens -- in the
/// played title's own look: its glass and corners (<see cref="HudPanel"/>), its colours (<see cref="HudPalette"/>), and its
/// status display's bitmap font (<see cref="TitleText"/>). Every overlay is drawn from these few pieces, so both titles' sets
/// read as one design, and as the heads-up display they stand over.
/// </summary>
internal sealed class HudOverlay
{
    /// <summary>The size of a small label -- a column's head, a key's name -- in layout pixels.</summary>
    public const float Small = 14f;

    /// <summary>The size of a line of running text -- a name, a chat line -- in layout pixels.</summary>
    public const float Body = 18f;

    /// <summary>The size of a heading or a figure, in layout pixels.</summary>
    public const float Large = 28f;

    /// <summary>The size of a result across the screen, in layout pixels.</summary>
    public const float Huge = 52f;

    /// <summary>Initializes a new instance of the <see cref="HudOverlay"/> class.</summary>
    /// <param name="painter">The painter, over the frame's batch.</param>
    /// <param name="context">The frame's layout.</param>
    /// <param name="theme">The played title's look.</param>
    /// <param name="text">The title's text, begun on the painter.</param>
    /// <param name="seconds">The time now, for the theme's shimmer and the pulses.</param>
    public HudOverlay(UiPainter painter, LayoutContext context, HudTheme theme, TitleText text, float seconds)
    {
        Painter = painter;
        Context = context;
        Theme = theme;
        Text = text;
        Seconds = seconds;
    }

    /// <summary>Gets the painter.</summary>
    public UiPainter Painter { get; }

    /// <summary>Gets the frame's layout.</summary>
    public LayoutContext Context { get; }

    /// <summary>Gets the played title's look.</summary>
    public HudTheme Theme { get; }

    /// <summary>Gets the title's colours.</summary>
    public HudPalette Ink => Theme.Palette;

    /// <summary>Gets the ink of what is said quietly -- a label, a column's head, a hint: the title's colour, softened.</summary>
    public Bgra32 Quiet => Fade(Ink.Primary, 0.72f);

    /// <summary>Gets the title's text.</summary>
    public TitleText Text { get; }

    /// <summary>Gets the time now, in seconds.</summary>
    public float Seconds { get; }

    /// <summary>Gets the screen's safe area.</summary>
    public UiRect Safe => Context.SafeRect;

    /// <summary>Gets how many pixels one of the theme's authored pixels is drawn as on the overlays.</summary>
    public float Motif => Context.Px(1.5f);

    /// <summary>Gets how thick a rule is, in pixels.</summary>
    public float Hairline => MathF.Max(1f, MathF.Round(Theme.Motifs.Hairline * Motif));

    /// <summary>Gets a slow pulse, nought to one, for what wants the eye: a fuse, an alarm.</summary>
    public float Pulse => 0.5f + (0.5f * MathF.Sin(Seconds * MathF.PI * 2.4f));

    /// <summary>The corners the theme cuts.</summary>
    public UiCorners Corners => Theme.Motifs.Corner == HudCorner.Cut ? UiCorners.TopLeft | UiCorners.BottomRight : UiCorners.All;

    /// <summary>Layout pixels in screen pixels.</summary>
    /// <param name="units">Layout pixels.</param>
    /// <returns>Screen pixels.</returns>
    public float Px(float units) => Context.Px(units);

    /// <summary>A colour at part of its opacity.</summary>
    /// <param name="colour">The colour.</param>
    /// <param name="opacity">How much of it, nought to one.</param>
    /// <returns>The colour faded.</returns>
    public static Bgra32 Fade(Bgra32 colour, float opacity) => HudDrawContext.Fade(colour, opacity);

    /// <summary>The ink of a side, as this player sees the sides: its own the ally's, the other the enemy's, nobody's plain.</summary>
    /// <param name="team">The side.</param>
    /// <param name="ownTeam">This player's side, or a negative number for none.</param>
    /// <param name="firstTeam">The side that stands first where this player is on neither: drawn as the ally's.</param>
    /// <returns>The ink.</returns>
    public Bgra32 SideInk(int team, int ownTeam, int firstTeam)
    {
        if (team < 0)
        {
            return Ink.Plain;
        }

        bool ours = ownTeam >= 0 ? team == ownTeam : team == firstTeam;
        return ours ? Ink.Ally : Ink.Enemy;
    }

    /// <summary>Draws a sheet the session lays over the picture to be read: the title's glass, darker, cut its way and bracketed.</summary>
    /// <param name="rect">Where it stands.</param>
    /// <param name="opacity">How far it has faded in, nought to one.</param>
    public void Sheet(UiRect rect, float opacity = 1f) => HudPanel.Draw(Painter, Theme, Motif, Seconds, rect, opacity, shade: 0.58f);

    /// <summary>Draws a strip of glass with a rule under it, a sheet's header.</summary>
    /// <param name="rect">Where it stands.</param>
    /// <param name="opacity">How far it has faded in, nought to one.</param>
    public void HeaderStrip(UiRect rect, float opacity = 1f) => HudPanel.Strip(Painter, Theme, Hairline, rect, ruleBelow: true, opacity);

    /// <summary>Draws a rule: one thin line, or the theme's pair.</summary>
    /// <param name="x">Its left end.</param>
    /// <param name="y">Its height.</param>
    /// <param name="width">Its length.</param>
    /// <param name="colour">Its colour.</param>
    public void Rule(float x, float y, float width, Bgra32 colour) => HudPanel.Rule(Painter, Theme, Motif, x, y, width, colour);

    /// <summary>Draws brackets at a rectangle's corners.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="colour">Their colour.</param>
    /// <param name="length">How far each arm runs, in layout pixels.</param>
    public void Brackets(UiRect rect, Bgra32 colour, float length = 6f) => HudPanel.Brackets(Painter, Theme, Motif, rect, Px(length), colour);

    /// <summary>Fills a rectangle with the theme's corners cut.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="cut">How deep a cut is, in the theme's pixels; the theme's own when omitted.</param>
    public void Plate(UiRect rect, Bgra32 colour, float? cut = null) =>
        Painter.FillCutRect(rect, (cut ?? (Theme.Motifs.CornerSize * 0.6f)) * Motif, Corners, colour);

    /// <summary>
    /// Lays a line's row: nothing for an ordinary one; this player's own washed in the highlight and bracketed; one under the
    /// pointer washed lighter.
    /// </summary>
    /// <param name="rect">The row.</param>
    /// <param name="own">Whether it is this player's own.</param>
    /// <param name="over">Whether the pointer is over it.</param>
    /// <param name="ink">The ink it is lit in; the highlight when omitted.</param>
    public void Row(UiRect rect, bool own, bool over = false, Bgra32? ink = null)
    {
        Bgra32 lit = ink ?? Ink.Highlight;
        if (own)
        {
            Plate(rect, Fade(lit, 0.14f));
            Brackets(rect, Fade(lit, 0.85f), 5f);
        }
        else if (over)
        {
            Plate(rect, Fade(lit, 0.08f));
        }
    }

    /// <summary>
    /// Lays a wash behind words set straight on the picture -- a chat line, a feed row: the title's glass darkened at its left,
    /// fading out to the right, with a tick of a colour down its leading edge.
    /// </summary>
    /// <param name="rect">Where it runs.</param>
    /// <param name="tick">The tick's colour.</param>
    /// <param name="opacity">How much of it shows, nought to one.</param>
    public void Wash(UiRect rect, Bgra32 tick, float opacity = 1f)
    {
        Bgra32 deep = Fade(Bgra32.FromRgba(0, 0, 0, 200), opacity);
        Bgra32 glass = Fade(Ink.Glass.WithAlpha(150), opacity);
        Span<UiGradientStop> stops = [new(0f, deep), new(0.6f, Fade(deep, 0.6f)), new(1f, deep.WithAlpha(0))];
        Painter.FillGradient(rect, stops, UiGradientDirection.Horizontal);
        Span<UiGradientStop> tint = [new(0f, glass), new(1f, glass.WithAlpha(0))];
        Painter.FillGradient(rect, tint, UiGradientDirection.Horizontal);
        Painter.FillRect(new UiRect(rect.X, rect.Y, MathF.Max(2f, Px(3f)), rect.Height), Fade(tick, opacity));
    }

    /// <summary>Lays a band across the screen that fades out at its ends, behind a line that runs across it: a result, a warning.</summary>
    /// <param name="rect">Where it runs.</param>
    /// <param name="colour">Its colour at its middle.</param>
    public void Band(UiRect rect, Bgra32 colour)
    {
        Bgra32 clear = colour.WithAlpha(0);
        Span<UiGradientStop> stops = [new(0f, clear), new(0.25f, colour), new(0.75f, colour), new(1f, clear)];
        Painter.FillGradient(rect, stops, UiGradientDirection.Horizontal);
    }

    /// <summary>Lays the glass band a result or the team menu stands on: the band, dark, under the title's tint, and a rule at each edge.</summary>
    /// <param name="rect">Where it runs.</param>
    /// <param name="edge">The rules' colour.</param>
    public void GlassBand(UiRect rect, Bgra32 edge)
    {
        Band(rect, Bgra32.FromRgba(0, 0, 0, 190));
        Band(rect, Ink.Glass.WithAlpha(160));
        Band(new UiRect(rect.X, rect.Y, rect.Width, Hairline), edge);
        Band(new UiRect(rect.X, rect.Bottom - Hairline, rect.Width, Hairline), edge);
        if (Theme.Motifs.DoubleRules)
        {
            Band(new UiRect(rect.X, rect.Y + (Hairline * 3f), rect.Width, Hairline), Fade(edge, 0.4f));
            Band(new UiRect(rect.X, rect.Bottom - (Hairline * 4f), rect.Width, Hairline), Fade(edge, 0.4f));
        }
    }

    /// <summary>Measures a key's cap.</summary>
    /// <param name="key">The key's name.</param>
    /// <returns>Its width, in pixels.</returns>
    public float KeyCapWidth(string key) => MathF.Max(KeyCapHeight, Text.MeasureAt(Text.Capitals(key), Px(Small)) + Px(14f));

    /// <summary>Gets a key cap's height, in pixels.</summary>
    public float KeyCapHeight => Text.LineHeightAt(Px(Small)) + Px(10f);

    /// <summary>Draws a key's cap: its name in the title's type in a bracketed box of the title's glass.</summary>
    /// <param name="key">The key's name.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="lit">Whether it is lit, as a key just pressed or a choice made is.</param>
    /// <returns>Its box.</returns>
    public UiRect KeyCap(string key, float x, float y, bool lit = false)
    {
        var cap = new UiRect(x, y, KeyCapWidth(key), KeyCapHeight);
        Plate(cap, lit ? Ink.Highlight : Fade(Ink.Primary, 0.14f), Theme.Motifs.CornerSize * 0.4f);
        if (!lit)
        {
            Brackets(cap, Fade(Ink.Primary, 0.8f), 4f);
        }

        string name = Text.Capitals(key);
        float size = Px(Small);
        Text.DrawCentredAt(name, cap.X + (cap.Width * 0.5f), cap.Y + ((cap.Height - Text.LineHeightAt(size)) * 0.5f), size, lit ? Bgra32.FromRgb(8, 10, 8) : Ink.Highlight);
        return cap;
    }

    /// <summary>Draws a small word in a box beside a name -- HOST, YOU, a dead player's S -- outlined, or filled for the strongest.</summary>
    /// <param name="word">The word.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="filled">Whether its box is filled.</param>
    /// <returns>Its right edge.</returns>
    public float Tag(string word, float x, float y, Bgra32 colour, bool filled = false)
    {
        float size = Px(Small);
        float height = Text.LineHeightAt(size) + Px(4f);
        var box = new UiRect(x, y, Text.MeasureAt(word, size) + Px(10f), height);
        if (filled)
        {
            Plate(box, colour, Theme.Motifs.CornerSize * 0.4f);
        }
        else
        {
            Painter.StrokeRect(box, Hairline, Fade(colour, 0.85f));
        }

        Text.DrawAt(word, box.X + Px(5f), box.Y + Px(2f), size, filled ? Bgra32.FromRgb(8, 10, 8) : colour);
        return box.Right;
    }

    /// <summary>Draws a meter: a thin track, filled as far as a value, with ticks at its ends as the title's gauges are.</summary>
    /// <param name="rect">The track.</param>
    /// <param name="value">How full, nought to one.</param>
    /// <param name="colour">The fill's colour.</param>
    public void Meter(UiRect rect, float value, Bgra32 colour)
    {
        Painter.FillRect(rect, Fade(Ink.Dim, 0.5f));
        float fill = rect.Width * Math.Clamp(value, 0f, 1f);
        if (fill > 0f)
        {
            Painter.FillRect(new UiRect(rect.X, rect.Y, fill, rect.Height), colour);
        }

        float tick = MathF.Max(1f, Hairline);
        float rise = MathF.Max(rect.Height + Px(4f), Px(8f));
        float top = rect.Y + ((rect.Height - rise) * 0.5f);
        Painter.FillRect(new UiRect(rect.X - tick, top, tick, rise), Ink.Primary);
        Painter.FillRect(new UiRect(rect.Right, top, tick, rise), Ink.Primary);
    }

    /// <summary>Draws four rising bars, as many lit as a round trip deserves.</summary>
    /// <param name="area">Where they stand.</param>
    /// <param name="lit">How many are lit, nought to four.</param>
    /// <param name="colour">The lit bars' colour.</param>
    public void Signal(UiRect area, int lit, Bgra32 colour)
    {
        float bar = MathF.Max(2f, Px(4f));
        float gap = (area.Width - (bar * 4f)) / 3f;
        for (int i = 0; i < 4; i++)
        {
            float h = area.Height * (0.4f + (0.2f * i));
            Painter.FillRect(new UiRect(area.X + (i * (bar + gap)), area.Bottom - h, bar, h), i < lit ? colour : Fade(Ink.Dim, 0.6f));
        }
    }

    /// <summary>The colour of a round trip: the ally's while unnoticed, the accent while it shows, the danger's when it is in the way.</summary>
    /// <param name="ping">The round trip, in milliseconds.</param>
    /// <returns>The colour.</returns>
    public Bgra32 PingInk(int ping) => ping < 60 ? Ink.Ally : ping < 110 ? Ink.Accent : Ink.Danger;

    /// <summary>How many of four bars a round trip lights.</summary>
    /// <param name="ping">The round trip, in milliseconds.</param>
    /// <returns>The bars.</returns>
    public static int PingBars(int ping) => ping < 60 ? 4 : ping < 110 ? 3 : ping < 170 ? 2 : 1;

    /// <summary>Draws the theme's diamond -- the HUD's own mark for a state -- centred on a point.</summary>
    /// <param name="centre">Its middle.</param>
    /// <param name="radius">Its half width, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    public void Diamond(UiVec2 centre, float radius, Bgra32 colour) => Painter.FillDiamond(centre, radius, colour);

    /// <summary>Draws the arrow beside a mark held on the screen's edge, pointing the way to what it marks, ringed in dark.</summary>
    /// <param name="centre">The mark's middle.</param>
    /// <param name="clear">How far out from the middle the arrow starts.</param>
    /// <param name="way">The way to point, any length.</param>
    /// <param name="colour">Its colour.</param>
    public void EdgeArrow(UiVec2 centre, float clear, UiVec2 way, Bgra32 colour)
    {
        float length = MathF.Sqrt((way.X * way.X) + (way.Y * way.Y));
        if (length < 0.001f)
        {
            return;
        }

        var unit = new UiVec2(way.X / length, way.Y / length);
        var side = new UiVec2(-unit.Y, unit.X);
        float arrow = Px(9f);
        var foot = new UiVec2(centre.X + (unit.X * clear), centre.Y + (unit.Y * clear));
        var tip = new UiVec2(foot.X + (unit.X * arrow * 1.3f), foot.Y + (unit.Y * arrow * 1.3f));
        var left = new UiVec2(foot.X + (side.X * arrow), foot.Y + (side.Y * arrow));
        var right = new UiVec2(foot.X - (side.X * arrow), foot.Y - (side.Y * arrow));
        var middle = new UiVec2((tip.X + left.X + right.X) / 3f, (tip.Y + left.Y + right.Y) / 3f);
        UiVec2 Grown(UiVec2 corner) => new(middle.X + ((corner.X - middle.X) * 1.4f), middle.Y + ((corner.Y - middle.Y) * 1.4f));
        Painter.FillTriangle(Grown(tip), Grown(left), Grown(right), Bgra32.FromRgba(0, 0, 0, (byte)(190 * colour.A / 255)));
        Painter.FillTriangle(tip, left, right, colour);
    }

    /// <summary>How far, as a mark says it beside a name: <c>42 M</c>.</summary>
    /// <param name="metres">The distance.</param>
    /// <returns>The words.</returns>
    public static string Distance(double metres) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{metres:0} M");

    /// <summary>
    /// Draws a line of words centred on a point across, straight on the picture, with a quieter second part after the first
    /// (a name, and how far).
    /// </summary>
    /// <param name="centreX">The line's middle, across.</param>
    /// <param name="top">The line's top.</param>
    /// <param name="first">The first part.</param>
    /// <param name="second">The quieter part, or <see langword="null"/>.</param>
    /// <param name="colour">The first part's colour.</param>
    /// <param name="opacity">How opaque the line is, nought to one.</param>
    /// <param name="size">Its size, in layout pixels.</param>
    /// <returns>The line's box.</returns>
    public UiRect Caption(float centreX, float top, string first, string? second, Bgra32 colour, float opacity = 1f, float size = Body)
    {
        float px = Px(size);
        float gap = Px(8f);
        string name = Text.Prose(first);
        float firstWidth = Text.MeasureAt(name, px);
        float total = firstWidth + (second is null ? 0f : gap + Text.MeasureAt(second, px));
        float x = centreX - (total * 0.5f);
        Text.DrawAt(name, x, top, px, Fade(colour, opacity), shadow: true);
        if (second is not null)
        {
            Text.DrawAt(second, x + firstWidth + gap, top, px, Fade(Quiet, opacity), shadow: true);
        }

        return new UiRect(x, top, total, Text.LineHeightAt(px));
    }
}

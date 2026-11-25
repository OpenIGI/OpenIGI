using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// How much room a board's page has, as the menu's arrangements name it: a monitor's (the board with a panel beside it on the
/// selected row), a small laptop's (the board alone), or a phone's on its side (one column, the list and the player's own row).
/// </summary>
public enum BoardSize
{
    /// <summary>A monitor: the board and, beside it, the selected row's panel.</summary>
    Wide,

    /// <summary>A small laptop, a 4:3 screen: the board alone.</summary>
    Medium,

    /// <summary>A phone on its side: the list beside the top three, short rows, nothing that can wait.</summary>
    Compact,
}

/// <summary>
/// The boards' look, shared by every part of them: the medals' colours, the panels, the players' marks and tags, the week's
/// movement, a rank's stripes and the figures' faces.
/// </summary>
public static class BoardLook
{
    /// <summary>The tracking of a caption in capitals, as an em.</summary>
    public const float CaptionTracking = 0.15f;

    /// <summary>Gets the first place's colour.</summary>
    public static Bgra32 Gold => Bgra32.FromRgb(242, 204, 94);

    /// <summary>Gets the second place's colour.</summary>
    public static Bgra32 Silver => Bgra32.FromRgb(201, 206, 214);

    /// <summary>Gets the third place's colour.</summary>
    public static Bgra32 Bronze => Bgra32.FromRgb(207, 140, 88);

    /// <summary>Gets the ink on a filled accent: a button's caption, the YOU tag's.</summary>
    public static Bgra32 OnAccent => Bgra32.FromRgb(23, 19, 12);

    /// <summary>A place's colour: gold, silver and bronze for the first three, the accent after.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="place">The place, from one.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Medal(UiTheme theme, int place)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return place switch
        {
            1 => Gold,
            2 => Silver,
            3 => Bronze,
            _ => theme.Accent,
        };
    }

    /// <summary>Draws a board's panel: dark glass, edged with a hairline.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">Where.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void Panel(UiPainter painter, UiRect rect, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.FillRect(rect, Bgra32.FromRgba(17, 18, 19, (byte)(224f * appear)));
        painter.StrokeRect(rect, painter.Context.Px(1f), UiPalette.White(0.1f * appear));
    }

    /// <summary>
    /// Gets whether a shape's box stands wholly inside the clip: triangles, quads and icons are drawn unclipped, so a row
    /// scrolled half out of its list leaves them out rather than drawing them over what is outside it.
    /// </summary>
    /// <param name="painter">The painter.</param>
    /// <param name="box">The shape's box.</param>
    /// <returns>Whether it may be drawn.</returns>
    public static bool InClip(UiPainter painter, UiRect box)
    {
        ArgumentNullException.ThrowIfNull(painter);
        UiRect clip = painter.Clip;
        return box.X >= clip.X - 0.5f && box.Y >= clip.Y - 0.5f && box.Right <= clip.Right + 0.5f && box.Bottom <= clip.Bottom + 0.5f;
    }

    /// <summary>Draws an icon where it stands wholly inside the clip.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="centre">Its middle.</param>
    /// <param name="size">Its size, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    public static void Icon(UiPainter painter, KitIcon icon, UiVec2 centre, float size, Bgra32 ink)
    {
        if (!InClip(painter, new UiRect(centre.X - (size / 2f), centre.Y - (size / 2f), size, size)))
        {
            return;
        }

        if (icon == KitIcon.Eye)
        {
            Eye(painter, centre, size, ink);
            return;
        }

        KitIcons.Draw(painter, icon, centre, size, ink);
    }

    // An eye: an almond's outline, its two lids as curves meeting at the corners, and the pupil in its middle.
    private static void Eye(UiPainter painter, UiVec2 centre, float size, Bgra32 ink)
    {
        const int Steps = 10;
        float half = size * 0.5f;
        float lid = size * 0.32f;
        Span<UiVec2> upper = stackalloc UiVec2[Steps + 1];
        Span<UiVec2> lower = stackalloc UiVec2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float t = i / (float)Steps;
            float x = centre.X - half + (t * size);
            float bulge = MathF.Sin(t * MathF.PI) * lid;
            upper[i] = new UiVec2(x, centre.Y - bulge);
            lower[i] = new UiVec2(x, centre.Y + bulge);
        }

        float stroke = MathF.Max(1f, size * 0.09f);
        painter.StrokeSoftPolyline(upper, stroke, ink);
        painter.StrokeSoftPolyline(lower, stroke, ink);
        painter.FillDot(centre, size * 0.15f, ink);
    }

    /// <summary>Draws a soft glow about a rectangle: rings fading out from its edge.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="colour">The glow's colour.</param>
    /// <param name="spread">How far it reaches, in design units.</param>
    /// <param name="strength">Its opacity at the edge, nought to one.</param>
    public static void Glow(UiPainter painter, UiRect rect, Bgra32 colour, float spread, float strength)
    {
        ArgumentNullException.ThrowIfNull(painter);
        const int Rings = 10;
        float step = painter.Context.Px(spread) / Rings;
        for (int i = 0; i < Rings; i++)
        {
            float fade = 1f - ((i + 0.5f) / Rings);
            var ring = new UiRect(rect.X - ((i + 1) * step), rect.Y - ((i + 1) * step), rect.Width + (2f * (i + 1) * step), rect.Height + (2f * (i + 1) * step));
            painter.StrokeRect(ring, step, colour.WithAlpha((byte)(255f * strength * fade * fade)));
        }
    }

    /// <summary>Draws a figure's tile: a quieter panel than a board's.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">Where.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void Tile(UiPainter painter, UiRect rect, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.FillRect(rect, UiPalette.InkAt(0.25f * appear));
        painter.StrokeRect(rect, painter.Context.Px(1f), UiPalette.White(0.1f * appear));
    }

    /// <summary>Draws a player's mark: their picture, or their initial on dark, in a square edged in a colour.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="box">The square, in pixels.</param>
    /// <param name="name">Their name, for the initial.</param>
    /// <param name="picture">Their picture, or null.</param>
    /// <param name="edge">The edge's and the initial's colour.</param>
    /// <param name="edgeWidth">The edge's width, in design units.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void Mark(UiPainter painter, UiTheme theme, UiRect box, string name, UiPicture? picture, Bgra32 edge, float edgeWidth = 1f, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        painter.FillRect(box, UiPalette.InkAt(0.4f * appear));
        if (picture is { } shown)
        {
            shown.DrawCover(painter, box, appear);
        }
        else
        {
            string initial = (name ?? string.Empty).Trim() is { Length: > 0 } trimmed ? trimmed[..1].ToUpperInvariant() : "?";
            float size = box.Height * 0.45f;
            float width = theme.Display.MeasureWidth(initial, size);
            painter.DrawText(theme.Display, initial, new UiVec2(box.Center.X - (width / 2f), UiRowStyle.CapsCenterY(theme.Display, size, box)), size, UiPalette.Fade(edge, appear));
        }

        painter.StrokeRect(box, painter.Context.Px(edgeWidth), UiPalette.Fade(edge, appear));
    }

    /// <summary>Draws a small tag in capitals -- DISCORD, YOU, TOP 10, WORLD RECORD -- outlined, or filled where given a fill.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">The line it sits on the middle of, in pixels.</param>
    /// <param name="text">What it says.</param>
    /// <param name="ink">Its text's colour.</param>
    /// <param name="edge">Its edge's colour.</param>
    /// <param name="fill">Its fill, or null for none.</param>
    /// <param name="size">Its text's size, in design units.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Tag(UiPainter painter, UiTheme theme, float left, float middle, string text, Bgra32 ink, Bgra32 edge, Bgra32? fill = null, float size = 9f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float px = context.Px(size);
        string caps = text.ToUpperInvariant();
        float width = UiPainter.MeasureLabel(theme.Mono, caps, px, 0.1f) + context.Px(12f);
        float height = px + context.Px(8f);
        var box = new UiRect(left, middle - (height / 2f), width, height);
        if (fill is { } filled)
        {
            painter.FillRect(box, filled);
        }

        painter.StrokeRect(box, context.Px(1f), edge);
        painter.DrawLabel(theme.Mono, caps, new UiVec2(box.X + context.Px(6f), UiRowStyle.CapsCenterY(theme.Mono, px, box)), px, ink, 0.1f);
        return box.Right;
    }

    /// <summary>Measures a <see cref="Tag"/>.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">What it says.</param>
    /// <param name="size">Its text's size, in design units.</param>
    /// <returns>Its width, in pixels.</returns>
    public static float TagWidth(LayoutContext context, UiTheme theme, string text, float size = 9f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);
        return UiPainter.MeasureLabel(theme.Mono, text.ToUpperInvariant(), context.Px(size), 0.1f) + context.Px(12f);
    }

    /// <summary>Draws the Discord tag a linked account's name carries.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <param name="size">Its text's size, in design units.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Discord(UiPainter painter, UiTheme theme, float left, float middle, float appear = 1f, float size = 9f)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Tag(painter, theme, left, middle, "Discord", UiPalette.Fade(theme.TextSecondary, appear), UiPalette.White(0.22f * appear), size: size);
    }

    /// <summary>Draws the YOU tag: filled in the accent.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float You(UiPainter painter, UiTheme theme, float left, float middle, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Bgra32 accent = UiPalette.Fade(theme.Accent, appear);
        return Tag(painter, theme, left, middle, "You", UiPalette.Fade(OnAccent, appear), accent, accent);
    }

    /// <summary>Draws a verified mark: a tick and VERIFIED (or what is given) in green on a green-washed tag.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="text">What it says.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <param name="size">Its text's size, in design units.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Verified(UiPainter painter, UiTheme theme, float left, float middle, string text = "Verified", float appear = 1f, float size = 9f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float px = context.Px(size);
        string caps = text.ToUpperInvariant();
        float tick = px * 1.1f;
        float width = UiPainter.MeasureLabel(theme.Mono, caps, px, 0.1f) + tick + context.Px(17f);
        float height = px + context.Px(8f);
        var box = new UiRect(left, middle - (height / 2f), width, height);
        Bgra32 green = theme.Success;
        painter.FillRect(box, green.WithAlpha((byte)(20f * appear)));
        painter.StrokeRect(box, context.Px(1f), green.WithAlpha((byte)(115f * appear)));
        Icon(painter, KitIcon.Check, new UiVec2(box.X + context.Px(6f) + (tick / 2f), middle), tick, UiPalette.Fade(green, appear));
        painter.DrawLabel(theme.Mono, caps, new UiVec2(box.X + context.Px(11f) + tick, UiRowStyle.CapsCenterY(theme.Mono, px, box)), px, UiPalette.Fade(green, appear), 0.1f);
        return box.Right;
    }

    /// <summary>
    /// Draws the week's movement: an arrow up and the places climbed in green, down and the places fallen quietly, a dash for
    /// none, NEW for one who held no place as the week began; nothing where the board keeps no movement.
    /// </summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="climbed">The places climbed (fallen, below nought), or null.</param>
    /// <param name="isNew">Whether they are new on the board this week.</param>
    /// <param name="size">The figure's size, in design units.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Movement(UiPainter painter, UiTheme theme, float left, float middle, int? climbed, bool isNew, float size = 12f, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float px = context.Px(size);
        if (isNew)
        {
            Bgra32 accent = UiPalette.Fade(theme.Accent, appear);
            return Tag(painter, theme, left, middle, "New", accent, accent.WithAlpha((byte)(160f * appear)), size: size * 0.75f);
        }

        if (climbed is not { } moved)
        {
            return left;
        }

        float y = UiRowStyle.CenterY(theme.Mono, px, new UiRect(left, middle - px, 0f, 2f * px));
        if (moved == 0)
        {
            return painter.DrawText(theme.Mono, "—", new UiVec2(left, y), px, UiPalette.Fade(theme.TextMuted, appear)).X;
        }

        Bgra32 ink = UiPalette.Fade(moved > 0 ? theme.Success : theme.TextMuted, appear);
        float arrow = px * 0.62f;
        var centre = new UiVec2(left + (arrow / 2f), middle);
        float half = arrow / 2f;
        if (!InClip(painter, new UiRect(left, middle - half, arrow, arrow)))
        {
            // Out of the list's view: the figure alone, which the clip cuts.
        }
        else if (moved > 0)
        {
            painter.FillTriangle(new UiVec2(centre.X - half, centre.Y + (half * 0.8f)), new UiVec2(centre.X + half, centre.Y + (half * 0.8f)), new UiVec2(centre.X, centre.Y - (half * 0.9f)), ink);
        }
        else
        {
            painter.FillTriangle(new UiVec2(centre.X - half, centre.Y - (half * 0.8f)), new UiVec2(centre.X + half, centre.Y - (half * 0.8f)), new UiVec2(centre.X, centre.Y + (half * 0.9f)), ink);
        }

        string figure = Math.Abs(moved).ToString("N0", CultureInfo.InvariantCulture);
        return painter.DrawText(theme.Mono, figure, new UiVec2(left + arrow + context.Px(3f), y), px, ink).X;
    }

    /// <summary>
    /// Draws a rank's stripes: three slanted bars, as many lit as the rank stands among the mission's ranks (the best three, the
    /// next two, the rest one), then the rank's name.
    /// </summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="lit">How many of the three are lit.</param>
    /// <param name="name">The rank's name, or empty.</param>
    /// <param name="nameSize">The name's size, in design units.</param>
    /// <param name="nameInk">The name's colour.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Stripes(UiPainter painter, UiTheme theme, float left, float middle, int lit, string name, float nameSize, Bgra32 nameInk, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float width = context.Px(4f);
        float height = context.Px(12f);
        float lean = height * 0.32f;
        float x = left;
        bool shown = InClip(painter, new UiRect(left, middle - (height / 2f), (3f * width) + (4f * context.Px(2f)) + lean, height));
        for (int i = 0; i < 3 && shown; i++)
        {
            Bgra32 ink = i < lit ? UiPalette.Fade(theme.Accent, appear) : UiPalette.White(0.22f * appear);
            float top = middle - (height / 2f);
            float bottom = middle + (height / 2f);
            painter.FillQuad(new UiVec2(x + lean, top), new UiVec2(x + lean + width, top), new UiVec2(x + width, bottom), new UiVec2(x, bottom), ink);
            x += width + context.Px(2f);
        }

        if (name.Length == 0)
        {
            return x;
        }

        float px = context.Px(nameSize);
        string caps = name.ToUpperInvariant();
        return painter.DrawLabel(theme.Heading, caps, new UiVec2(x + context.Px(6f), UiRowStyle.CapsCenterY(theme.Heading, px, new UiRect(x, middle - px, 0f, 2f * px))), px, UiPalette.Fade(nameInk, appear), 0.1f).X;
    }

    /// <summary>How many of a rank's three stripes are lit: the mission's best rank all three, the next two, the rest one.</summary>
    /// <param name="rank">The rank, from nought (the lowest) up.</param>
    /// <param name="ranks">How many ranks the mission has.</param>
    /// <returns>One to three; nought for no rank.</returns>
    public static int StripesLit(int rank, int ranks) => rank < 0 ? 0 : Math.Clamp(3 - (ranks - 1 - rank), 1, 3);

    /// <summary>Draws a caption in capitals in the mono face: a panel's or a column's heading.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">What it says.</param>
    /// <param name="topLeft">Where, in pixels.</param>
    /// <param name="size">Its size, in design units.</param>
    /// <param name="ink">Its colour.</param>
    /// <returns>Where it ends.</returns>
    public static UiVec2 Caption(UiPainter painter, UiTheme theme, string text, UiVec2 topLeft, float size, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        return painter.DrawLabel(theme.Mono, text.ToUpperInvariant(), topLeft, painter.Context.Px(size), ink, CaptionTracking);
    }

    /// <summary>Measures a <see cref="Caption"/>.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">What it says.</param>
    /// <param name="size">Its size, in design units.</param>
    /// <returns>Its width, in pixels.</returns>
    public static float CaptionWidth(LayoutContext context, UiTheme theme, string text, float size)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);
        return UiPainter.MeasureLabel(theme.Mono, text.ToUpperInvariant(), context.Px(size), CaptionTracking);
    }

    /// <summary>Draws text right-aligned to an edge.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The face.</param>
    /// <param name="text">What it says.</param>
    /// <param name="right">Its right edge, in pixels.</param>
    /// <param name="y">Its top, in pixels.</param>
    /// <param name="px">Its size, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    /// <returns>Its left edge.</returns>
    public static float Right(UiPainter painter, UiFont font, string text, float right, float y, float px, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        float left = right - font.MeasureWidth(text, px);
        painter.DrawText(font, text, new UiVec2(left, y), px, ink);
        return left;
    }

    /// <summary>Draws text centred on a point across.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The face.</param>
    /// <param name="text">What it says.</param>
    /// <param name="centre">Its middle across, in pixels.</param>
    /// <param name="y">Its top, in pixels.</param>
    /// <param name="px">Its size, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    public static void Centred(UiPainter painter, UiFont font, string text, float centre, float y, float px, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        painter.DrawText(font, text, new UiVec2(centre - (font.MeasureWidth(text, px) / 2f), y), px, ink);
    }

    /// <summary>Draws a thin share bar: a track, and its share filled.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="box">The track.</param>
    /// <param name="share">The share, nought to one.</param>
    /// <param name="fill">The fill's colour.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void Bar(UiPainter painter, UiRect box, double share, Bgra32 fill, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        painter.FillRect(box, UiPalette.White(0.1f * appear));
        float filled = box.Width * (float)Math.Clamp(share, 0d, 1d);
        if (filled > 0f)
        {
            painter.FillRect(new UiRect(box.X, box.Y, filled, box.Height), UiPalette.Fade(fill, appear));
        }
    }

    /// <summary>Draws the focus and hover a board's row or card shows: a wash, and the accent's edge on the one with the keys.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="bounds">The row.</param>
    /// <param name="focus">How focused, nought to one.</param>
    /// <param name="hover">Whether a pointer is over it.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void Lit(UiPainter painter, UiTheme theme, UiRect bounds, float focus, bool hover, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float hot = MathF.Max(focus, hover ? 0.6f : 0f);
        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.11f * hot * appear));
        }

        if (focus > 0.001f)
        {
            painter.StrokeRect(bounds, painter.Context.Px(1f), UiPalette.Fade(theme.Accent, focus * appear));
        }
    }

    /// <summary>Draws a key's cap inline, as a row's or a button's hint shows it.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key's name.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">Its middle, in pixels.</param>
    /// <param name="onAccent">Whether it stands on a filled accent: dark ink, no fill.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <returns>Its right edge, in pixels.</returns>
    public static float Key(UiPainter painter, UiTheme theme, string key, float left, float middle, bool onAccent = false, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float px = context.Px(10f);
        float width = MathF.Max(context.Px(20f), theme.Mono.MeasureWidth(key, px) + context.Px(10f));
        var box = new UiRect(left, middle - context.Px(10f), width, context.Px(20f));
        if (!onAccent)
        {
            painter.FillRect(box, UiPalette.InkAt(0.35f * appear));
        }

        painter.StrokeRect(box, context.Px(1f), onAccent ? OnAccent.WithAlpha((byte)(115f * appear)) : UiPalette.White(0.22f * appear));
        Bgra32 ink = onAccent ? Bgra32.FromRgba(58, 44, 18, (byte)(255f * appear)) : UiPalette.Fade(theme.TextSecondary, appear);
        painter.DrawText(theme.Mono, key, new UiVec2(box.Center.X - (theme.Mono.MeasureWidth(key, px) / 2f), UiRowStyle.CenterY(theme.Mono, px, box)), px, ink);
        return box.Right;
    }

    /// <summary>Measures a <see cref="Key"/>.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key's name.</param>
    /// <returns>Its width, in pixels.</returns>
    public static float KeyWidth(LayoutContext context, UiTheme theme, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);
        return MathF.Max(context.Px(20f), theme.Mono.MeasureWidth(key, context.Px(10f)) + context.Px(10f));
    }
}

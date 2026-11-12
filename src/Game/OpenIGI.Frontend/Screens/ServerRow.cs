using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The server browser's columns, shared by its header and its rows so the two line up.</summary>
internal static class ServerColumns
{
    /// <summary>Each column's share of the row's width: mode, name, map, players, ping.</summary>
    public static readonly float[] Weights = [0.95f, 5.0f, 2.2f, 1.65f, 1.2f];

    /// <summary>The columns' headings.</summary>
    public static readonly string[] Headings = ["MODE", "NAME", "MAP", "PLAYERS", "PING"];

    /// <summary>The column the list is ordered by when the page opens: the ping.</summary>
    public const int Ping = 4;

    /// <summary>The room left of the first column and right of the last, in design units.</summary>
    public const float Inset = 24f;

    /// <summary>Where each column starts across a row, from its left, given the row's width.</summary>
    /// <param name="width">The row's width, its insets taken off.</param>
    /// <returns>Each column's left edge.</returns>
    public static float[] Lefts(float width)
    {
        float total = Weights.Sum();
        float[] lefts = new float[Weights.Length];
        float x = 0f;
        for (int i = 0; i < Weights.Length; i++)
        {
            lefts[i] = x;
            x += width * Weights[i] / total;
        }

        return lefts;
    }
}

/// <summary>What one line of the server browser shows.</summary>
/// <param name="Name">The server's or room's name.</param>
/// <param name="Kind">Its kind, as the line under the name says it: <c>SERVER</c>, <c>ROOM</c>, <c>LAN</c>.</param>
/// <param name="ModeId">The mode it plays, by its id: what its badge and its colour come from.</param>
/// <param name="Mode">The mode it plays, by name.</param>
/// <param name="Map">The map it plays.</param>
/// <param name="Players">How many play in it.</param>
/// <param name="MaxPlayers">How many it takes.</param>
/// <param name="Ping">The round trip in milliseconds, or <see langword="null"/> while not known.</param>
/// <param name="Region">Where it is.</param>
internal sealed record ServerLine(string Name, string Kind, string ModeId, string Mode, string Map, int Players, int MaxPlayers, int? Ping, string Region)
{
    /// <summary>Gets a value indicating whether it has no room left.</summary>
    public bool Full => Players >= MaxPlayers;

    /// <summary>Gets how strong the line is, 0 to 4 bars, by its round trip.</summary>
    public int Bars => Ping switch
    {
        null => 0,
        < 60 => 4,
        < 100 => 3,
        < 150 => 2,
        _ => 1,
    };
}

/// <summary>
/// A column's heading in the server browser. Pressing it orders the list by its column; the one the list is ordered by is in
/// the accent, with a mark after it pointing the way: up for lowest first, down for highest first.
/// </summary>
internal sealed class SortHeading : Control
{
    private const float TextSize = 11f;
    private const float Tracking = 0.3f;

    private readonly string _text;

    /// <summary>Initializes a new instance of the <see cref="SortHeading"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The column's name, in capitals.</param>
    public SortHeading(UiTheme theme, string text)
        : base(theme)
    {
        _text = text;
    }

    /// <summary>Gets or sets how the list is ordered by this column: 0 not by it, 1 lowest first, -1 highest first.</summary>
    public int Order { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float size = context.Px(TextSize);
        return new UiVec2(UiPainter.MeasureLabel(Theme.Mono, _text, size, Tracking) + context.Px(16f), Theme.Mono.LineHeight(size) + context.Px(8f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        Bgra32 ink = UiPalette.Fade(Order != 0 ? Theme.Accent : hot > 0.5f ? Theme.TextBright : Theme.TextMuted, Visual.Appear);
        float size = context.Px(TextSize);
        UiVec2 end = painter.DrawLabel(Theme.Mono, _text, new UiVec2(Bounds.X, UiRowStyle.CenterY(Theme.Mono, size, Bounds)), size, ink, Tracking);
        if (Order == 0)
        {
            return;
        }

        // The mark: a small triangle, its point the way the values run.
        float half = context.Px(4f);
        float rise = context.Px(3f) * Order;
        float x = end.X + context.Px(2f) + half;
        float y = Bounds.Y + (Bounds.Height / 2f);
        painter.FillTriangle(new UiVec2(x - half, y + rise), new UiVec2(x + half, y + rise), new UiVec2(x, y - rise), ink);
    }
}

/// <summary>
/// One line of the server browser: a server or room in columns -- its mode as a badge in the mode's colour, its name over its
/// kind and region, the map, the players over a bar of places filling as it fills, the ping with bars by strength. Clicking
/// picks it; the picked line is washed in the accent, with the accent down its edge and a chevron at its end.
/// </summary>
internal sealed class ServerRow : Control
{
    private readonly ServerLine _line;
    private readonly string _under;

    /// <summary>Initializes a new instance of the <see cref="ServerRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="line">What it shows.</param>
    public ServerRow(UiTheme theme, ServerLine line)
        : base(theme)
    {
        _line = line;
        _under = line.Region.Length > 0 && line.Region != line.Kind ? $"{line.Kind} · {line.Region}" : line.Kind;
    }

    /// <summary>Gets or sets a value indicating whether it is the line picked.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets a value indicating whether the player saved it.</summary>
    public bool Saved { get; set; }

    /// <summary>Gets or sets a value indicating whether joining it takes a password, which marks it with a padlock.</summary>
    public bool Locked { get; set; }

    /// <summary>Gets or sets a value indicating whether it can't be joined (another version), which greys it.</summary>
    public bool Unjoinable { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(68f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        float mid = bounds.Y + (bounds.Height / 2f);
        if (Selected)
        {
            Span<UiGradientStop> wash = [new(0f, UiPalette.AccentAt(0.2f * appear)), new(0.7f, UiPalette.AccentAt(0.05f * appear)), new(1f, UiPalette.AccentAt(0.02f * appear))];
            painter.FillGradient(bounds, wash, UiGradientDirection.Horizontal);
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }
        else if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.045f * hot * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.05f * appear));

        float inset = context.Px(ServerColumns.Inset);
        float[] lefts = ServerColumns.Lefts(bounds.Width - (inset * 2f));
        float Left(int column) => bounds.X + inset + lefts[column];
        float Room(int column) => (column + 1 < lefts.Length ? Left(column + 1) : bounds.Right - inset) - Left(column) - context.Px(14f);
        Bgra32 Ink(Bgra32 colour) => UiPalette.Fade(Unjoinable ? Theme.TextDisabled : colour, appear);

        DrawBadge(painter, new UiRect(Left(0), mid - context.Px(15f), MathF.Min(Room(0), context.Px(64f)), context.Px(30f)), Ink(ModeLook.Colour(_line.ModeId)));
        DrawName(painter, Left(1), Room(1), Ink);

        float mapSize = context.Px(15f);
        painter.DrawText(Theme.Body, Fit(_line.Map, Theme.Body, mapSize, Room(2)), new UiVec2(Left(2), UiRowStyle.CenterY(Theme.Body, mapSize, bounds)), mapSize, Ink(Selected ? Theme.Text : Theme.TextSecondary));

        DrawPlayers(painter, Left(3), MathF.Min(Room(3), context.Px(104f)), mid, Ink);
        DrawPing(painter, Left(4), mid, Ink);

        if (Selected || hot > 0.5f)
        {
            painter.DrawChevron(new UiVec2(bounds.Right - inset + context.Px(4f), mid), context.Px(10f), UiDirection.Right, context.Px(1.5f), Ink(Selected ? Theme.Accent : Theme.TextMuted));
        }
    }

    // The mode: its code on a tint of its colour, the colour down the leading edge.
    private void DrawBadge(UiPainter painter, UiRect box, Bgra32 colour)
    {
        LayoutContext context = painter.Context;
        painter.FillRect(box, colour.WithAlpha((byte)(colour.A * 0.14f)));
        painter.FillRect(new UiRect(box.X, box.Y, context.Px(3f), box.Height), colour);
        float size = context.Px(14f);
        string code = ModeLook.Code(_line.ModeId);
        float width = UiPainter.MeasureLabel(Theme.Heading, code, size, 0.1f);
        painter.DrawLabel(Theme.Heading, code, new UiVec2(box.X + context.Px(3f) + ((box.Width - context.Px(3f) - width) / 2f), UiRowStyle.CapsCenterY(Theme.Heading, size, box)), size, colour, 0.1f);
    }

    // The name, a padlock when it asks for a password and a mark when saved; under it, its kind and where it is.
    private void DrawName(UiPainter painter, float x, float room, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float nameSize = context.Px(18f);
        float underSize = context.Px(11f);
        float gap = context.Px(5f);
        float block = Theme.Body.LineHeight(nameSize) + gap + Theme.Mono.LineHeight(underSize);
        float top = bounds.Y + ((bounds.Height - block) / 2f);
        float marks = (Locked ? context.Px(21f) : 0f) + (Saved ? context.Px(20f) : 0f);
        string name = Fit(_line.Name, Theme.Body, nameSize, room - marks);
        painter.DrawText(Theme.Body, name, new UiVec2(x, top), nameSize, ink(Selected ? Theme.TextBright : Theme.Text));

        float markX = x + Theme.Body.MeasureWidth(name, nameSize) + context.Px(12f);
        float markY = top + (Theme.Body.LineHeight(nameSize) / 2f);
        if (Locked)
        {
            // A padlock: the shackle, and the body over its feet.
            float bodyWidth = context.Px(9f);
            Bgra32 lockInk = ink(Theme.TextMuted);
            painter.StrokeRect(new UiRect(markX + context.Px(2f), markY - context.Px(6f), bodyWidth - context.Px(4f), context.Px(8f)), context.Px(1.5f), lockInk);
            painter.FillRect(new UiRect(markX, markY - context.Px(1f), bodyWidth, context.Px(7f)), lockInk);
            markX += bodyWidth + context.Px(12f);
        }

        if (Saved)
        {
            painter.FillDiamond(new UiVec2(markX + context.Px(4f), markY + context.Px(1f)), context.Px(9f), ink(Theme.Accent));
        }

        painter.DrawLabel(Theme.Mono, _under, new UiVec2(x, top + Theme.Body.LineHeight(nameSize) + gap), underSize, ink(Theme.TextMuted), 0.18f);
    }

    // The players over a bar of places, one for each the game takes up to sixteen, filled as it fills; red when full.
    private void DrawPlayers(UiPainter painter, float x, float track, float mid, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        float size = context.Px(14f);
        Bgra32 colour = _line.Full ? Theme.Danger : Theme.Text;
        string count = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_line.Players}");
        UiVec2 end = painter.DrawText(Theme.Mono, count, new UiVec2(x, mid - context.Px(5f) - Theme.Mono.LineHeight(size)), size, ink(colour));
        painter.DrawText(Theme.Mono, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/{_line.MaxPlayers}"), end, size, ink(_line.Full ? Theme.Danger : Theme.TextMuted));

        int places = Math.Clamp(_line.MaxPlayers, 1, 16);
        int taken = _line.MaxPlayers <= 0 ? 0 : Math.Clamp((int)MathF.Ceiling(_line.Players * places / (float)_line.MaxPlayers), 0, places);
        float gap = context.Px(2f);
        float width = (track - (gap * (places - 1))) / places;
        for (int i = 0; i < places; i++)
        {
            painter.FillRect(new UiRect(x + (i * (width + gap)), mid + context.Px(5f), width, context.Px(5f)), ink(i < taken ? colour : UiPalette.White(0.12f)));
        }
    }

    // The ping: four bars rising, lit by strength and coloured by it -- good, middling, poor -- then the milliseconds.
    private void DrawPing(UiPainter painter, float x, float mid, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        float[] heights = [4f, 7f, 10f, 14f];
        Bgra32 strength = _line.Bars switch
        {
            >= 3 => Theme.Success,
            2 => Theme.Warning,
            _ => Theme.Danger,
        };
        for (int i = 0; i < heights.Length; i++)
        {
            float h = context.Px(heights[i]);
            painter.FillRect(new UiRect(x, mid + context.Px(7f) - h, context.Px(3f), h), ink(i < _line.Bars ? strength : UiPalette.White(0.15f)));
            x += context.Px(5f);
        }

        float size = context.Px(14f);
        float y = UiRowStyle.CenterY(Theme.Mono, size, Bounds);
        if (_line.Ping is not { } ms)
        {
            painter.DrawText(Theme.Mono, "—", new UiVec2(x + context.Px(8f), y), size, ink(Theme.TextMuted));
            return;
        }

        UiVec2 end = painter.DrawText(Theme.Mono, ms.ToString(System.Globalization.CultureInfo.InvariantCulture), new UiVec2(x + context.Px(8f), y), size, ink(Selected ? Theme.TextBright : Theme.Text));
        float small = context.Px(10f);
        painter.DrawText(Theme.Mono, "MS", new UiVec2(end.X + context.Px(4f), y + Theme.Mono.Ascent(size) - Theme.Mono.Ascent(small)), small, ink(Theme.TextMuted));
    }

    /// <summary>Cuts text to the room it has, with an ellipsis.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">Its face.</param>
    /// <param name="size">Its size.</param>
    /// <param name="width">The room it has.</param>
    /// <returns>The text, cut when it must be.</returns>
    internal static string Fit(string text, UiFont font, float size, float width)
    {
        if (width <= 0f || font.MeasureWidth(text, size) <= width)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 1; length--)
        {
            string cut = string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
            if (font.MeasureWidth(cut, size) <= width)
            {
                return cut;
            }
        }

        return "…";
    }
}

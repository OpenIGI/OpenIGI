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
    /// <summary>Each column's share of the row's width: name, mode, map, players, ping, region.</summary>
    public static readonly float[] Weights = [5.4f, 2.0f, 2.1f, 1.15f, 1.25f, 0.85f];

    /// <summary>The columns' headings.</summary>
    public static readonly string[] Headings = ["NAME", "MODE", "MAP", "PLAYERS", "PING", "REGION"];

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
/// <param name="Kind">Its kind as the tag after the name says it: <c>SERVER</c>, <c>ROOM</c>, <c>LAN</c>.</param>
/// <param name="Mode">The mode it plays.</param>
/// <param name="Map">The map it plays.</param>
/// <param name="Players">How many play in it.</param>
/// <param name="MaxPlayers">How many it takes.</param>
/// <param name="Ping">The round trip in milliseconds, or <see langword="null"/> while not known.</param>
/// <param name="Region">Where it is.</param>
internal sealed record ServerLine(string Name, string Kind, string Mode, string Map, int Players, int MaxPlayers, int? Ping, string Region)
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
/// One line of the server browser: a server or room in columns -- its name tagged with its kind, the mode, the map, the players
/// over a bar filling as it fills, the ping with bars by strength, the region. Clicking picks it; the picked line has the
/// accent down its edge.
/// </summary>
internal sealed class ServerRow : Control
{
    private readonly ServerLine _line;

    /// <summary>Initializes a new instance of the <see cref="ServerRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="line">What it shows.</param>
    public ServerRow(UiTheme theme, ServerLine line)
        : base(theme)
    {
        _line = line;
    }

    /// <summary>Gets or sets a value indicating whether it is the line picked.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets a value indicating whether the player saved it.</summary>
    public bool Saved { get; set; }

    /// <summary>Gets or sets a value indicating whether it can't be joined (another version), which greys it.</summary>
    public bool Unjoinable { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(60f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        if (Selected)
        {
            painter.FillRect(bounds, UiPalette.Fade(UiPalette.AccentAt(0.10f), appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }
        else if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.04f * hot * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.05f * appear));

        float inset = context.Px(ServerColumns.Inset);
        float[] lefts = ServerColumns.Lefts(bounds.Width - (inset * 2f));
        float Left(int column) => bounds.X + inset + lefts[column];
        float Room(int column) => (column + 1 < lefts.Length ? Left(column + 1) : bounds.Right - inset) - Left(column) - context.Px(12f);
        Bgra32 Ink(Bgra32 colour) => UiPalette.Fade(Unjoinable ? Theme.TextDisabled : colour, appear);
        Bgra32 text = Selected ? Theme.TextBright : Theme.TextLabel;

        // The name, its kind in a box after it, and a mark when saved.
        float nameSize = context.Px(17f);
        float tagSize = context.Px(10f);
        float tagWidth = UiPainter.MeasureLabel(Theme.Mono, _line.Kind, tagSize, 0.2f) + context.Px(12f);
        float roomForName = Room(0) - tagWidth - context.Px(40f);
        string name = Fit(_line.Name, Theme.Body, nameSize, roomForName);
        float x = Left(0);
        painter.DrawText(Theme.Body, name, new UiVec2(x, UiRowStyle.CenterY(Theme.Body, nameSize, bounds)), nameSize, Ink(text));
        x += Theme.Body.MeasureWidth(name, nameSize) + context.Px(12f);
        float tagHeight = context.Px(18f);
        var tag = new UiRect(x, bounds.Y + ((bounds.Height - tagHeight) / 2f), tagWidth, tagHeight);
        painter.StrokeRect(tag, context.Px(1f), Ink(UiPalette.AccentAt(0.45f)));
        painter.DrawLabel(Theme.Mono, _line.Kind, new UiVec2(tag.X + context.Px(6f), UiRowStyle.CapsCenterY(Theme.Mono, tagSize, tag)), tagSize, Ink(Theme.Accent), 0.2f);
        if (Saved)
        {
            painter.FillDiamond(new UiVec2(tag.Right + context.Px(14f), bounds.Y + (bounds.Height / 2f)), context.Px(9f), Ink(Theme.Accent));
        }

        // The mode, in the condensed face; the map.
        float modeSize = context.Px(16f);
        painter.DrawLabel(Theme.Heading, Fit(_line.Mode.ToUpperInvariant(), Theme.Heading, modeSize, Room(1)), new UiVec2(Left(1), UiRowStyle.CapsCenterY(Theme.Heading, modeSize, bounds)), modeSize, Ink(text), 0.15f);
        float mapSize = context.Px(15f);
        painter.DrawText(Theme.Body, Fit(_line.Map, Theme.Body, mapSize, Room(2)), new UiVec2(Left(2), UiRowStyle.CenterY(Theme.Body, mapSize, bounds)), mapSize, Ink(text));

        // The players over a bar filling as the game fills, red when full.
        float monoSize = context.Px(14f);
        Bgra32 players = _line.Full ? Theme.Danger : Theme.Text;
        float mid = bounds.Y + (bounds.Height / 2f);
        string count = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_line.Players}/{_line.MaxPlayers}");
        painter.DrawText(Theme.Mono, count, new UiVec2(Left(3), mid - context.Px(4f) - Theme.Mono.LineHeight(monoSize)), monoSize, Ink(players));
        float track = context.Px(64f);
        painter.FillRect(new UiRect(Left(3), mid + context.Px(4f), track, context.Px(2f)), Ink(UiPalette.White(0.1f)));
        float filled = _line.MaxPlayers > 0 ? track * Math.Clamp(_line.Players / (float)_line.MaxPlayers, 0f, 1f) : 0f;
        painter.FillRect(new UiRect(Left(3), mid + context.Px(4f), filled, context.Px(2f)), Ink(players));

        // The ping: four bars rising, lit by strength, then the milliseconds.
        float bx = Left(4);
        float[] heights = [4f, 7f, 10f, 14f];
        for (int i = 0; i < heights.Length; i++)
        {
            float h = context.Px(heights[i]);
            painter.FillRect(new UiRect(bx, mid + context.Px(7f) - h, context.Px(3f), h), Ink(i < _line.Bars ? Theme.Text : UiPalette.White(0.15f)));
            bx += context.Px(5f);
        }

        string ping = _line.Ping is { } ms ? ms.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
        painter.DrawText(Theme.Mono, ping, new UiVec2(bx + context.Px(6f), UiRowStyle.CenterY(Theme.Mono, monoSize, bounds)), monoSize, Ink(text));

        // The region.
        painter.DrawText(Theme.Mono, Fit(_line.Region, Theme.Mono, monoSize, Room(5)), new UiVec2(Left(5), UiRowStyle.CenterY(Theme.Mono, monoSize, bounds)), monoSize, Ink(text));
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

using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Servers;

/// <summary>The server browser's columns, shared by its header and its rows so the two line up.</summary>
/// <remarks>
/// As the design draws them: a mark for a saved game, the name taking what room is left, then the mode, the map, the
/// players and the ping, each a fixed width, the ping set against the row's right edge.
/// </remarks>
public static class ServerColumns
{
    /// <summary>The columns the list can be ordered by, as its ordering numbers them.</summary>
    public static readonly string[] Headings = ["MODE", "NAME", "MAP", "PLAYERS", "PING"];

    /// <summary>The mode's column, as the ordering numbers it.</summary>
    public const int Mode = 0;

    /// <summary>The name's column.</summary>
    public const int Name = 1;

    /// <summary>The map's column.</summary>
    public const int Map = 2;

    /// <summary>The players' column.</summary>
    public const int Players = 3;

    /// <summary>The column the list is ordered by when the page opens: the ping.</summary>
    public const int Ping = 4;

    /// <summary>The columns in the order they stand across a row, after the saved mark.</summary>
    public static readonly int[] Across = [Name, Mode, Map, Players, Ping];

    /// <summary>The room left of the saved mark, in design units.</summary>
    public const float PadLeft = 8f;

    /// <summary>The room right of the ping, in design units.</summary>
    public const float PadRight = 16f;

    /// <summary>The room between two columns, in design units.</summary>
    public const float Gap = 16f;

    /// <summary>The saved mark's column, in design units.</summary>
    public const float StarWidth = 32f;

    /// <summary>The mode's column, in design units.</summary>
    public const float ModeWidth = 96f;

    /// <summary>The players' column, in design units.</summary>
    public const float PlayersWidth = 130f;

    /// <summary>The ping's column, in design units.</summary>
    public const float PingWidth = 110f;

    /// <summary>Gets the map's column, in design units.</summary>
    /// <param name="wide">Whether the page is the wide one.</param>
    /// <returns>The width.</returns>
    public static float MapWidth(bool wide) => wide ? 200f : 164f;

    /// <summary>Gets a fixed column's width, in design units; the name's is what is left.</summary>
    /// <param name="column">The column, as the ordering numbers it.</param>
    /// <param name="wide">Whether the page is the wide one.</param>
    /// <returns>The width, or zero for the name.</returns>
    public static float Width(int column, bool wide) => column switch
    {
        Mode => ModeWidth,
        Map => MapWidth(wide),
        Players => PlayersWidth,
        Ping => PingWidth,
        _ => 0f,
    };

    /// <summary>
    /// The columns' headings over the list, lined up with its rows: each orders the list by its column when pressed (again,
    /// the other way); the one the list is ordered by is brighter, marked with the way it runs.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="sortBy">The column the list is ordered by.</param>
    /// <param name="descending">Whether it runs highest first.</param>
    /// <param name="wide">Whether the page is the wide one.</param>
    /// <param name="sort">Told the column pressed and the way it now runs (descending when pressed again).</param>
    /// <returns>The headings.</returns>
    public static Grid Header(UiTheme theme, int sortBy, bool descending, bool wide, Action<int, bool> sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        var header = new Grid { ColumnSpacing = Gap, Margin = new Thickness(PadLeft, 0f, PadRight, 0f) };
        header.RowDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Absolute(StarWidth));
        for (int place = 0; place < Across.Length; place++)
        {
            int column = Across[place];
            float width = Width(column, wide);
            header.ColumnDefinitions.Add(width > 0f ? GridLength.Absolute(width) : GridLength.Star);
            bool sorted = column == sortBy;
            bool last = column == Ping;
            var heading = new SortHeading(theme, Headings[column])
            {
                Order = !sorted ? 0 : descending ? -1 : 1,
                AlignRight = last,
                HorizontalAlignment = last ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                Tooltip = sorted ? (descending ? "Ordered by this, highest first: press for lowest first." : "Ordered by this, lowest first: press for highest first.") : "Order the list by this.",
            };
            heading.Activated += _ => sort(column, sortBy == column && !descending);
            header.Add(heading, 0, place + 1);
        }

        return header;
    }

    /// <summary>Orders a list by a column, either way; lines alike in it by their names.</summary>
    /// <typeparam name="T">What the list holds.</typeparam>
    /// <param name="items">The list.</param>
    /// <param name="line">What each shows as a line.</param>
    /// <param name="sortBy">The column, as <see cref="Headings"/> numbers it.</param>
    /// <param name="descending">Whether highest first.</param>
    /// <returns>The list, ordered.</returns>
    public static List<T> Order<T>(IEnumerable<T> items, Func<T, ServerLine> line, int sortBy, bool descending)
    {
        ArgumentNullException.ThrowIfNull(line);
        Func<T, IComparable> key = sortBy switch
        {
            Mode => item => line(item).Mode,
            Map => item => line(item).MapNumber,
            Players => item => line(item).Players,
            Ping => item => line(item).Ping ?? int.MaxValue,
            _ => item => line(item).Name.ToUpperInvariant(),
        };
        IOrderedEnumerable<T> ordered = descending ? items.OrderByDescending(key) : items.OrderBy(key);
        return [.. ordered.ThenBy(item => line(item).Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Where each column stands across a row.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="bounds">The row.</param>
    /// <param name="wide">Whether the page is the wide one.</param>
    /// <returns>The columns' left edges and the name's and map's room, in pixels.</returns>
    public static ServerColumnPlaces Lay(LayoutContext context, UiRect bounds, bool wide)
    {
        float gap = context.Px(Gap);
        float pingRight = bounds.Right - context.Px(PadRight);
        float ping = pingRight - context.Px(PingWidth);
        float players = ping - gap - context.Px(PlayersWidth);
        float mapWidth = context.Px(MapWidth(wide));
        float map = players - gap - mapWidth;
        float mode = map - gap - context.Px(ModeWidth);
        float star = bounds.X + context.Px(PadLeft);
        float name = star + context.Px(StarWidth) + gap;
        return new ServerColumnPlaces(star, name, MathF.Max(0f, mode - gap - name), mode, map, mapWidth, players, ping, pingRight);
    }
}

/// <summary>Where a server row's columns stand, in pixels.</summary>
/// <param name="Star">The saved mark's left edge.</param>
/// <param name="Name">The name's left edge.</param>
/// <param name="NameWidth">The name's room.</param>
/// <param name="Mode">The mode's left edge.</param>
/// <param name="Map">The map's left edge.</param>
/// <param name="MapWidth">The map's room.</param>
/// <param name="Players">The players' left edge.</param>
/// <param name="Ping">The ping's left edge.</param>
/// <param name="PingRight">The ping's right edge, which it is set against.</param>
public readonly record struct ServerColumnPlaces(float Star, float Name, float NameWidth, float Mode, float Map, float MapWidth, float Players, float Ping, float PingRight);

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
/// <param name="MapNumber">The map's mission number, which the map column is ordered by; 0 when not known.</param>
/// <param name="Official">Whether it is an official server, marked so after its name (<see cref="OfficialMark"/>).</param>
public sealed record ServerLine(string Name, string Kind, string ModeId, string Mode, string Map, int Players, int MaxPlayers, int? Ping, string Region, int MapNumber = 0, bool Official = false)
{
    /// <summary>Gets a value indicating whether it has no room left.</summary>
    public bool Full => Players >= MaxPlayers;

    /// <summary>Gets what it is and where, as the panel of the server picked heads its name: <c>OFFICIAL SERVER · EU</c>.</summary>
    public string Heading => (Official ? "OFFICIAL " : string.Empty) + (Region.Length > 0 ? $"{Kind} · {Region}" : Kind);

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
public sealed class SortHeading : Control
{
    private const float TextSize = 14f;
    private const float Tracking = 0.12f;

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

    /// <summary>Gets or sets a value indicating whether the heading is set against its column's right edge, as the ping's is.</summary>
    public bool AlignRight { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float size = context.Px(TextSize);
        return new UiVec2(UiPainter.MeasureLabel(Theme.Heading, _text, size, Tracking) + context.Px(16f), context.Px(44f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        Bgra32 ink = UiPalette.Fade(Order != 0 || hot > 0.5f ? Theme.Text : Theme.TextSecondary, Visual.Appear);
        float size = context.Px(TextSize);
        float width = UiPainter.MeasureLabel(Theme.Heading, _text, size, Tracking) + (Order != 0 ? context.Px(12f) : 0f);
        float x = AlignRight ? Bounds.Right - width : Bounds.X;
        UiVec2 end = painter.DrawLabel(Theme.Heading, _text, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, size, Bounds)), size, ink, Tracking);
        if (Order == 0)
        {
            return;
        }

        // The mark: a small triangle, its point the way the values run.
        float half = context.Px(4f);
        float rise = context.Px(3f) * Order;
        float markX = end.X + context.Px(2f) + half;
        float y = Bounds.Y + (Bounds.Height / 2f);
        painter.FillTriangle(new UiVec2(markX - half, y + rise), new UiVec2(markX + half, y + rise), new UiVec2(markX, y - rise), ink);
    }
}

/// <summary>
/// One line of the server browser: a server or room in columns -- a star when it is saved, its name over its kind and
/// region (or why it cannot be joined), its mode as an outlined badge in the mode's colour, the map, the players over a bar
/// of places filling as it fills, the ping with bars by strength. Clicking picks it; the picked line is washed in the
/// accent, with the accent down its edge.
/// </summary>
public sealed class ServerRow : Control
{
    private ServerLine _line;
    private string _under = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="ServerRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="line">What it shows.</param>
    public ServerRow(UiTheme theme, ServerLine line)
        : base(theme)
    {
        _line = line;
        Line = line;
    }

    /// <summary>
    /// Gets or sets what the line shows. Set again as the server's players and ping come in, so the line stays on the page
    /// (and keeps its place, focus and look) rather than being made anew.
    /// </summary>
    public ServerLine Line
    {
        get => _line;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _line = value;
            string kind = value.Kind.Length > 1 && value.Kind != "LAN" ? string.Concat(value.Kind.AsSpan(0, 1), value.Kind.AsSpan(1).ToString().ToLowerInvariant()) : value.Kind;
            _under = value.Region.Length > 0 && value.Region != value.Kind ? $"{kind} · {value.Region}" : kind;
        }
    }

    /// <summary>Gets or sets a value indicating whether it is the line picked.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets a value indicating whether the player saved it.</summary>
    public bool Saved { get; set; }

    /// <summary>Gets or sets a value indicating whether joining it takes a password, which marks it with a padlock.</summary>
    public bool Locked { get; set; }

    /// <summary>Gets or sets a value indicating whether it can't be joined (another version), which greys it.</summary>
    public bool Unjoinable { get; set; }

    /// <summary>Gets or sets why the line cannot be joined, shown under its name in place of its kind; empty when it can.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the reason is a failure -- no answer -- rather than a caution.</summary>
    public bool ReasonIsFailure { get; set; }

    /// <summary>Gets or sets a value indicating whether the page is the wide one, whose map column is wider.</summary>
    public bool Wide { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the line is drawn two deep for a narrow page: its name, and under it its mode, map and players.</summary>
    public bool Compact { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 60f : 72f));

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
            painter.FillRect(bounds, UiPalette.AccentAt(0.13f * appear));
            painter.StrokeRect(bounds, context.Px(1f), UiPalette.AccentAt(0.45f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }
        else if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.07f * hot * appear));
        }

        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(bounds, context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.07f * appear));
        if (Compact)
        {
            DrawCompact(painter);
            return;
        }


        ServerColumnPlaces at = ServerColumns.Lay(context, bounds, Wide);

        // A line that cannot be joined is quieter, not gone: it has to be read to see why.
        Bgra32 Ink(Bgra32 colour) => UiPalette.Fade(Unjoinable ? Theme.TextSecondary : colour, appear);

        var star = new UiVec2(at.Star + (context.Px(ServerColumns.StarWidth) / 2f), mid);
        if (Saved)
        {
            FillStar(painter, star, context.Px(18f), UiPalette.Fade(Theme.Accent, appear));
        }
        else
        {
            KitIcons.Draw(painter, KitIcon.Star, star, context.Px(18f), UiPalette.Fade(Theme.TextMuted, appear));
        }

        DrawName(painter, at.Name, at.NameWidth, Ink);
        ModeLook.DrawBadge(painter, Theme, new UiVec2(at.Mode, mid - context.Px(12f)), _line.ModeId, appear);

        float mapSize = context.Px(17f);
        painter.DrawText(Theme.Body, Theme.Body.Fit(_line.Map, mapSize, at.MapWidth), new UiVec2(at.Map, UiRowStyle.CenterY(Theme.Body, mapSize, bounds)), mapSize, Ink(Theme.Text));

        DrawPlayers(painter, at.Players, mid, Ink);
        DrawPing(painter, at.PingRight, mid, Ink);
    }

    // A star filled: a fan of triangles about its middle, since its outline is not convex.
    private static void FillStar(UiPainter painter, UiVec2 centre, float size, Bgra32 colour)
    {
        float outer = size * 0.5f;
        Span<UiVec2> points = stackalloc UiVec2[10];
        for (int i = 0; i < 10; i++)
        {
            float radius = i % 2 == 0 ? outer : outer * 0.4f;
            (float sin, float cos) = MathF.SinCos((i * MathF.PI / 5f) - (MathF.PI * 0.5f));
            points[i] = new UiVec2(centre.X + (cos * radius), centre.Y + (sin * radius));
        }

        for (int i = 0; i < 10; i++)
        {
            painter.FillTriangle(centre, points[i], points[(i + 1) % 10], colour);
        }
    }

    // The line two deep: the name with its marks; under it the mode in its colour, the map and the players, or why it
    // cannot be joined; and at the right the ping and the way in.
    private void DrawCompact(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        float pad = context.Px(12f);
        float right = bounds.Right - pad;
        painter.DrawChevron(new UiVec2(right - context.Px(4f), bounds.Center.Y), context.Px(10f), UiDirection.Right, context.Px(2f), UiPalette.Fade(Theme.TextSecondary, appear));
        right -= context.Px(22f);

        float pingSize = context.Px(15f);
        string ping = _line.Ping is { } ms ? ms.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
        float pingWidth = Theme.Mono.MeasureWidth(ping, pingSize);
        painter.DrawText(Theme.Mono, ping, new UiVec2(right - pingWidth, UiRowStyle.CenterY(Theme.Mono, pingSize, bounds)), pingSize, UiPalette.Fade(Unjoinable ? Theme.TextMuted : Theme.Text, appear));
        right -= pingWidth + context.Px(8f);
        float[] heights = [4f, 7f, 10f, 14f];
        Bgra32 strength = Unjoinable ? Theme.TextSecondary : Theme.Text;
        float barX = right - context.Px(18f);
        for (int i = 0; i < heights.Length; i++)
        {
            float h = context.Px(heights[i]);
            painter.FillRect(new UiRect(barX + (i * context.Px(5f)), bounds.Center.Y + context.Px(7f) - h, context.Px(3f), h), UiPalette.Fade(i < _line.Bars ? strength : UiPalette.White(0.18f), appear));
        }

        right = barX - context.Px(12f);
        float x = bounds.X + pad + (Selected ? context.Px(4f) : 0f);
        float nameSize = context.Px(17f);
        float underSize = context.Px(14f);
        float gap = context.Px(2f);
        float block = Theme.BodyStrong.LineHeight(nameSize) + gap + Theme.Body.LineHeight(underSize);
        float top = bounds.Y + ((bounds.Height - block) / 2f);
        float official = _line.Official ? OfficialMark.Width(context, Theme) + context.Px(8f) : 0f;
        float marks = official + (Locked ? context.Px(22f) : 0f) + (Saved ? context.Px(22f) : 0f);
        string name = Theme.BodyStrong.Fit(_line.Name, nameSize, right - x - marks);
        painter.DrawText(Theme.BodyStrong, name, new UiVec2(x, top), nameSize, UiPalette.Fade(Unjoinable ? Theme.TextSecondary : Theme.Text, appear));
        float markX = x + Theme.BodyStrong.MeasureWidth(name, nameSize) + context.Px(10f);
        float markY = top + (Theme.BodyStrong.LineHeight(nameSize) / 2f);
        if (_line.Official)
        {
            markX = OfficialMark.Draw(painter, Theme, new UiVec2(markX, markY), appear).Right + context.Px(8f);
        }

        if (Locked)
        {
            KitIcons.Draw(painter, KitIcon.Lock, new UiVec2(markX + context.Px(6f), markY), context.Px(14f), UiPalette.Fade(Theme.TextSecondary, appear));
            markX += context.Px(22f);
        }

        if (Saved)
        {
            FillStar(painter, new UiVec2(markX + context.Px(7f), markY), context.Px(15f), UiPalette.Fade(Theme.Accent, appear));
        }

        float underY = top + Theme.BodyStrong.LineHeight(nameSize) + gap;
        if (Reason.Length > 0 && (Unjoinable || _line.Full))
        {
            Bgra32 why = UiPalette.Fade(ReasonIsFailure ? Theme.Danger : Theme.Warning, appear);
            float reasonSize = context.Px(14f);
            var line = new UiRect(x, underY, right - x, Theme.Body.LineHeight(underSize));
            KitIcons.Draw(painter, ReasonIsFailure ? KitIcon.Close : KitIcon.Warning, new UiVec2(x + context.Px(8f), line.Center.Y), context.Px(14f), why);
            painter.DrawLabel(Theme.Heading, Theme.Heading.Fit(Reason, reasonSize, line.Width - context.Px(22f)), new UiVec2(x + context.Px(22f), UiRowStyle.CapsCenterY(Theme.Heading, reasonSize, line)), reasonSize, why, 0.08f);
            return;
        }

        string under = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ModeLook.Code(_line.ModeId)} · {_line.Map} · {_line.Players}/{_line.MaxPlayers}");
        painter.DrawText(Theme.Body, Theme.Body.Fit(under, underSize, right - x), new UiVec2(x, underY), underSize, UiPalette.Fade(Theme.TextSecondary, appear));
    }

    // The name, the official mark after it for one of ours and a padlock when it asks for a password; under it, its kind
    // and where it is, or why it cannot be joined.
    private void DrawName(UiPainter painter, float x, float room, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float appear = Visual.Appear;
        float nameSize = context.Px(18f);
        float underSize = context.Px(15f);
        float gap = context.Px(3f);
        float block = Theme.BodyStrong.LineHeight(nameSize) + gap + Theme.Body.LineHeight(underSize);
        float top = bounds.Y + ((bounds.Height - block) / 2f);
        float official = _line.Official ? OfficialMark.Width(context, Theme) + context.Px(10f) : 0f;
        float marks = official + (Locked ? context.Px(24f) : 0f);
        string name = Theme.BodyStrong.Fit(_line.Name, nameSize, room - marks);
        painter.DrawText(Theme.BodyStrong, name, new UiVec2(x, top), nameSize, ink(Theme.Text));
        float markX = x + Theme.BodyStrong.MeasureWidth(name, nameSize) + context.Px(10f);
        float markY = top + (Theme.BodyStrong.LineHeight(nameSize) / 2f);
        if (_line.Official)
        {
            markX = OfficialMark.Draw(painter, Theme, new UiVec2(markX, markY), appear).Right + context.Px(8f);
        }

        if (Locked)
        {
            KitIcons.Draw(painter, KitIcon.Lock, new UiVec2(markX + context.Px(8f), markY), context.Px(16f), ink(Theme.TextSecondary));
        }

        float underY = top + Theme.BodyStrong.LineHeight(nameSize) + gap;
        if (Reason.Length == 0)
        {
            painter.DrawText(Theme.Body, Theme.Body.Fit(_under, underSize, room), new UiVec2(x, underY), underSize, UiPalette.Fade(Theme.TextSecondary, appear));
            return;
        }

        // Why it cannot be joined, in the colour of how bad that is, behind a mark.
        Bgra32 why = UiPalette.Fade(ReasonIsFailure ? Theme.Danger : Theme.Warning, appear);
        float reasonSize = context.Px(14f);
        var line = new UiRect(x, underY, room, Theme.Body.LineHeight(underSize));
        KitIcons.Draw(painter, ReasonIsFailure ? KitIcon.Close : _line.Full ? KitIcon.People : KitIcon.Warning, new UiVec2(x + context.Px(8f), line.Center.Y), context.Px(14f), why);
        painter.DrawLabel(Theme.Heading, Theme.Heading.Fit(Reason, reasonSize, room - context.Px(22f)), new UiVec2(x + context.Px(22f), UiRowStyle.CapsCenterY(Theme.Heading, reasonSize, line)), reasonSize, why, 0.08f);
    }

    // The players over a bar of places, one for each the game takes up to sixteen, filled as it fills; the count warns when full.
    private void DrawPlayers(UiPainter painter, float x, float mid, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        float size = context.Px(15f);
        float bar = context.Px(6f);
        float between = context.Px(6f);
        float block = Theme.Mono.LineHeight(size) + between + bar;
        float top = mid - (block / 2f);
        string count = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_line.Players} / {_line.MaxPlayers}");
        painter.DrawText(Theme.Mono, count, new UiVec2(x, top), size, _line.Full ? UiPalette.Fade(Theme.Warning, Visual.Appear) : ink(Theme.Text));

        int places = Math.Clamp(_line.MaxPlayers, 1, 16);
        int taken = _line.MaxPlayers <= 0 ? 0 : Math.Clamp((int)MathF.Ceiling(_line.Players * places / (float)_line.MaxPlayers), 0, places);
        float gap = context.Px(2f);
        float track = context.Px(114f);
        float width = (track - (gap * (places - 1))) / places;
        float barY = top + Theme.Mono.LineHeight(size) + between;
        for (int i = 0; i < places; i++)
        {
            painter.FillRect(new UiRect(x + (i * (width + gap)), barY, width, bar), i < taken ? ink(Theme.Text) : UiPalette.White(0.16f * Visual.Appear));
        }
    }

    // The ping, against the row's right edge: four bars rising, lit by strength, then the milliseconds.
    private void DrawPing(UiPainter painter, float right, float mid, Func<Bgra32, Bgra32> ink)
    {
        LayoutContext context = painter.Context;
        float size = context.Px(15f);
        string text = _line.Ping is { } ms ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ms} ms") : "—";
        Bgra32 colour = _line.Ping is null && ReasonIsFailure ? UiPalette.Fade(Theme.Danger, Visual.Appear) : ink(Theme.Text);
        float width = Theme.Mono.MeasureWidth(text, size);
        float x = right - width;
        painter.DrawText(Theme.Mono, text, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, size, Bounds)), size, colour);

        float[] heights = [4f, 7f, 10f, 13f];
        float barX = x - context.Px(8f) - context.Px((heights.Length * 3f) + ((heights.Length - 1) * 2f));
        for (int i = 0; i < heights.Length; i++)
        {
            float h = context.Px(heights[i]);
            painter.FillRect(new UiRect(barX, mid + context.Px(6.5f) - h, context.Px(3f), h), i < _line.Bars ? ink(Theme.Text) : UiPalette.White(0.18f * Visual.Appear));
            barX += context.Px(5f);
        }
    }

}

/// <summary>
/// The shape of a line of the server browser, shown while the list has not been heard yet: a block where each of a line's
/// parts will stand -- the badge, the name and what is under it, the map, the players, the ping -- with a soft light passing
/// along them, and each line fainter than the one above so the list tails off rather than ends.
/// </summary>
public sealed class ServerRowSkeleton : UiElement
{
    private readonly int _index;
    private readonly int _count;
    private float _time;

    /// <summary>Gets or sets a value indicating whether it stands in for a line drawn two deep.</summary>
    public bool Compact { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ServerRowSkeleton"/> class.</summary>
    /// <param name="index">Which line it is, from the top.</param>
    /// <param name="count">How many there are.</param>
    public ServerRowSkeleton(int index, int count)
    {
        _index = index;
        _count = Math.Max(1, count);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 60f : 72f));

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds) => _time += elapsedSeconds;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float mid = bounds.Y + (bounds.Height / 2f);
        float fade = 1f - (0.75f * _index / _count);
        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.05f * fade));

        ServerColumnPlaces at = ServerColumns.Lay(context, bounds, wide: true);

        // A block, lit by where the passing light is: it crosses the row from the left about once every two seconds,
        // each row a little behind the one above.
        void Block(float x, float y, float width, float height)
        {
            float along = (x + (width / 2f) - bounds.X) / MathF.Max(1f, bounds.Width);
            float wave = 0.5f + (0.5f * MathF.Sin(MathF.Tau * ((_time * 0.55f) - along - (_index * 0.06f))));
            painter.FillRect(new UiRect(x, y, width, height), UiPalette.White((0.05f + (0.07f * wave * wave)) * fade));
        }

        if (Compact)
        {
            float lead = bounds.X + context.Px(12f);
            float wide = (bounds.Width * 0.3f) + (bounds.Width * 0.18f * ((_index * 7 % 5) / 4f));
            Block(lead, mid - context.Px(14f), wide, context.Px(13f));
            Block(lead, mid + context.Px(5f), context.Px(150f), context.Px(9f));
            Block(bounds.Right - context.Px(90f), mid - context.Px(6f), context.Px(50f), context.Px(12f));
            return;
        }

        // Names of different lengths, so the rows do not read as a ruled table.
        float name = at.NameWidth * (0.34f + (0.22f * ((_index * 7 % 5) / 4f)));
        Block(at.Name, mid - context.Px(15f), name, context.Px(14f));
        Block(at.Name, mid + context.Px(7f), context.Px(96f), context.Px(8f));
        Block(at.Mode, mid - context.Px(12f), context.Px(52f), context.Px(24f));
        Block(at.Map, mid - context.Px(6f), MathF.Min(at.MapWidth, context.Px(120f)), context.Px(12f));
        Block(at.Players, mid - context.Px(14f), context.Px(48f), context.Px(10f));
        Block(at.Players, mid + context.Px(5f), context.Px(114f), context.Px(6f));
        Block(at.PingRight - context.Px(64f), mid - context.Px(6f), context.Px(64f), context.Px(12f));
    }
}

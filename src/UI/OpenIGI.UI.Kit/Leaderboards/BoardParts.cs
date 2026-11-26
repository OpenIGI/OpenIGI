using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>A board's panel: its parts laid out in a grid on the boards' dark glass.</summary>
public sealed class BoardPanel : Grid
{
    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        BoardLook.Panel(painter, Bounds);
        base.OnDraw(painter);
    }
}

/// <summary>What a <see cref="SeasonCard"/> shows: this season with the days it has left, one gone by, all time, or nothing known.</summary>
public enum SeasonCardState
{
    /// <summary>This season, counting down.</summary>
    Current,

    /// <summary>A season gone by: its final standings.</summary>
    Ended,

    /// <summary>All time.</summary>
    AllTime,

    /// <summary>Being read.</summary>
    Loading,

    /// <summary>The board can't be read.</summary>
    Unavailable,
}

/// <summary>
/// The season's card at a board's top right: RANKED SEASON over its name and dates; then how long it has left, the day of the
/// month it is, and a bar of the month gone. A season gone by says its standings are final; all time says so.
/// </summary>
/// <param name="theme">The theme.</param>
public sealed class SeasonCard(UiTheme theme) : UiElement
{
    /// <summary>Gets or sets what it shows.</summary>
    public SeasonCardState State { get; set; }

    /// <summary>Gets or sets the season shown (<c>yyyy-MM</c>).</summary>
    public string Season { get; set; } = string.Empty;

    /// <summary>Gets or sets the time it is now.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets a value indicating whether it is the smaller card of a laptop's page.</summary>
    public bool Small { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(MathF.Min(available.X, context.Px(Small ? 470f : 600f)), context.Px(Small ? 76f : 92f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        BoardLook.Panel(painter, box);
        float pad = context.Px(20f);
        float scale = Small ? 0.8f : 1f;
        if (State == SeasonCardState.Loading)
        {
            Bgra32 block = UiPalette.White(0.07f);
            painter.FillRect(new UiRect(box.X + pad, box.Y + context.Px(18f), context.Px(160f), context.Px(14f)), block);
            painter.FillRect(new UiRect(box.X + pad, box.Y + context.Px(40f * scale), context.Px(220f), context.Px(22f * scale)), block);
            painter.FillRect(new UiRect(box.Center.X + pad, box.Y + context.Px(30f), context.Px(180f), context.Px(30f * scale)), block);
            return;
        }

        if (State == SeasonCardState.Unavailable)
        {
            float icon = context.Px(18f);
            BoardLook.Icon(painter, theme, KitIcon.Warning, new UiVec2(box.X + pad + (icon / 2f), box.Center.Y), icon, theme.TextMuted);
            float textLeft = box.X + pad + icon + context.Px(12f);
            BoardLook.Caption(painter, theme, "Season", new UiVec2(textLeft, box.Center.Y - context.Px(16f)), 10f, theme.TextMuted);
            float px = context.Px(18f * scale);
            painter.DrawLabel(theme.Display, "SEASON INFO UNAVAILABLE", new UiVec2(textLeft, box.Center.Y - context.Px(2f)), px, theme.TextSecondary, 0.06f);
            return;
        }

        (DateOnly First, DateOnly Last)? span = LeaderboardSeasons.Span(Season);
        float split = box.X + context.Px(Small ? 184f : 228f);
        float x = box.X + pad;
        float nameSize = context.Px(30f * scale);
        float captionGap = context.Px(4f);
        float leftHeight = context.Px(10f) + captionGap + (nameSize * 1.05f) + captionGap + context.Px(12f);
        float top = box.Center.Y - (leftHeight / 2f);
        string eyebrow = State switch
        {
            SeasonCardState.Ended => "Season ended",
            SeasonCardState.AllTime => "All time",
            _ => "Ranked season",
        };
        string name = State == SeasonCardState.AllTime ? "EVERY SEASON" : LeaderboardSeasons.Name(Season).ToUpperInvariant();
        string dates = span is { } days
            ? string.Create(CultureInfo.InvariantCulture, $"{days.First.Day} {days.First:MMM} – {days.Last.Day} {days.Last:MMM yyyy}")
            : "Every match counted";
        BoardLook.Caption(painter, theme, eyebrow, new UiVec2(x, top), 10f, theme.Accent);
        float nameTop = top + context.Px(10f) + captionGap;
        painter.DrawLabel(theme.Display, theme.Display.Fit(name, nameSize, split - x - pad), new UiVec2(x, nameTop), nameSize, theme.TextBright, 0.03f);
        painter.DrawText(theme.Mono, dates, new UiVec2(x, nameTop + (nameSize * 1.05f) + captionGap), context.Px(12f), theme.TextSecondary);

        painter.FillRect(new UiRect(split, box.Y, context.Px(1f), box.Height), UiPalette.White(0.1f));
        float right = box.Right - pad;
        x = split + pad;
        if (State != SeasonCardState.Current || span is null)
        {
            string headline = State == SeasonCardState.Ended ? "FINAL" : "ALL";
            string under = State == SeasonCardState.Ended ? "standings" : "seasons";
            BoardLook.Caption(painter, theme, State == SeasonCardState.Ended ? "Final standings" : "Since the first season", new UiVec2(x, top), 10f, theme.TextSecondary);
            float bigSize = context.Px(36f * scale);
            float end = painter.DrawLabel(theme.Display, headline, new UiVec2(x, nameTop), bigSize, theme.TextBright, 0.02f).X;
            painter.DrawLabel(theme.Display, under.ToUpperInvariant(), new UiVec2(end + context.Px(8f), nameTop + (bigSize * 0.42f)), context.Px(16f * scale), theme.TextSecondary, 0.08f);
            return;
        }

        (_, DateOnly last) = span.Value;
        int total = last.Day;
        int today = Math.Clamp(Now.ToUniversalTime().Day, 1, total);
        int left = LeaderboardSeasons.DaysLeft(Now);
        BoardLook.Caption(painter, theme, "Ends in", new UiVec2(x, top), 10f, theme.TextSecondary);
        string day = string.Create(CultureInfo.InvariantCulture, $"Day {today} of {total}");
        BoardLook.Right(painter, theme.Mono, day, right, top - context.Px(1f), context.Px(11f), theme.TextSecondary);
        float figureSize = context.Px(42f * scale);
        string figure = left.ToString(CultureInfo.InvariantCulture);
        float figureTop = top + context.Px(10f);
        float figureEnd = painter.DrawLabel(theme.Display, figure, new UiVec2(x, figureTop), figureSize, theme.Accent, 0f).X;
        painter.DrawLabel(theme.Display, left == 1 ? "DAY" : "DAYS", new UiVec2(figureEnd + context.Px(8f), figureTop + (figureSize * 0.38f)), context.Px(18f * scale), theme.Accent, 0.08f);
        var bar = new UiRect(x, box.Bottom - context.Px(Small ? 16f : 20f), right - x, context.Px(4f));
        BoardLook.Bar(painter, bar, (double)today / total, theme.Accent);
    }
}

/// <summary>A short note boxed in the accent at a phone's top: an icon and a few words, such as 6 DAYS LEFT.</summary>
/// <param name="theme">The theme.</param>
public sealed class BoardChip(UiTheme theme) : UiElement
{
    /// <summary>Gets or sets what it says.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the icon before it.</summary>
    public KitIcon Icon { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float icon = Icon != KitIcon.None ? context.Px(22f) : 0f;
        return new UiVec2(context.Px(28f) + icon + UiPainter.MeasureLabel(theme.Heading, Text.ToUpperInvariant(), context.Px(15f), 0.1f), context.Px(44f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        painter.FillRect(Bounds, UiPalette.AccentAt(0.08f));
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.AccentAt(0.5f));
        float x = Bounds.X + context.Px(14f);
        if (Icon != KitIcon.None)
        {
            BoardLook.Icon(painter, theme, Icon, new UiVec2(x + context.Px(8f), Bounds.Center.Y), context.Px(15f), theme.Accent);
            x += context.Px(22f);
        }

        float px = context.Px(15f);
        painter.DrawLabel(theme.Heading, Text.ToUpperInvariant(), new UiVec2(x, UiRowStyle.CapsCenterY(theme.Heading, px, Bounds)), px, theme.Accent, 0.1f);
    }
}

/// <summary>A figure's tile: a caption over a large figure, and a quieter line under it, with a change beside the figure where given.</summary>
/// <param name="theme">The theme.</param>
/// <param name="label">The caption.</param>
/// <param name="value">The figure.</param>
/// <param name="sub">The line under it; empty for none.</param>
public sealed class StatTile(UiTheme theme, string label, string value, string sub = "") : UiElement
{
    /// <summary>Gets or sets the figure's colour; the theme's bright text when null.</summary>
    public Bgra32? Ink { get; set; }

    /// <summary>Gets or sets a change beside the figure, such as +1.13 against all time; null for none.</summary>
    public double? Change { get; set; }

    /// <summary>Gets or sets a share drawn as a bar under the figure; null for none.</summary>
    public double? Share { get; set; }

    /// <summary>Gets or sets the figure's size, in design units.</summary>
    public float FigureSize { get; set; } = 26f;

    /// <summary>Gets or sets the tile's padding, in design units.</summary>
    public float TilePadding { get; set; } = 15f;

    /// <summary>Gets or sets a value indicating whether it stands on a board's panel rather than a quieter tile.</summary>
    public bool Panelled { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float height = (2f * context.Px(TilePadding)) + context.Px(10f) + context.Px(8f) + (context.Px(FigureSize) * 1.1f)
            + (sub.Length > 0 || Share is not null ? context.Px(8f) + context.Px(12f) : 0f)
            + (Share is not null ? context.Px(10f) : 0f);
        return new UiVec2(available.X, height);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        if (Panelled)
        {
            BoardLook.Panel(painter, Bounds);
        }
        else
        {
            BoardLook.Tile(painter, Bounds);
        }

        float pad = context.Px(TilePadding);
        float x = Bounds.X + pad;
        float y = Bounds.Y + pad;
        BoardLook.Caption(painter, theme, label, new UiVec2(x, y), 10f, theme.TextSecondary);
        y += context.Px(10f) + context.Px(8f);
        float px = context.Px(FigureSize);
        float end = painter.DrawText(theme.MonoFigures, value, new UiVec2(x, y - (px * 0.08f)), px, Ink ?? theme.TextBright).X;
        if (Change is { } change && Math.Abs(change) >= 0.005 && BoardLook.InClip(painter, Bounds))
        {
            float changePx = context.Px(12f);
            Bgra32 ink = change > 0 ? theme.Success : theme.TextMuted;
            float arrow = changePx * 0.6f;
            float mid = y + (px * 0.62f);
            float ax = end + context.Px(10f);
            if (change > 0)
            {
                painter.FillTriangle(new UiVec2(ax, mid + (arrow * 0.4f)), new UiVec2(ax + arrow, mid + (arrow * 0.4f)), new UiVec2(ax + (arrow / 2f), mid - (arrow * 0.45f)), ink);
            }
            else
            {
                painter.FillTriangle(new UiVec2(ax, mid - (arrow * 0.4f)), new UiVec2(ax + arrow, mid - (arrow * 0.4f)), new UiVec2(ax + (arrow / 2f), mid + (arrow * 0.45f)), ink);
            }

            painter.DrawText(theme.Mono, Math.Abs(change).ToString("0.00", CultureInfo.InvariantCulture), new UiVec2(ax + arrow + context.Px(4f), mid - (theme.Mono.LineHeight(changePx) / 2f)), changePx, ink);
        }

        y += px * 1.1f;
        if (Share is { } share)
        {
            y += context.Px(6f);
            BoardLook.Bar(painter, new UiRect(x, y, Bounds.Width - (2f * pad), context.Px(4f)), share, theme.Accent);
            y += context.Px(6f);
        }

        if (sub.Length > 0)
        {
            y += context.Px(6f);
            painter.DrawText(theme.Mono, theme.Mono.Fit(sub, context.Px(11f), Bounds.Width - (2f * pad)), new UiVec2(x, y), context.Px(11f), theme.TextMuted);
        }
    }
}

/// <summary>The latest results as a strip of tiles, newest first: W filled, L outlined.</summary>
/// <param name="theme">The theme.</param>
/// <param name="results">Each result: won or not, newest first; null while being read.</param>
public sealed class WinLossStrip(UiTheme theme, IReadOnlyList<bool>? results) : UiElement
{
    /// <summary>Gets or sets each tile's height, in design units.</summary>
    public float TileHeight { get; set; } = 30f;

    /// <summary>Gets or sets how many tiles there are room for: the strip is as wide as so many.</summary>
    public int Slots { get; set; } = 10;

    /// <summary>Gets or sets each tile's width, in design units; nought to share the strip's width.</summary>
    public float TileWidth { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = TileWidth > 0f ? (Slots * context.Px(TileWidth)) + ((Slots - 1) * context.Px(4f)) : available.X;
        return new UiVec2(width, context.Px(TileHeight));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float gap = context.Px(TileWidth > 0f ? 3f : 4f);
        float width = TileWidth > 0f ? context.Px(TileWidth) : (Bounds.Width - ((Slots - 1) * gap)) / Slots;
        float px = context.Px(TileHeight * 0.47f);
        for (int i = 0; i < Slots; i++)
        {
            var tile = new UiRect(Bounds.X + (i * (width + gap)), Bounds.Y, width, Bounds.Height);
            if (results is null || i >= results.Count)
            {
                painter.FillRect(tile, UiPalette.White(results is null ? 0.06f : 0.025f));
                continue;
            }

            bool won = results[i];
            if (won)
            {
                painter.FillRect(tile, UiPalette.White(0.88f));
            }
            else
            {
                painter.StrokeRect(tile, context.Px(1f), UiPalette.White(0.22f));
            }

            string letter = won ? "W" : "L";
            BoardLook.Centred(painter, theme.Display, letter, tile.Center.X, UiRowStyle.CapsCenterY(theme.Display, px, tile), px, won ? Bgra32.FromRgb(17, 17, 17) : theme.TextMuted);
        }
    }
}

/// <summary>The medals earned, as chips in a row: a diamond in the accent and the medal's name.</summary>
/// <param name="theme">The theme.</param>
/// <param name="medals">The medals.</param>
public sealed class MedalStrip(UiTheme theme, IReadOnlyList<Medal> medals) : UiElement
{
    /// <summary>Gets or sets a value indicating whether only the diamonds are shown, in a line after a caption.</summary>
    public bool DiamondsOnly { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(DiamondsOnly ? 20f : Rows(context, available.X) * 40f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float x = Bounds.X;
        float y = Bounds.Y;
        if (DiamondsOnly)
        {
            for (int i = 0; i < medals.Count && BoardLook.InClip(painter, Bounds); i++)
            {
                painter.FillDiamond(new UiVec2(x + context.Px(9f) + (i * context.Px(24f)), Bounds.Center.Y), context.Px(16f), theme.Accent);
            }

            return;
        }

        float px = context.Px(14f);
        foreach (Medal medal in medals)
        {
            float width = ChipWidth(context, medal);
            if (x + width > Bounds.Right && x > Bounds.X)
            {
                x = Bounds.X;
                y += context.Px(40f);
            }

            var chip = new UiRect(x, y, width, context.Px(34f));
            painter.FillRect(chip, UiPalette.AccentAt(0.06f));
            painter.StrokeRect(chip, context.Px(1f), UiPalette.AccentAt(0.3f));
            if (BoardLook.InClip(painter, chip))
            {
                painter.FillDiamond(new UiVec2(chip.X + context.Px(19f), chip.Center.Y), context.Px(18f), theme.Accent);
            }

            painter.DrawLabel(theme.Display, medal.Name.ToUpperInvariant(), new UiVec2(chip.X + context.Px(36f), UiRowStyle.CapsCenterY(theme.Display, px, chip)), px, theme.TextBright, 0.03f);
            x = chip.Right + context.Px(8f);
        }
    }

    private float ChipWidth(LayoutContext context, Medal medal) =>
        context.Px(36f) + UiPainter.MeasureLabel(theme.Display, medal.Name.ToUpperInvariant(), context.Px(14f), 0.03f) + context.Px(12f);

    private int Rows(LayoutContext context, float width)
    {
        int rows = medals.Count > 0 ? 1 : 0;
        float x = 0f;
        foreach (Medal medal in medals)
        {
            float chip = ChipWidth(context, medal);
            if (x + chip > width && x > 0f)
            {
                rows++;
                x = 0f;
            }

            x += chip + context.Px(8f);
        }

        return rows;
    }
}

/// <summary>A podium's place nobody has claimed: its place, OPEN, and a line saying so, in the medal's colour.</summary>
/// <param name="theme">The theme.</param>
/// <param name="place">The place.</param>
/// <param name="size">How much room the page has.</param>
public sealed class OpenPodiumSlot(UiTheme theme, int place, BoardSize size) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(PodiumCard.HeightOf(place, size)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        Bgra32 medal = BoardLook.Medal(theme, place);
        Span<Drawing.UiGradientStop> stops = [new(0f, medal.WithAlpha(20)), new(0.72f, Bgra32.FromRgba(17, 18, 19, 200)), new(1f, Bgra32.FromRgba(17, 18, 19, 200))];
        painter.FillGradient(box, stops, Drawing.UiGradientDirection.Vertical);
        painter.PushClip(box);
        string numeral = place.ToString(CultureInfo.InvariantCulture);
        float numeralSize = context.Px(size == BoardSize.Compact ? 60f : 190f);
        painter.DrawText(theme.Display, numeral, new UiVec2(box.Right - context.Px(10f) - theme.Display.MeasureWidth(numeral, numeralSize), box.Bottom - (numeralSize * 0.74f)), numeralSize, medal.WithAlpha(18));
        painter.PopClip();
        painter.StrokeRect(box, context.Px(1f), medal.WithAlpha(50));
        painter.FillRect(new UiRect(box.X, box.Y, box.Width, context.Px(3f)), medal.WithAlpha(110));
        float pad = context.Px(size == BoardSize.Compact ? 12f : 22f);
        painter.DrawLabel(theme.Display, $"#{place}", new UiVec2(box.X + pad, box.Y + context.Px(18f)), context.Px(22f), medal.WithAlpha(170), 0.04f);
        float side = context.Px(size == BoardSize.Compact ? 28f : 40f);
        var mark = new UiRect(box.X + pad, box.Center.Y - (side / 2f), side, side);
        painter.StrokeRect(mark, context.Px(1f), UiPalette.White(0.18f));
        float x = mark.Right + context.Px(14f);
        painter.DrawLabel(theme.Display, "OPEN", new UiVec2(x, mark.Y - context.Px(2f)), context.Px(22f), theme.TextSecondary, 0.04f);
        painter.DrawText(theme.Mono, "No one has claimed it", new UiVec2(x, mark.Y + context.Px(24f)), context.Px(11f), theme.TextMuted);
        float dash = context.Px(18f);
        for (int i = 0; i < 3; i++)
        {
            painter.FillRect(new UiRect(box.X + pad + (i * (dash + context.Px(10f))), box.Bottom - context.Px(28f), dash, context.Px(2f)), UiPalette.White(0.18f));
        }
    }
}

/// <summary>The words in the middle of a board with nothing in it to show: an icon in a box, a headline, a line under it.</summary>
/// <param name="theme">The theme.</param>
/// <param name="icon">The icon.</param>
/// <param name="headline">The headline.</param>
/// <param name="detail">The line under it.</param>
/// <param name="warn">Whether it is a warning: the icon in the accent, boxed.</param>
public sealed class BoardMessage(UiTheme theme, KitIcon icon, string headline, string detail, bool warn = false) : UiElement
{
    /// <summary>Gets the headline.</summary>
    public string Headline => headline;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(warn ? 130f : 112f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float centre = Bounds.Center.X;
        float y = Bounds.Y;
        float iconSize = context.Px(warn ? 22f : 26f);
        if (warn)
        {
            float side = context.Px(46f);
            var box = new UiRect(centre - (side / 2f), y, side, side);
            painter.StrokeRect(box, context.Px(1f), UiPalette.AccentAt(0.5f));
            BoardLook.Icon(painter, theme, icon, box.Center, iconSize, theme.Accent);
            y = box.Bottom + context.Px(18f);
        }
        else
        {
            BoardLook.Icon(painter, theme, icon, new UiVec2(centre, y + (iconSize / 2f)), iconSize, theme.Accent);
            y += iconSize + context.Px(14f);
        }

        float headSize = context.Px(warn ? 26f : 28f);
        string head = headline.ToUpperInvariant();
        float headWidth = UiPainter.MeasureLabel(theme.Display, head, headSize, 0.04f);
        painter.DrawLabel(theme.Display, head, new UiVec2(centre - (headWidth / 2f), y), headSize, theme.TextBright, 0.04f);
        y += headSize * 1.2f;
        float px = context.Px(14f);
        BoardLook.Centred(painter, theme.Body, theme.Body.Fit(detail, px, Bounds.Width), centre, y, px, theme.TextSecondary);
    }
}

/// <summary>
/// HOW POINTS WORK: a panel beside an empty board saying what a season's score counts -- a kill, an assist, an objective, a
/// win -- and that only official servers' matches count, under a linked account.
/// </summary>
/// <param name="theme">The theme.</param>
public sealed class PointsPanel(UiTheme theme) : UiElement
{
    private static readonly (string Name, int Points)[] Worth = [("Kill", 10), ("Assist", 5), ("Objective", 25), ("Win", 50)];

    /// <summary>Gets or sets the line at its foot, such as how long the season has left; empty for none.</summary>
    public string Foot { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, available.Y);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        BoardLook.Panel(painter, Bounds);
        float pad = context.Px(24f);
        float x = Bounds.X + pad;
        float right = Bounds.Right - pad;
        float y = Bounds.Y + context.Px(22f);
        BoardLook.Caption(painter, theme, "How points work", new UiVec2(x, y), 10f, theme.Accent);
        y += context.Px(20f);
        painter.DrawLabel(theme.Display, "SCORE THIS SEASON", new UiVec2(x, y), context.Px(28f), theme.TextBright, 0.03f);
        y += context.Px(42f);
        float px = context.Px(13f);
        foreach (string line in Wrap(theme, "Only matches on official servers count. Your Discord account must be linked on the Profile page.", px, right - x))
        {
            painter.DrawText(theme.Body, line, new UiVec2(x, y), px, theme.TextSecondary);
            y += theme.Body.LineHeight(px);
        }

        y += context.Px(18f);
        foreach ((string name, int points) in Worth)
        {
            var row = new UiRect(x, y, right - x, context.Px(42f));
            painter.FillRect(new UiRect(x, row.Bottom - context.Px(1f), row.Width, context.Px(1f)), UiPalette.White(0.1f));
            painter.DrawText(theme.BodyStrong, name, new UiVec2(x, UiRowStyle.CenterY(theme.BodyStrong, context.Px(15f), row)), context.Px(15f), theme.Text);
            BoardLook.Right(painter, theme.Mono, string.Create(CultureInfo.InvariantCulture, $"+{points}"), right, UiRowStyle.CenterY(theme.Mono, context.Px(16f), row), context.Px(16f), theme.Accent);
            y = row.Bottom;
        }

        if (Foot.Length > 0)
        {
            painter.DrawText(theme.Mono, Foot, new UiVec2(x, Bounds.Bottom - context.Px(34f)), context.Px(11f), theme.TextMuted);
        }
    }

    // Words laid in lines no wider than the room.
    internal static List<string> Wrap(UiTheme theme, string text, float px, float room)
    {
        List<string> lines = [];
        string line = string.Empty;
        foreach (string word in text.Split(' '))
        {
            string tried = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && theme.Body.MeasureWidth(tried, px) > room)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = tried;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }
}

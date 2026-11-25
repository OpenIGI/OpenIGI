using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// A mission board's head: the mission's picture at the right under a dark wash, edged in the accent along its top; the mission's
/// number, difficulty and rules over its name, and the server's check; at the right, the world record and who holds it, the
/// player's best and their place, and the gap between, over a bar of the top seven's times and the player's. With no run on the
/// board, the record stands UNCLAIMED, and a child (the way to play the mission) stands at the right.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="size">How much room the page has.</param>
public sealed class MissionHero(UiTheme theme, BoardSize size) : Panel
{
    /// <summary>Gets or sets the line over the name.</summary>
    public string Eyebrow { get; set; } = string.Empty;

    /// <summary>Gets or sets the mission's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the mission's picture, once it is to hand.</summary>
    public Func<UiPicture?> Picture { get; set; } = static () => null;

    /// <summary>Gets or sets the first row: the world record.</summary>
    public RunBoardRow? Record { get; set; }

    /// <summary>Gets or sets the player's best, or null.</summary>
    public RunBoardRow? Yours { get; set; }

    /// <summary>Gets or sets how many the board ranks.</summary>
    public int Players { get; set; }

    /// <summary>Gets or sets the board's rows, whose top seven's times the bar marks.</summary>
    public IReadOnlyList<RunBoardRow> Rows { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the board is read: until then the right is left empty.</summary>
    public bool Read { get; set; }

    /// <summary>Gets or sets the room at the right kept for a child, in design units.</summary>
    public float ChildRoom { get; set; }

    private bool Compact => size == BoardSize.Compact;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        base.MeasureContent(context, available);
        return new UiVec2(available.X, context.Px(size == BoardSize.Wide ? 212f : 150f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;

        // The ground: the mission's picture at the right under a wash, dark at the left where the words are.
        painter.FillRect(box, Bgra32.FromRgba(17, 18, 19, 235));
        if (Picture() is { } picture)
        {
            var art = new UiRect(box.X + (box.Width * 0.3f), box.Y, box.Width * 0.7f, box.Height);
            painter.PushClip(box);
            picture.DrawCover(painter, art, 0.9f);
            painter.PopClip();
        }

        Span<UiGradientStop> across = [new(0f, Bgra32.FromRgba(17, 18, 19, 247)), new(0.38f, Bgra32.FromRgba(17, 18, 19, 209)), new(0.7f, Bgra32.FromRgba(17, 18, 19, 89)), new(1f, Bgra32.FromRgba(17, 18, 19, 153))];
        painter.FillGradient(box, across, UiGradientDirection.Horizontal);
        Span<UiGradientStop> down = [new(0f, Bgra32.FromRgba(0, 0, 0, 0)), new(0.4f, Bgra32.FromRgba(0, 0, 0, 0)), new(1f, Bgra32.FromRgba(0, 0, 0, 140))];
        painter.FillGradient(box, down, UiGradientDirection.Vertical);
        painter.StrokeRect(box, context.Px(1f), UiPalette.White(0.1f));
        painter.FillRect(new UiRect(box.X, box.Y, box.Width, context.Px(2f)), theme.Accent);

        float pad = context.Px(Compact ? 16f : 32f);
        float x = box.X + pad;
        float nameSize = context.Px(size == BoardSize.Wide ? 84f : 56f);
        float block = context.Px(12f) + context.Px(8f) + (nameSize * 0.88f) + context.Px(12f) + context.Px(20f);
        float y = box.Center.Y - (block / 2f);
        BoardLook.Caption(painter, theme, Eyebrow, new UiVec2(x, y), 12f, theme.Accent);
        y += context.Px(20f);
        float rightStart = RightBlockLeft(context);
        painter.DrawLabel(theme.Display, theme.Display.Fit(Name.ToUpperInvariant(), nameSize, rightStart - x - context.Px(24f)), new UiVec2(x, y - (nameSize * 0.1f)), nameSize, theme.TextBright, 0.02f);
        y += (nameSize * 0.88f) + context.Px(12f);
        float mid = y + context.Px(10f);
        float end = BoardLook.Verified(painter, theme, x, mid, "Server-verified board", size: 10f);
        if (size == BoardSize.Wide || rightStart - end > context.Px(360f))
        {
            painter.DrawText(theme.Mono, "Every run is re-played by the server before it counts.", new UiVec2(end + context.Px(10f), mid - (theme.Mono.LineHeight(context.Px(13f)) / 2f)), context.Px(13f), theme.TextSecondary);
        }

        if (Read)
        {
            DrawFigures(painter);
        }
    }

    // Where the figures at the right begin.
    private float RightBlockLeft(LayoutContext context)
    {
        float right = Bounds.Right - context.Px(Compact ? 16f : 32f) - context.Px(ChildRoom);
        float columns = Record is null ? 260f : Yours is null ? 260f : size == BoardSize.Wide ? 620f : 460f;
        return right - context.Px(columns);
    }

    // The record, the player's best and the gap; and the bar of the top seven's times under them.
    private void DrawFigures(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        bool wide = size == BoardSize.Wide;
        float right = box.Right - context.Px(Compact ? 16f : 32f) - context.Px(ChildRoom);
        float figureSize = context.Px(wide ? 46f : 34f);
        float columnsTop = box.Y + context.Px(wide ? 34f : 26f);
        if (Record is not { } record)
        {
            BoardLook.Right(painter, theme.Mono, "WORLD RECORD", right, columnsTop, context.Px(10f), BoardLook.Gold);
            BoardLook.Right(painter, theme.Display, "UNCLAIMED", right, columnsTop + context.Px(16f), context.Px(wide ? 44f : 32f), theme.TextSecondary);
            BoardLook.Right(painter, theme.Mono, "No verified run on this board yet", right, columnsTop + context.Px(wide ? 66f : 52f), context.Px(11f), theme.TextMuted);
            return;
        }

        // Right to left: the gap, your best, the record, a rule between each.
        float columnGap = context.Px(28f);
        float x = right;
        float Column(string caption, Bgra32 captionInk, string figure, Bgra32 figureInk, string under, Bgra32 underInk, bool trophy = false)
        {
            float captionPx = context.Px(10f);
            float underPx = context.Px(12f);
            float width = MathF.Max(theme.Mono.MeasureWidth(figure, figureSize), MathF.Max(BoardLook.CaptionWidth(context, theme, caption, 10f) + (trophy ? context.Px(18f) : 0f), theme.Mono.MeasureWidth(under, underPx)));
            float left = x - width;
            float cx = left;
            if (trophy)
            {
                BoardLook.Icon(painter, KitIcon.Leaderboard, new UiVec2(cx + context.Px(6f), columnsTop + (captionPx / 2f)), context.Px(12f), captionInk);
                cx += context.Px(18f);
            }

            BoardLook.Caption(painter, theme, caption, new UiVec2(cx, columnsTop), 10f, captionInk);
            painter.DrawText(theme.Mono, figure, new UiVec2(left, columnsTop + context.Px(12f)), figureSize, figureInk);
            painter.DrawText(theme.Mono, under, new UiVec2(left, columnsTop + context.Px(16f) + (figureSize * 1.15f)), underPx, underInk);
            return left;
        }

        void Rule()
        {
            x -= columnGap;
            painter.FillRect(new UiRect(x, columnsTop + context.Px(4f), context.Px(1f), context.Px(70f)), UiPalette.White(0.22f));
            x -= columnGap;
        }

        if (Yours is { } own && own.Account != record.Account)
        {
            int gap = own.Ticks - record.Ticks;
            string off = record.Ticks > 0 ? string.Create(CultureInfo.InvariantCulture, $"{gap * 100.0 / record.Ticks:0.0}% off") : string.Empty;
            x = Column("Gap to WR", theme.TextSecondary, SplitsBar.Delta(gap), theme.TextBright, off, theme.TextSecondary);
            Rule();
            x = Column("Your best", theme.Accent, MissionRow.Time(own.Ticks), theme.Accent, string.Create(CultureInfo.InvariantCulture, $"#{own.Place:N0} of {Players:N0}"), theme.TextSecondary);
            Rule();
        }

        float recordLeft = Column("World record", BoardLook.Gold, MissionRow.Time(record.Ticks), BoardLook.Gold, string.Empty, theme.TextSecondary, trophy: true);
        float holderY = columnsTop + context.Px(16f) + (figureSize * 1.15f);
        float nameEnd = painter.DrawText(theme.Mono, record.Name, new UiVec2(recordLeft, holderY), context.Px(12f), theme.TextBright).X;
        BoardLook.Verified(painter, theme, nameEnd + context.Px(8f), holderY + (theme.Mono.LineHeight(context.Px(12f)) / 2f));

        if (!wide || Rows.Count < 2)
        {
            return;
        }

        // The bar: from the record to the slowest of the top seven (or the player), each a tick, the record gold, the player's
        // the accent, the stretch between them lit.
        List<int> marks = [.. Rows.Take(7).Select(static r => r.Ticks)];
        int you = Yours?.Ticks ?? 0;
        int low = (int)(Math.Floor(record.Ticks / 300.0) * 300);
        int high = (int)(Math.Ceiling(Math.Max(marks.Max(), you) / 300.0) * 300);
        if (high <= low)
        {
            high = low + 300;
        }

        float barWidth = context.Px(620f);
        var bar = new UiRect(right - barWidth, box.Bottom - context.Px(52f), barWidth, context.Px(4f));
        float At(int ticks) => bar.X + (bar.Width * Math.Clamp((ticks - low) / (float)(high - low), 0f, 1f));
        painter.FillRect(bar, UiPalette.White(0.1f));
        if (Yours is not null)
        {
            float from = At(record.Ticks);
            painter.FillRect(new UiRect(from, bar.Y, At(you) - from, bar.Height), UiPalette.AccentAt(0.45f));
        }

        foreach (int mark in marks.Skip(1))
        {
            painter.FillRect(new UiRect(At(mark), bar.Y - context.Px(3f), context.Px(2f), context.Px(10f)), theme.TextMuted);
        }

        painter.FillRect(new UiRect(At(record.Ticks), bar.Y - context.Px(7f), context.Px(3f), context.Px(18f)), BoardLook.Gold);
        if (Yours is not null)
        {
            painter.FillRect(new UiRect(At(you), bar.Y - context.Px(7f), context.Px(3f), context.Px(18f)), theme.Accent);
        }

        float labelY = bar.Bottom + context.Px(10f);
        float px = context.Px(10f);
        painter.DrawText(theme.Mono, Minutes(low), new UiVec2(bar.X, labelY), px, theme.TextMuted);
        BoardLook.Centred(painter, theme.Mono, Yours is null ? "top 7" : "top 7 + you", bar.Center.X, labelY, px, theme.TextMuted);
        BoardLook.Right(painter, theme.Mono, Minutes(high), bar.Right, labelY, px, theme.TextMuted);
    }

    // A whole number of seconds in ticks as minutes and seconds: 5:30.
    private static string Minutes(int ticks)
    {
        int seconds = Math.Max(0, ticks) / 30;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}

/// <summary>A run's time large under MISSION TIME, and beside it the rank it earned as stripes and a name.</summary>
/// <param name="theme">The theme.</param>
/// <param name="time">The time.</param>
/// <param name="ink">The time's colour.</param>
/// <param name="lit">How many of the rank's stripes are lit.</param>
/// <param name="rank">The rank's name.</param>
public sealed class RunTimeBlock(UiTheme theme, string time, Bgra32 ink, int lit, string rank) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(82f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        BoardLook.Caption(painter, theme, "Mission time", new UiVec2(Bounds.X, Bounds.Y), 10f, theme.TextSecondary);
        float size = context.Px(54f);
        float end = painter.DrawText(theme.Mono, time, new UiVec2(Bounds.X, Bounds.Y + context.Px(10f)), size, ink).X;
        float x = end + context.Px(18f);
        float baseline = Bounds.Bottom - context.Px(14f);
        BoardLook.Caption(painter, theme, "Rank earned", new UiVec2(x, baseline - context.Px(30f)), 10f, theme.TextSecondary);
        BoardLook.Stripes(painter, theme, x, baseline - context.Px(4f), lit, rank, 15f, theme.TextBright);
    }
}

using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>A caption in capitals at the left and a quieter note at the right: a panel's or a part's heading.</summary>
/// <param name="theme">The theme.</param>
/// <param name="caption">The caption.</param>
/// <param name="note">The note; empty for none.</param>
public sealed class CaptionRow(UiTheme theme, string caption, string note = "") : UiElement
{
    /// <summary>Gets or sets the caption's colour; the theme's secondary text when null.</summary>
    public Bgra32? Ink { get; set; }

    /// <summary>Gets or sets the caption's size, in design units.</summary>
    public float CaptionSize { get; set; } = 10f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(16f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        BoardLook.Caption(painter, theme, caption, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Mono, context.Px(CaptionSize), Bounds)), CaptionSize, Ink ?? theme.TextSecondary);
        if (note.Length > 0)
        {
            float px = context.Px(11f);
            BoardLook.Right(painter, theme.Mono, note, Bounds.Right, UiRowStyle.CenterY(theme.Mono, px, Bounds), px, theme.TextSecondary);
        }
    }
}

/// <summary>
/// The head of the panel beside a board: its caption and the selected one's place, then their mark large, their name, and the
/// tags that follow it (DISCORD, TOP 10, the week's movement, WORLD RECORD).
/// </summary>
/// <param name="theme">The theme.</param>
public sealed class SelectedHead(UiTheme theme) : UiElement
{
    /// <summary>Gets or sets the caption, such as SELECTED PLAYER.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Gets or sets the note at the right of the caption, such as #4 of 1,480; empty for none.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Gets or sets a tag at the right of the caption in place of the note, such as WORLD RECORD; empty for none.</summary>
    public string Badge { get; set; } = string.Empty;

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the picture, once it has come down.</summary>
    public Func<UiPicture?> Picture { get; set; } = static () => null;

    /// <summary>Gets or sets the mark's edge, in the place's colour.</summary>
    public Bgra32? Edge { get; set; }

    /// <summary>Gets or sets a value indicating whether the account is linked to Discord.</summary>
    public bool Linked { get; set; }

    /// <summary>Gets or sets the tags after the Discord one, in the accent: TOP 10, #1 OF 312.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the places climbed this week, or null.</summary>
    public int? Climbed { get; set; }

    /// <summary>Gets or sets a value indicating whether they are new on the board this week.</summary>
    public bool IsNew { get; set; }

    /// <summary>Gets or sets the mark's side, in design units.</summary>
    public float MarkSize { get; set; } = 76f;

    /// <summary>Gets or sets the name's size, in design units.</summary>
    public float NameSize { get; set; } = 40f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(16f + 18f + MarkSize));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        Bgra32 edge = Edge ?? theme.Accent;
        var head = new UiRect(box.X, box.Y, box.Width, context.Px(16f));
        BoardLook.Caption(painter, theme, Caption, new UiVec2(box.X, UiRowStyle.CapsCenterY(theme.Mono, context.Px(11f), head)), 11f, theme.Accent);
        if (Badge.Length > 0)
        {
            float width = BoardLook.TagWidth(context, theme, Badge);
            BoardLook.Tag(painter, theme, box.Right - width, head.Center.Y, Badge, BoardLook.OnAccent, BoardLook.Gold, BoardLook.Gold);
        }
        else if (Note.Length > 0)
        {
            float px = context.Px(12f);
            BoardLook.Right(painter, theme.Mono, Note, box.Right, UiRowStyle.CenterY(theme.Mono, px, head), px, theme.TextSecondary);
        }

        float side = context.Px(MarkSize);
        var mark = new UiRect(box.X, head.Bottom + context.Px(18f), side, side);
        BoardLook.Mark(painter, theme, mark, Name, Picture(), edge, 2f);
        float x = mark.Right + context.Px(18f);
        float nameSize = context.Px(NameSize);
        float tagRow = context.Px(20f);
        float block = (nameSize * 1.0f) + context.Px(8f) + tagRow;
        float top = mark.Center.Y - (block / 2f);
        painter.DrawLabel(theme.Display, theme.Display.Fit(Name.ToUpperInvariant(), nameSize, box.Right - x), new UiVec2(x, top - (nameSize * 0.06f)), nameSize, theme.TextBright, 0.03f);
        float mid = top + (nameSize * 1.0f) + context.Px(8f) + (tagRow / 2f);
        float end = x;
        if (Linked)
        {
            end = BoardLook.Discord(painter, theme, end, mid, size: 10f) + context.Px(6f);
        }

        foreach (string tag in Tags)
        {
            end = BoardLook.Tag(painter, theme, end, mid, tag, theme.Accent, theme.Accent.WithAlpha(128), size: 10f) + context.Px(6f);
        }

        if (IsNew || Climbed is not null)
        {
            end = BoardLook.Movement(painter, theme, end + context.Px(4f), mid, Climbed, IsNew, 12f);
            if (!IsNew && Climbed is not null)
            {
                painter.DrawText(theme.Mono, "this week", new UiVec2(end + context.Px(6f), mid - (theme.Mono.LineHeight(context.Px(11f)) / 2f)), context.Px(11f), theme.TextSecondary);
            }
        }
    }
}

/// <summary>
/// A run's splits against the player's own best: the run's time to each objective, one after another, as segments as wide as
/// they took, and under each how much slower (or faster) the player's own best was through it.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="theirs">The run's splits, in played ticks from its start: each objective's, then the end.</param>
/// <param name="yours">The player's own best's, alike; null where they have none.</param>
public sealed class SplitsBar(UiTheme theme, IReadOnlyList<int> theirs, IReadOnlyList<int>? yours) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(yours is null ? 30f : 52f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        if (theirs.Count == 0)
        {
            return;
        }

        float gap = context.Px(2f);
        float total = theirs[^1];
        float width = Bounds.Width - ((theirs.Count - 1) * gap);
        float x = Bounds.X;
        float height = context.Px(30f);
        int previous = 0;
        for (int i = 0; i < theirs.Count; i++)
        {
            int segment = Math.Max(0, theirs[i] - previous);
            float w = total > 0f ? width * (segment / total) : width / theirs.Count;
            var box = new UiRect(x, Bounds.Y, w, height);
            painter.FillRect(box, UiPalette.AccentAt(0.1f + (0.06f * i)));
            painter.FillRect(new UiRect(box.X, box.Y, box.Width, context.Px(2f)), theme.Accent);
            string time = MissionRow.Time(segment);
            float px = context.Px(12f);
            if (theme.Mono.MeasureWidth(time, px) < w - context.Px(4f))
            {
                BoardLook.Centred(painter, theme.Mono, time, box.Center.X, UiRowStyle.CenterY(theme.Mono, px, box), px, theme.TextBright);
            }

            if (yours is { } own && i < own.Count)
            {
                int ownSegment = own[i] - (i > 0 ? own[i - 1] : 0);
                int delta = ownSegment - segment;
                string said = Delta(delta);
                float deltaPx = context.Px(11f);
                if (theme.Mono.MeasureWidth(said, deltaPx) < w)
                {
                    BoardLook.Centred(painter, theme.Mono, said, box.Center.X, box.Bottom + context.Px(6f), deltaPx, delta < 0 ? theme.Success : theme.TextSecondary);
                }
            }

            previous = theirs[i];
            x += w + gap;
        }
    }

    /// <summary>A difference in ticks as seconds, signed: +1.9, -0.4.</summary>
    /// <param name="ticks">The difference.</param>
    /// <returns>The text.</returns>
    public static string Delta(int ticks) =>
        (ticks >= 0 ? "+" : "-") + (Math.Abs(ticks) / 30.0).ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>A wide strip on green: a shield and VERIFIED BY THE SERVER, and how.</summary>
/// <param name="theme">The theme.</param>
/// <param name="headline">The headline.</param>
/// <param name="detail">The line under it.</param>
public sealed class VerifiedStrip(UiTheme theme, string headline, string detail) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(58f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        Bgra32 green = theme.Success;
        painter.FillRect(Bounds, green.WithAlpha(18));
        painter.StrokeRect(Bounds, context.Px(1f), green.WithAlpha(102));
        float icon = context.Px(24f);
        var centre = new UiVec2(Bounds.X + context.Px(14f) + (icon / 2f), Bounds.Center.Y);
        Shield(painter, centre, icon, green);
        float x = Bounds.X + context.Px(14f) + icon + context.Px(12f);
        float head = context.Px(16f);
        float px = context.Px(12f);
        float top = Bounds.Center.Y - ((head * 1.05f) + context.Px(2f) + theme.Mono.LineHeight(px)) / 2f;
        painter.DrawLabel(theme.Display, headline.ToUpperInvariant(), new UiVec2(x, top), head, green, 0.06f);
        painter.DrawText(theme.Mono, theme.Mono.Fit(detail, px, Bounds.Right - x - context.Px(8f)), new UiVec2(x, top + (head * 1.05f) + context.Px(2f)), px, theme.TextSecondary);
    }

    /// <summary>Draws a shield's outline with a tick in it.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="centre">Its middle.</param>
    /// <param name="size">Its height, in pixels.</param>
    /// <param name="ink">Its colour.</param>
    public static void Shield(UiPainter painter, UiVec2 centre, float size, Bgra32 ink)
    {
        ArgumentNullException.ThrowIfNull(painter);
        float w = size * 0.42f;
        float h = size * 0.5f;
        ReadOnlySpan<UiVec2> outline =
        [
            new(centre.X, centre.Y - h),
            new(centre.X + w, centre.Y - (h * 0.7f)),
            new(centre.X + w, centre.Y + (h * 0.05f)),
            new(centre.X, centre.Y + h),
            new(centre.X - w, centre.Y + (h * 0.05f)),
            new(centre.X - w, centre.Y - (h * 0.7f)),
        ];
        if (!BoardLook.InClip(painter, new UiRect(centre.X - w, centre.Y - h, 2f * w, 2f * h)))
        {
            return;
        }

        painter.StrokePolygon(outline, painter.Context.Px(1.6f), ink);
        BoardLook.Icon(painter, KitIcon.Check, new UiVec2(centre.X, centre.Y - (h * 0.05f)), size * 0.45f, ink);
    }
}

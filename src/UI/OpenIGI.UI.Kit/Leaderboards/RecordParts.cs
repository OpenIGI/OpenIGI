using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit.Servers;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// The head of a player's record: their picture large and ringed in the accent, the game and season over their name, and a row
/// of tags under it -- their place this season, whether their Discord is linked, how long they have played.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="compact">Whether it is the narrow page's.</param>
public sealed class RecordHero(UiTheme theme, bool compact) : UiElement
{
    /// <summary>Gets or sets the line over the name, such as <c>I.G.I. 2 · SEASON NOVEMBER 2026</c>.</summary>
    public string Eyebrow { get; set; } = string.Empty;

    /// <summary>Gets or sets the player's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the player's picture, asked for each frame as it comes down.</summary>
    public Func<UiPicture?> Avatar { get; set; } = static () => null;

    /// <summary>Gets or sets whether the record is a linked account's.</summary>
    public bool Linked { get; set; }

    /// <summary>Gets or sets the tags under the name; the first is lit when <see cref="FirstLit"/>.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Gets or sets whether the first tag is the player's place, set in the accent.</summary>
    public bool FirstLit { get; set; }

    private float Side => compact ? 72f : 112f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Side + (compact ? 28f : 56f)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        Span<UiGradientStop> stops = [new(0f, UiPalette.AccentAt(0.2f)), new(0.55f, UiPalette.InkAt(0.82f)), new(1f, UiPalette.InkAt(0.86f))];
        painter.FillGradient(bounds, stops, UiGradientDirection.Horizontal);
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.White(0.1f));

        float pad = context.Px(compact ? 14f : 28f);
        float side = context.Px(Side);
        var box = new UiRect(bounds.X + pad, bounds.Center.Y - (side / 2f), side, side);
        KitDraw.PlayerMark(painter, theme, box, Name, Avatar(), Linked);
        painter.StrokeRect(box.Inflate(new Thickness(context.Px(3f))), context.Px(2f), theme.Accent);

        float x = box.Right + context.Px(compact ? 16f : 28f);
        float room = bounds.Right - pad - x;
        float eyebrowSize = context.Px(compact ? 12f : 14f);
        float nameSize = context.Px(compact ? 30f : 56f);
        float tagHeight = context.Px(compact ? 22f : 28f);
        float gap = context.Px(compact ? 6f : 10f);
        float block = (eyebrowSize * 0.72f) + gap + (nameSize * 0.74f) + gap + context.Px(4f) + tagHeight;
        float y = bounds.Center.Y - (block / 2f);

        painter.DrawLabel(theme.Heading, TextFit.ToWidth(theme.Heading, Eyebrow.ToUpperInvariant(), eyebrowSize, room), new UiVec2(x, y - (eyebrowSize * 0.14f)), eyebrowSize, theme.Accent, 0.14f);
        y += (eyebrowSize * 0.72f) + gap;

        string name = Name.ToUpperInvariant();
        float natural = UiPainter.MeasureLabel(theme.Display, name, nameSize, 0.02f);
        float size = natural > room && natural > 0f ? nameSize * (room / natural) : nameSize;
        var nameBox = new UiRect(x, y, room, nameSize * 0.74f);
        painter.DrawLabel(theme.Display, name, new UiVec2(x, UiRowStyle.CapsCenterY(theme.Display, size, nameBox)), size, theme.TextBright, 0.02f);
        y = nameBox.Bottom + gap + context.Px(4f);

        float tagSize = context.Px(compact ? 11f : 13f);
        float tagX = x;
        for (int i = 0; i < Tags.Count; i++)
        {
            Bgra32 ink = i == 0 && FirstLit ? theme.Accent : theme.TextSecondary;
            float width = theme.Mono.MeasureWidth(Tags[i], tagSize) + context.Px(compact ? 14f : 22f);
            if (tagX + width > bounds.Right - pad)
            {
                break;
            }

            var tag = new UiRect(tagX, y, width, tagHeight);
            painter.StrokeRect(tag, context.Px(1f), i == 0 && FirstLit ? theme.Accent : UiPalette.White(0.2f));
            painter.DrawText(theme.Mono, Tags[i], new UiVec2(tag.X + context.Px(compact ? 7f : 11f), UiRowStyle.CenterY(theme.Mono, tagSize, tag)), tagSize, ink);
            tagX = tag.Right + context.Px(8f);
        }
    }
}

/// <summary>One figure of a record: a big number, what it is, a quieter line under it, and a bar where it is a share.</summary>
/// <param name="theme">The theme.</param>
/// <param name="label">What the figure is, such as <c>Kills</c>.</param>
/// <param name="value">The figure.</param>
/// <param name="sub">The line under it, such as <c>1,204 all time</c>.</param>
public sealed class FigureTile(UiTheme theme, string label, string value, string sub) : UiElement
{
    /// <summary>Gets or sets whether the figure is the record's lead, set in the accent.</summary>
    public bool Lit { get; set; }

    /// <summary>Gets or sets a share, nought to one, drawn as a bar under the figure; <see langword="null"/> for none.</summary>
    public double? Share { get; set; }

    /// <summary>Gets or sets whether it is the narrow page's.</summary>
    public bool Compact { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 92f : 124f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        painter.FillRect(bounds, UiPalette.InkAt(0.78f));
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.White(0.08f));
        float pad = context.Px(Compact ? 12f : 22f);
        float x = bounds.X + pad;
        float room = bounds.Width - (2f * pad);
        float valueSize = context.Px(Compact ? 26f : 38f);
        float y = bounds.Y + pad;
        var valueBox = new UiRect(x, y, room, valueSize * 0.74f);
        painter.DrawLabel(theme.Display, TextFit.ToWidth(theme.Display, value, valueSize, room), new UiVec2(x, UiRowStyle.CapsCenterY(theme.Display, valueSize, valueBox)), valueSize, Lit ? theme.Accent : theme.TextBright, 0.02f);
        y = valueBox.Bottom + context.Px(Compact ? 8f : 12f);

        float labelSize = context.Px(Compact ? 11f : 13f);
        painter.DrawLabel(theme.Heading, label.ToUpperInvariant(), new UiVec2(x, y), labelSize, theme.TextMuted, 0.14f);
        y += labelSize + context.Px(Compact ? 6f : 8f);

        if (Share is { } share)
        {
            BoardColumns.DrawBar(painter, theme, new UiRect(x, y, room, context.Px(3f)), share);
            y += context.Px(8f);
        }

        float subSize = context.Px(Compact ? 12f : 14f);
        painter.DrawText(theme.Body, theme.Body.Fit(sub, subSize, room), new UiVec2(x, y), subSize, theme.TextSecondary);
    }
}

/// <summary>A medal a player has earned: its emblem, its name and what earned it.</summary>
/// <param name="theme">The theme.</param>
/// <param name="medal">The medal.</param>
public sealed class MedalChip(UiTheme theme, Medal medal) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(68f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        painter.FillRect(bounds, UiPalette.AccentAt(0.1f));
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.AccentAt(0.35f));

        float emblem = context.Px(40f);
        var centre = new UiVec2(bounds.X + context.Px(14f) + (emblem / 2f), bounds.Center.Y);
        painter.FillDiamond(centre, emblem, theme.Accent);
        KitDraw.FillStar(painter, centre, emblem * 0.5f, UiPalette.Ink);

        float x = centre.X + (emblem / 2f) + context.Px(14f);
        float room = bounds.Right - context.Px(12f) - x;
        float nameSize = context.Px(16f);
        float aboutSize = context.Px(13f);
        float top = bounds.Center.Y - ((theme.BodyStrong.LineHeight(nameSize) + theme.Body.LineHeight(aboutSize)) / 2f);
        painter.DrawText(theme.BodyStrong, theme.BodyStrong.Fit(medal.Name, nameSize, room), new UiVec2(x, top), nameSize, theme.TextBright);
        painter.DrawText(theme.Body, theme.Body.Fit(medal.About, aboutSize, room), new UiVec2(x, top + theme.BodyStrong.LineHeight(nameSize)), aboutSize, theme.TextMuted);
    }
}

/// <summary>
/// One of a record's latest matches: down its edge green for won and red for lost, the mode's badge, the map by name, when,
/// where and the points it gave, the player's kills / deaths / assists, and WON or LOST.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="match">The match.</param>
/// <param name="map">The map, by name.</param>
public sealed class MatchLine(UiTheme theme, RecordMatch match, string map) : UiElement
{
    /// <summary>Gets or sets whether it is the narrow page's.</summary>
    public bool Compact { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 60f : 66f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        RecordLine? you = match.Line;
        bool won = you?.Won == true;
        Bgra32 result = won ? theme.Success : theme.Danger;
        painter.FillRect(bounds, UiPalette.InkAt(0.72f));
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.White(0.08f));
        painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), result);

        float pad = context.Px(Compact ? 12f : 18f);
        float right = bounds.Right - pad;
        float resultSize = context.Px(13f);
        string word = won ? "WON" : "LOST";
        float wordWidth = UiPainter.MeasureLabel(theme.Heading, word, resultSize, 0.14f);
        painter.DrawLabel(theme.Heading, word, new UiVec2(right - wordWidth, UiRowStyle.CapsCenterY(theme.Heading, resultSize, bounds)), resultSize, result, 0.14f);
        right -= context.Px(56f);

        if (!Compact && you is not null)
        {
            float kdaSize = context.Px(15f);
            float kdaWidth = BoardColumns.MeasureKda(theme, kdaSize, you.Kills, you.Deaths, you.Assists);
            BoardColumns.DrawKda(painter, theme, new UiVec2(right - kdaWidth, UiRowStyle.CenterY(theme.Mono, kdaSize, bounds)), kdaSize, you.Kills, you.Deaths, you.Assists);
            right -= kdaWidth + context.Px(24f);
        }

        float x = bounds.X + context.Px(4f) + pad;
        UiRect badge = ModeLook.DrawBadge(painter, theme, new UiVec2(x, bounds.Center.Y - context.Px(12f)), match.Mode);
        x = badge.Right + context.Px(14f);
        float room = MathF.Max(0f, right - x);

        float nameSize = context.Px(16f);
        float underSize = context.Px(12.5f);
        float top = bounds.Center.Y - ((theme.BodyStrong.LineHeight(nameSize) + context.Px(3f) + theme.Mono.LineHeight(underSize)) / 2f);
        painter.DrawText(theme.BodyStrong, theme.BodyStrong.Fit(map, nameSize, room), new UiVec2(x, top), nameSize, theme.Text);

        string when = DateTimeOffset.FromUnixTimeMilliseconds(match.At).ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);
        string points = LeaderboardMetric.Count(you?.Score ?? 0) + " points";
        string under = Compact && you is not null
            ? $"{when}  ·  {BoardColumns.KdaText(you.Kills, you.Deaths, you.Assists)}  ·  {points}"
            : $"{when}  ·  {match.Server ?? "Official"}  ·  {points}";
        painter.DrawText(theme.Mono, theme.Mono.Fit(under, underSize, room), new UiVec2(x, top + theme.BodyStrong.LineHeight(nameSize) + context.Px(3f)), underSize, theme.TextMuted);
    }
}

/// <summary>A block of a page's shape while it is read, in place of what will stand there.</summary>
/// <param name="height">Its height, in design units.</param>
public sealed class SkeletonBlock(float height) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(height));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, UiPalette.White(0.05f));
}

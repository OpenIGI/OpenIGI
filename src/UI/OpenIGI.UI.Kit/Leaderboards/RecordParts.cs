using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// A record's head: washed from the left in their place's colour with its numeral large behind; their mark large; the game and
/// season over their name and its tags; at the right, their place this season with the week's movement, and their score with
/// how far they stand from the next place.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="size">How much room the page has.</param>
public sealed class RecordHero(UiTheme theme, BoardSize size) : UiElement
{
    /// <summary>Gets or sets the line over the name.</summary>
    public string Eyebrow { get; set; } = string.Empty;

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the picture, once it has come down.</summary>
    public Func<UiPicture?> Avatar { get; set; } = static () => null;

    /// <summary>Gets or sets a value indicating whether the account is linked to Discord.</summary>
    public bool Linked { get; set; }

    /// <summary>Gets or sets the tags after the name's first, quieter: 24.5 H PLAYED, 31 MATCHES.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Gets or sets their place this season, or null where they have none.</summary>
    public BoardRow? Placed { get; set; }

    /// <summary>Gets or sets how many are ranked this season.</summary>
    public int Players { get; set; }

    /// <summary>Gets or sets their score this season.</summary>
    public int Score { get; set; }

    /// <summary>Gets or sets the line under the score: how far from the next place, or what leads.</summary>
    public string ScoreNote { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether they are new on the board this week.</summary>
    public bool IsNew { get; set; }

    private bool Compact => size == BoardSize.Compact;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 82f : 176f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        int place = Placed?.Rank ?? 0;
        Bgra32 medal = place is >= 1 and <= 3 ? BoardLook.Medal(theme, place) : theme.Accent;
        Span<UiGradientStop> stops = [new(0f, medal.WithAlpha(51)), new(0.6f, Bgra32.FromRgba(17, 18, 19, 235)), new(1f, Bgra32.FromRgba(17, 18, 19, 235))];
        painter.FillGradient(box, stops, UiGradientDirection.Horizontal);
        if (place is >= 1 and <= 3 && !Compact)
        {
            painter.PushClip(box);
            string numeral = place.ToString(CultureInfo.InvariantCulture);
            float numeralSize = context.Px(299f);
            painter.DrawText(theme.Display, numeral, new UiVec2(box.Right - context.Px(40f) - theme.Display.MeasureWidth(numeral, numeralSize), box.Y - context.Px(61f) - (numeralSize * 0.1f)), numeralSize, medal.WithAlpha(15));
            painter.PopClip();
        }

        painter.StrokeRect(box, context.Px(1f), medal.WithAlpha(89));
        painter.FillRect(new UiRect(box.X, box.Y, context.Px(4f), box.Height), medal);

        float pad = context.Px(Compact ? 16f : 28f);
        float side = context.Px(Compact ? 54f : 120f);
        var mark = new UiRect(box.X + pad, box.Center.Y - (side / 2f), side, side);
        BoardLook.Mark(painter, theme, mark, Name, Avatar(), medal, 2f);

        // At the right: the place this season, and the score.
        float right = box.Right - pad;
        float placeLeft = right;
        if (Compact)
        {
            if (Placed is { } own)
            {
                float big = context.Px(36f);
                string text = $"#{own.Rank.ToString("N0", CultureInfo.InvariantCulture)}";
                placeLeft = BoardLook.Right(painter, theme.Display, text, right, box.Y + context.Px(12f), big, medal);
                BoardLook.Right(painter, theme.Mono, string.Create(CultureInfo.InvariantCulture, $"of {Players:N0}"), right, box.Y + context.Px(54f), context.Px(12f), theme.TextSecondary);
            }
        }
        else
        {
            float scoreSize = context.Px(44f);
            string score = LeaderboardMetric.Count(Score);
            float top = box.Center.Y - context.Px(40f);
            BoardLook.Right(painter, theme.Mono, "SCORE", right, top, context.Px(10f), theme.TextSecondary);
            float scoreLeft = BoardLook.Right(painter, theme.Mono, score, right, top + context.Px(14f), scoreSize, theme.TextBright);
            BoardLook.Right(painter, theme.Mono, ScoreNote, right, top + context.Px(14f) + (scoreSize * 1.2f), context.Px(12f), theme.TextSecondary);
            float rule = MathF.Min(scoreLeft, right - context.Px(150f)) - context.Px(28f);
            painter.FillRect(new UiRect(rule, box.Center.Y - context.Px(44f), context.Px(1f), context.Px(88f)), UiPalette.White(0.22f));
            float placeRight = rule - context.Px(28f);
            BoardLook.Right(painter, theme.Mono, "SEASON RANK", placeRight, top, context.Px(10f), theme.TextSecondary);
            if (Placed is { } own)
            {
                float of = context.Px(13f);
                string ofText = string.Create(CultureInfo.InvariantCulture, $"of {Players:N0}");
                float bigSize = context.Px(72f);
                float ofLeft = BoardLook.Right(painter, theme.Mono, ofText, placeRight, top + context.Px(14f) + (bigSize * 0.62f), of, theme.TextSecondary);
                string text = $"#{own.Rank.ToString("N0", CultureInfo.InvariantCulture)}";
                placeLeft = BoardLook.Right(painter, theme.Display, text, ofLeft - context.Px(8f), top + context.Px(8f), bigSize, medal);
                float moveY = top + context.Px(14f) + (bigSize * 0.92f);
                if (IsNew || own.Climbed is not null)
                {
                    float width = (IsNew ? BoardLook.TagWidth(context, theme, "New", 9f) : context.Px(30f)) + context.Px(70f);
                    float end = BoardLook.Movement(painter, theme, placeRight - width, moveY + context.Px(8f), own.Climbed, IsNew, 13f);
                    if (!IsNew)
                    {
                        painter.DrawText(theme.Mono, "this week", new UiVec2(end + context.Px(6f), moveY + context.Px(8f) - (theme.Mono.LineHeight(context.Px(12f)) / 2f)), context.Px(12f), theme.TextSecondary);
                    }
                }
            }
            else
            {
                placeLeft = BoardLook.Right(painter, theme.Display, "—", placeRight, top + context.Px(14f), context.Px(56f), theme.TextMuted);
            }
        }

        // Who: the game and season, the name, its tags.
        float x = mark.Right + context.Px(Compact ? 14f : 26f);
        float room = MathF.Max(0f, placeLeft - context.Px(24f) - x);
        float nameSize = context.Px(Compact ? 30f : 76f);
        float eyebrowSize = Compact ? 0f : context.Px(12f);
        float tagRow = context.Px(20f);
        float block = (Compact ? 0f : eyebrowSize + context.Px(8f)) + (nameSize * 0.92f) + context.Px(Compact ? 6f : 10f) + tagRow;
        float y = box.Center.Y - (block / 2f);
        if (!Compact)
        {
            BoardLook.Caption(painter, theme, Eyebrow, new UiVec2(x, y), 12f, theme.Accent);
            y += eyebrowSize + context.Px(8f);
        }

        painter.DrawLabel(theme.Display, theme.Display.Fit(Name.ToUpperInvariant(), nameSize, room), new UiVec2(x, y - (nameSize * 0.08f)), nameSize, theme.TextBright, 0.02f);
        y += (nameSize * 0.92f) + context.Px(Compact ? 6f : 10f);
        float mid = y + (tagRow / 2f);
        float tagEnd = x;
        if (Placed is { } placed && place > 0)
        {
            tagEnd = BoardLook.Tag(painter, theme, tagEnd, mid, string.Create(CultureInfo.InvariantCulture, $"#{placed.Rank:N0} this season"), BoardLook.OnAccent, medal, medal, 10f) + context.Px(8f);
        }

        if (Linked)
        {
            tagEnd = BoardLook.Discord(painter, theme, tagEnd, mid, size: 10f) + context.Px(8f);
        }

        foreach (string tag in Compact ? [] : Tags)
        {
            tagEnd = BoardLook.Tag(painter, theme, tagEnd, mid, tag, theme.TextSecondary, UiPalette.White(0.22f), size: 10f) + context.Px(8f);
        }
    }
}

/// <summary>
/// One of a record's latest matches: edged in green where it was won; the mode in a box; the map over the day, the server and
/// its region; the points it gave, the kills / deaths / assists, and WON or LOST.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="match">The match.</param>
/// <param name="map">The map, as the menus name it.</param>
public sealed class MatchLine(UiTheme theme, RecordMatch match, string map) : UiElement
{
    /// <summary>Gets or sets a value indicating whether it is laid out for a narrow page.</summary>
    public bool Compact { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 47f : 53f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        RecordLine? line = match.Line;
        bool won = line?.Won ?? false;
        painter.FillRect(box, Bgra32.FromRgba(17, 18, 19, 224));
        painter.FillRect(new UiRect(box.X, box.Bottom - context.Px(1f), box.Width, context.Px(1f)), UiPalette.White(0.08f));
        painter.FillRect(new UiRect(box.X, box.Y, context.Px(3f), box.Height), won ? theme.Success : UiPalette.White(0.15f));
        float mid = box.Center.Y;
        float x = box.X + context.Px(16f);

        // The mode, in a box.
        string mode = match.Mode.ToUpperInvariant() switch
        {
            "SD" => "S&D",
            var other => other,
        };
        float modeSize = context.Px(10f);
        var modeBox = new UiRect(x, mid - context.Px(11f), context.Px(42f), context.Px(22f));
        painter.StrokeRect(modeBox, context.Px(1f), UiPalette.White(0.22f));
        BoardLook.Centred(painter, theme.Mono, mode, modeBox.Center.X, UiRowStyle.CenterY(theme.Mono, modeSize, modeBox), modeSize, theme.TextSecondary);
        x = modeBox.Right + context.Px(14f);

        // At the right: WON or LOST, the line's figures, the points.
        float right = box.Right - context.Px(16f);
        float verdictSize = context.Px(13f);
        string verdict = won ? "WON" : "LOST";
        float verdictWidth = UiPainter.MeasureLabel(theme.Display, verdict, verdictSize, 0.08f);
        painter.DrawLabel(theme.Display, verdict, new UiVec2(right - verdictWidth, UiRowStyle.CapsCenterY(theme.Display, verdictSize, box)), verdictSize, won ? theme.Success : theme.TextMuted, 0.08f);
        right -= context.Px(Compact ? 50f : 60f);
        float small = context.Px(13f);
        float smallY = UiRowStyle.CenterY(theme.Mono, small, box);
        if (line is not null)
        {
            float kdaWidth = BoardColumns.MeasureKda(theme, small, line.Kills, line.Deaths, line.Assists);
            BoardColumns.DrawKda(painter, theme, new UiVec2(right - kdaWidth, smallY), small, line.Kills, line.Deaths, line.Assists);
            right -= kdaWidth + context.Px(18f);
            string points = string.Create(CultureInfo.InvariantCulture, $"+{line.Score:N0}");
            right = BoardLook.Right(painter, theme.Mono, points, right, smallY, small, theme.TextSecondary) - context.Px(18f);
        }

        // The map over the day, the server and its region.
        float room = MathF.Max(0f, right - x);
        float nameSize = context.Px(15f);
        float underSize = context.Px(11f);
        float top = mid - ((theme.BodyStrong.LineHeight(nameSize) + theme.Mono.LineHeight(underSize)) / 2f);
        painter.DrawText(theme.BodyStrong, theme.BodyStrong.Fit(map, nameSize, room), new UiVec2(x, top), nameSize, theme.TextBright);
        string when = DateTimeOffset.FromUnixTimeMilliseconds(match.At).UtcDateTime.ToString("d MMM", CultureInfo.InvariantCulture);
        string under = match.Server is { Length: > 0 } server ? $"{when}  ·  {server}" : when;
        painter.DrawText(theme.Mono, theme.Mono.Fit(under, underSize, room), new UiVec2(x, top + theme.BodyStrong.LineHeight(nameSize)), underSize, theme.TextMuted);
    }
}

/// <summary>
/// A player's standing on the mission boards, as a table: every mission together on top, then each mission with the rank their
/// fastest run earned, its time and its place, verified; NO RUN where they have none.
/// </summary>
/// <param name="theme">The theme.</param>
/// <param name="standing">Their standing.</param>
/// <param name="missions">The game's missions, in campaign order.</param>
public sealed class StandingTable(UiTheme theme, MissionStanding standing, IReadOnlyList<(int Id, string Name)> missions) : UiElement
{
    /// <summary>Gets or sets the most rows shown, the overall row among them.</summary>
    public int MaxRows { get; set; } = 9;

    private int Rows => Math.Min(MaxRows, missions.Count + 1);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(45f * Rows));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        BoardLook.Panel(painter, Bounds);
        float rowHeight = context.Px(45f);
        float pad = context.Px(15f);
        float right = Bounds.Right - pad;
        float y = Bounds.Y;

        // Every mission together.
        var overall = new UiRect(Bounds.X, y, Bounds.Width, rowHeight);
        painter.FillRect(overall, UiPalette.AccentAt(0.08f));
        painter.FillRect(new UiRect(overall.X, overall.Bottom - context.Px(1f), overall.Width, context.Px(1f)), UiPalette.White(0.1f));
        float nameSize = context.Px(15f);
        painter.DrawText(theme.BodyStrong, "All missions (overall)", new UiVec2(Bounds.X + pad, UiRowStyle.CenterY(theme.BodyStrong, nameSize, overall)), nameSize, theme.TextBright);
        if (standing.Overall is { } total)
        {
            float tagLeft = right - BoardLook.TagWidth(context, theme, "Verified", 9f) - context.Px(17f);
            BoardLook.Verified(painter, theme, tagLeft, overall.Center.Y);
            float figure = context.Px(16f);
            BoardLook.Right(painter, theme.Mono, $"#{total.Place.ToString("N0", CultureInfo.InvariantCulture)}", tagLeft - context.Px(12f), UiRowStyle.CenterY(theme.Mono, figure, overall), figure, theme.Accent);
        }
        else
        {
            float px = context.Px(12f);
            BoardLook.Right(painter, theme.Mono, "Not every mission run", right, UiRowStyle.CenterY(theme.Mono, px, overall), px, theme.TextMuted);
        }

        y += rowHeight;
        foreach ((int id, string name) in missions.Take(Rows - 1))
        {
            var row = new UiRect(Bounds.X, y, Bounds.Width, rowHeight);
            painter.FillRect(new UiRect(row.X, row.Bottom - context.Px(1f), row.Width, context.Px(1f)), UiPalette.White(0.06f));
            PlayerBest? best = standing.Missions.FirstOrDefault(b => b.Mission == id);
            painter.DrawText(theme.BodyStrong, name, new UiVec2(Bounds.X + pad, UiRowStyle.CenterY(theme.BodyStrong, nameSize, row)), nameSize, best is null ? theme.TextSecondary : theme.TextBright);
            float px = context.Px(12f);
            if (best?.Time is not { } time)
            {
                BoardLook.Right(painter, theme.Mono, "No run", right, UiRowStyle.CenterY(theme.Mono, px, row), px, theme.TextMuted);
                y += rowHeight;
                continue;
            }

            float tick = context.Px(12f);
            BoardLook.Icon(painter, KitIcon.Check, new UiVec2(right - (tick / 2f), row.Center.Y), tick, theme.Success);
            float at = right - tick - context.Px(14f);
            if (best.TimePlace is { } timePlace)
            {
                at = BoardLook.Right(painter, theme.Mono, $"#{timePlace.ToString("N0", CultureInfo.InvariantCulture)}", at, UiRowStyle.CenterY(theme.Mono, context.Px(14f), row), context.Px(14f), theme.TextBright) - context.Px(20f);
            }

            at = BoardLook.Right(painter, theme.Mono, MissionRow.Time(time.Ticks), at, UiRowStyle.CenterY(theme.Mono, context.Px(15f), row), context.Px(15f), theme.TextBright) - context.Px(26f);
            int rank = best.Best?.Rank ?? time.Rank;
            if (OpenIGI.Core.Titles.GameTitle.RankName(rank, standing.Difficulty) is { } rankName)
            {
                string caps = rankName.ToUpperInvariant();
                float rankSize = context.Px(12f);
                float width = UiPainter.MeasureLabel(theme.Heading, caps, rankSize, 0.1f);
                painter.DrawLabel(theme.Heading, caps, new UiVec2(at - width, UiRowStyle.CapsCenterY(theme.Heading, rankSize, row)), rankSize, theme.TextSecondary, 0.1f);
            }

            y += rowHeight;
        }
    }
}

/// <summary>A part's shape while it is read: a quiet block as tall as given.</summary>
/// <param name="height">Its height, in design units.</param>
public sealed class SkeletonBlock(float height) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(height));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, UiPalette.White(0.05f));
        painter.StrokeRect(Bounds, painter.Context.Px(1f), UiPalette.White(0.06f));
    }
}

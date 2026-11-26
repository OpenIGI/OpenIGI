using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One row of a mission board: the place in its medal's colour, the player's mark and name (WORLD RECORD after the first's, YOU
/// after the player's own), the time, the gap to the first, the rank the run earned as stripes and a name, the server's check,
/// and -- where its replay is kept -- RACE and WATCH; NO REPLAY where it isn't. The overall board's rows give each player's total
/// and how many missions it is of. Enter watches, a pointer watches or races by the button it presses, and the page binds a key
/// to <see cref="Race"/>.
/// </summary>
public sealed class MissionRow : Control
{
    private readonly RunBoardRow _row;
    private readonly string _metric;
    private readonly bool _overall;
    private readonly int _difficulty;
    private readonly RowButtonFeedback _feedback = new();
    private UiRect? _watch;
    private UiRect? _race;

    /// <summary>Initializes a new instance of the <see cref="MissionRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="row">The row.</param>
    /// <param name="metric">What the board ranks by: time or rank.</param>
    /// <param name="overall">Whether the board is every mission's together.</param>
    /// <param name="difficulty">The difficulty the board's runs were played at, which names their ranks.</param>
    public MissionRow(UiTheme theme, RunBoardRow row, string metric, bool overall, int difficulty)
        : base(theme)
    {
        _row = row ?? throw new ArgumentNullException(nameof(row));
        _metric = metric ?? throw new ArgumentNullException(nameof(metric));
        _overall = overall;
        _difficulty = difficulty;
        Id = "mission-row-" + row.Account;
    }

    /// <summary>Raised when the row's RACE is pressed, or <see cref="Race"/> is called.</summary>
    public event Action<MissionRow>? Raced;

    /// <summary>Gets the row.</summary>
    public RunBoardRow Row => _row;

    /// <summary>Gets or sets whether it is the player's own.</summary>
    public bool IsYou { get; set; }

    /// <summary>Gets or sets whether the player has reported its run.</summary>
    public bool Reported { get; set; }

    /// <summary>Gets whether its run can be reported: a mission's row, of another player's run.</summary>
    public bool CanReport => !_overall && !IsYou && _row.Run is not null;

    /// <summary>Gets or sets how much room the page has.</summary>
    public BoardSize Size { get; set; }

    /// <summary>Gets or sets the first row's time, which the gap is from; null where there is none.</summary>
    public int? Leader { get; set; }

    /// <summary>Gets or sets how many missions the game has, which an overall row's total is said against.</summary>
    public int MissionCount { get; set; }

    /// <summary>Gets or sets a value indicating whether it is pinned under the board as the player's best: two deep, washed.</summary>
    public bool Pinned { get; set; }

    /// <summary>Gets whether its run can be watched and raced: a mission's row whose replay is kept.</summary>
    public bool HasReplay => !_overall && _row.Replay && _row.Run is not null;

    private bool Compact => Size == BoardSize.Compact;

    /// <summary>A time in ticks as a mission's clock shows it: 5:04.0, or 1:20:33.3 past an hour.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <returns>The time.</returns>
    public static string Time(int ticks)
    {
        double seconds = Math.Max(0, ticks) / 30.0;
        int minutes = (int)(seconds / 60.0);
        double rest = seconds - (minutes * 60);
        return minutes >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:00}:{rest:00.0}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{rest:00.0}");
    }

    /// <summary>How many missions an overall total is of, said against the game's: 3 of 14 missions.</summary>
    /// <param name="missions">The missions the total is of.</param>
    /// <param name="count">The game's missions; nought where not known.</param>
    /// <returns>The words.</returns>
    public static string MissionsLine(int missions, int count) =>
        count > 0 && missions > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{missions} of {count} {(count == 1 ? "mission" : "missions")}")
            : string.Create(CultureInfo.InvariantCulture, $"{missions} {(missions == 1 ? "mission" : "missions")}");

    /// <summary>Races the row's run, where it can be.</summary>
    public void Race()
    {
        if (HasReplay && IsEnabled)
        {
            if (_race is { } race)
            {
                _feedback.Pressed(race);
            }

            Raced?.Invoke(this);
        }
    }

    /// <inheritdoc/>
    public override void ActivateAt(UiVec2 point)
    {
        if (_race is { } race && race.Contains(point))
        {
            Race();
            return;
        }

        base.ActivateAt(point);
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _feedback.PointerAt(point);

    /// <inheritdoc/>
    protected override void OnPointerOverChanged(bool over)
    {
        if (!over)
        {
            _feedback.PointerAt(null);
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (_watch is { } watch)
        {
            _feedback.Pressed(watch);
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _feedback.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Pinned ? 58f : Compact ? 45f : 45f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        if (Pinned)
        {
            Span<Drawing.UiGradientStop> stops = [new(0f, UiPalette.AccentAt(0.2f * appear)), new(0.6f, UiPalette.AccentAt(0.06f * appear)), new(1f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear)))];
            painter.FillGradient(bounds, stops, Drawing.UiGradientDirection.Horizontal);
            painter.StrokeRect(bounds, context.Px(1f), UiPalette.AccentAt(0.45f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }
        else if (IsYou)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.1f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        BoardLook.Lit(painter, Theme, bounds, Visual.Focus, IsPointerOver, appear);
        if (!Pinned)
        {
            painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.1f * appear));
        }

        MissionColumns cols = MissionColumns.Lay(context, bounds, Size);
        float mid = bounds.Center.Y;
        Bgra32 medal = _row.Place <= 3 ? BoardLook.Medal(Theme, _row.Place) : Theme.TextBright;
        bool first = _row.Place == 1;

        // The place.
        float placeSize = context.Px(Pinned ? 20f : 15f);
        painter.DrawText(Theme.Mono, LeaderboardMetric.Count(_row.Place), new UiVec2(cols.Rank, UiRowStyle.CenterY(Theme.Mono, placeSize, bounds)), placeSize, UiPalette.Fade(Pinned ? Theme.Accent : _row.Place > 20 ? Theme.TextMuted : medal, appear));

        // The buttons, or why there are none; then the check, the rank, the gap and the time, right to left.
        DrawButtons(painter, cols, appear);
        if (!_overall && !Compact)
        {
            float tick = context.Px(14f);
            BoardLook.Icon(painter, Theme, KitIcon.Check, new UiVec2(cols.Check + (tick / 2f), mid), tick, UiPalette.Fade(Theme.Success, appear));
            painter.DrawText(Theme.Mono, "OK", new UiVec2(cols.Check + tick + context.Px(5f), UiRowStyle.CenterY(Theme.Mono, context.Px(10f), bounds)), context.Px(10f), UiPalette.Fade(Theme.Success, appear));
        }

        if (!Compact)
        {
            if (_overall)
            {
                string missions = MissionsLine(_row.Missions, MissionCount);
                if (_metric == "rank")
                {
                    missions = string.Create(CultureInfo.InvariantCulture, $"{_row.Points:N0} pts  ·  {missions}");
                }
                painter.DrawText(Theme.Mono, missions, new UiVec2(cols.Rank2, UiRowStyle.CenterY(Theme.Mono, context.Px(12f), bounds)), context.Px(12f), UiPalette.Fade(Theme.TextSecondary, appear));
            }
            else
            {
                int lit = BoardLook.StripesLit(_row.Rank, OpenIGI.Core.Titles.GameTitle.RanksPerDifficulty);
                string rank = OpenIGI.Core.Titles.GameTitle.RankName(_row.Rank, _difficulty) ?? "No rank";
                BoardLook.Stripes(painter, Theme, cols.Rank2, mid, lit, rank, 12f, Theme.TextSecondary, appear);
            }
        }

        float timeSize = context.Px(Pinned ? 18f : 16f);
        Bgra32 timeInk = first ? BoardLook.Gold : IsYou || Pinned ? Theme.Accent : Theme.TextBright;
        BoardLook.Right(painter, Theme.Mono, Time(_row.Ticks), cols.TimeRight, UiRowStyle.CenterY(Theme.Mono, timeSize, bounds), timeSize, UiPalette.Fade(timeInk, appear));
        float gapSize = context.Px(12f);
        string gap = first ? "WR" : Leader is { } leader ? SplitsBar.Delta(_row.Ticks - leader) : string.Empty;
        BoardLook.Right(painter, Theme.Mono, gap, cols.GapRight, UiRowStyle.CenterY(Theme.Mono, gapSize, bounds), gapSize, UiPalette.Fade(first ? BoardLook.Gold : Theme.TextSecondary, appear));
        if (Compact && !_overall)
        {
            float tick = context.Px(13f);
            BoardLook.Icon(painter, Theme, KitIcon.Check, new UiVec2(cols.Check + (tick / 2f), mid), tick, UiPalette.Fade(Theme.Success, appear));
        }

        // The player: the mark, the name, and its tags.
        float side = context.Px(Pinned ? 34f : 30f);
        var mark = new UiRect(cols.Player, mid - (side / 2f), side, side);
        BoardLook.Mark(painter, Theme, mark, _row.Name, null, _row.Place <= 3 ? medal : Theme.Accent, 1.5f, appear);
        float x = mark.Right + context.Px(12f);
        float nameSize = context.Px(15f);
        float room = MathF.Max(0f, cols.PlayerRight - x);
        if (Pinned)
        {
            float eyebrow = context.Px(10f);
            float top = mid - ((eyebrow + context.Px(5f) + Theme.BodyStrong.LineHeight(context.Px(17f))) / 2f);
            BoardLook.Caption(painter, Theme, Compact ? "Your best" : "Your best · always shown", new UiVec2(x, top), 10f, UiPalette.Fade(Theme.Accent, appear));
            float nameTop = top + eyebrow + context.Px(5f);
            float end = painter.DrawText(Theme.BodyStrong, Theme.BodyStrong.Fit(_row.Name, context.Px(17f), room), new UiVec2(x, nameTop), context.Px(17f), UiPalette.Fade(Theme.TextBright, appear)).X;
            BoardLook.You(painter, Theme, end + context.Px(8f), nameTop + (Theme.BodyStrong.LineHeight(context.Px(17f)) / 2f), appear);
            return;
        }

        string tag = first && !_overall ? "World record" : IsYou ? "You" : Reported ? "Reported" : string.Empty;
        float tagWidth = tag.Length > 0 && !Compact ? BoardLook.TagWidth(context, Theme, tag) + context.Px(10f) : 0f;
        float nameEnd = painter.DrawText(Theme.BodyStrong, Theme.BodyStrong.Fit(_row.Name, nameSize, room - tagWidth), new UiVec2(x, UiRowStyle.CenterY(Theme.BodyStrong, nameSize, bounds)), nameSize, UiPalette.Fade(Theme.TextBright, appear)).X;
        if (tagWidth > 0f)
        {
            if (tag == "World record")
            {
                Bgra32 gold = UiPalette.Fade(BoardLook.Gold, appear);
                BoardLook.Tag(painter, Theme, nameEnd + context.Px(10f), mid, tag, UiPalette.Fade(BoardLook.OnAccent, appear), gold, gold);
            }
            else if (IsYou)
            {
                BoardLook.You(painter, Theme, nameEnd + context.Px(10f), mid, appear);
            }
            else
            {
                BoardLook.Tag(painter, Theme, nameEnd + context.Px(10f), mid, tag, UiPalette.Fade(Theme.TextMuted, appear), UiPalette.White(0.22f * appear));
            }
        }
    }

    // RACE and WATCH where the replay is kept: RACE in the accent, solid on the row with the keys; WATCH a quiet outline,
    // brighter on that row. Each lights under the pointer and flashes when pressed.
    private void DrawButtons(UiPainter painter, MissionColumns cols, float appear)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float mid = bounds.Center.Y;
        _watch = null;
        _race = null;
        if (_overall)
        {
            return;
        }

        if (!HasReplay)
        {
            if (!Compact)
            {
                float px = context.Px(11f);
                BoardLook.Right(painter, Theme.Mono, Pinned ? "No replay kept below #20" : "No replay · top 20 only", cols.ButtonsRight, UiRowStyle.CenterY(Theme.Mono, px, bounds), px, UiPalette.Fade(Theme.TextMuted, appear));
            }

            return;
        }

        bool rowLit = Visual.Focus > 0.5f;
        float height = context.Px(30f);
        float watchWidth = context.Px(Compact ? 92f : 104f);
        float raceWidth = context.Px(Compact ? 84f : 92f);
        var watch = new UiRect(cols.ButtonsRight - watchWidth, mid - (height / 2f), watchWidth, height);
        var race = new UiRect(watch.X - context.Px(8f) - raceWidth, watch.Y, raceWidth, height);
        DrawRowButton(painter, race, "RACE", KitIcon.Play, primary: true, rowLit, appear);
        DrawRowButton(painter, watch, "WATCH", KitIcon.Eye, primary: false, rowLit, appear);
        (_watch, _race) = (watch, race);
    }

    private void DrawRowButton(UiPainter painter, UiRect box, string caption, KitIcon icon, bool primary, bool rowLit, float appear)
    {
        LayoutContext context = painter.Context;
        float hover = _feedback.IsOver(box) ? 1f : 0f;
        float flash = _feedback.Flash(box);
        Bgra32 ink;
        if (primary && rowLit)
        {
            // The row's main button, solid: brighter under the pointer.
            painter.FillRect(box, UiPalette.Fade(hover > 0f ? Theme.AccentHover : Theme.Accent, appear));
            ink = BoardLook.OnAccent;
        }
        else if (primary)
        {
            painter.FillRect(box, UiPalette.AccentAt((0.07f + (0.13f * hover)) * appear));
            painter.StrokeRect(box, context.Px(1f), UiPalette.AccentAt((0.45f + (0.55f * hover)) * appear));
            ink = hover > 0f ? Theme.AccentHover : Theme.Accent;
        }
        else
        {
            float edge = rowLit ? 0.4f : 0.16f;
            painter.FillRect(box, UiPalette.White((0.03f + (0.07f * hover)) * appear));
            painter.StrokeRect(box, context.Px(1f), UiPalette.White(MathF.Min(1f, edge + (0.45f * hover)) * appear));
            ink = rowLit || hover > 0f ? Theme.TextBright : Theme.TextSecondary;
        }

        // A press flashes the button white for a moment.
        if (flash > 0.001f)
        {
            painter.FillRect(box, UiPalette.White(0.35f * flash * appear));
        }

        ink = UiPalette.Fade(ink, appear);
        float captionSize = context.Px(12f);
        float iconSize = context.Px(icon == KitIcon.Play ? 10f : 13f);
        float gap = context.Px(7f);
        float captionWidth = UiPainter.MeasureLabel(Theme.Heading, caption, captionSize, 0.14f);
        float x = box.Center.X - ((iconSize + gap + captionWidth) / 2f);
        BoardLook.Icon(painter, Theme, icon, new UiVec2(x + (iconSize / 2f), box.Center.Y), iconSize, ink);
        painter.DrawLabel(Theme.Heading, caption, new UiVec2(x + iconSize + gap, UiRowStyle.CapsCenterY(Theme.Heading, captionSize, box)), captionSize, ink, 0.14f);
    }
}

/// <summary>
/// Where a mission board row's columns stand: the place, the player, the time, the gap, the rank earned, the check and the
/// buttons. On a phone the rank earned goes, and the check stands after the name.
/// </summary>
/// <param name="Rank">The place's left.</param>
/// <param name="Player">The player's mark's left.</param>
/// <param name="PlayerRight">The player's column's right.</param>
/// <param name="TimeRight">The time's right.</param>
/// <param name="GapRight">The gap's right.</param>
/// <param name="Rank2">The rank earned's left.</param>
/// <param name="Check">The check's left.</param>
/// <param name="ButtonsRight">The buttons' right.</param>
public readonly record struct MissionColumns(float Rank, float Player, float PlayerRight, float TimeRight, float GapRight, float Rank2, float Check, float ButtonsRight)
{
    /// <summary>Lays a row's columns: 58, the player, 120, 96, 190, 108 and 204 across on a monitor.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="bounds">The row.</param>
    /// <param name="size">How much room the page has.</param>
    /// <returns>Where each column stands.</returns>
    public static MissionColumns Lay(LayoutContext context, UiRect bounds, BoardSize size)
    {
        ArgumentNullException.ThrowIfNull(context);
        float gap = context.Px(16f);
        float pad = context.Px(16f);
        float left = bounds.X + pad;
        float right = bounds.Right - pad;
        if (size == BoardSize.Compact)
        {
            float buttons = right;
            float gapRight = buttons - context.Px(92f + 8f + 84f) - gap;
            float time = gapRight - context.Px(58f) - context.Px(8f);
            float check = time - context.Px(80f) - context.Px(10f);
            return new MissionColumns(left, left + context.Px(34f), check - gap, time, gapRight, 0f, check, buttons);
        }

        float buttonsRight = right;
        float check2 = buttonsRight - context.Px(204f) - gap - context.Px(108f);
        float rank = check2 - gap - context.Px(190f);
        float gapRight2 = rank - gap;
        float timeRight = gapRight2 - context.Px(96f) - gap;
        float player = left + context.Px(58f) + gap;
        return new MissionColumns(left, player, timeRight - context.Px(120f) - gap, timeRight, gapRight2, rank, check2, buttonsRight);
    }

    /// <summary>A mission board's headings, in the columns its rows lay.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="size">How much room the page has.</param>
    /// <param name="overall">Whether the board is every mission's together.</param>
    /// <returns>The headings.</returns>
    public static UiElement Header(UiTheme theme, BoardSize size, bool overall) => new Heading(theme, size, overall);

    private sealed class Heading(UiTheme theme, BoardSize size, bool overall) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(28f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            MissionColumns at = Lay(context, Bounds, size);
            float y = UiRowStyle.CapsCenterY(theme.Mono, context.Px(11f), Bounds);
            void Left(string text, float x) => BoardLook.Caption(painter, theme, text, new UiVec2(x, y), 11f, theme.TextMuted);
            void Right(string text, float right) => Left(text, right - BoardLook.CaptionWidth(context, theme, text, 11f));
            Left("#", at.Rank);
            Left("Player", at.Player);
            Right(overall ? "Total" : "Time", at.TimeRight);
            Right("Gap", at.GapRight);
            if (size != BoardSize.Compact)
            {
                Left(overall ? "Missions" : "Rank earned", at.Rank2);
                if (!overall)
                {
                    Left("Check", at.Check);
                    Right("Ghost · replay", at.ButtonsRight);
                }
            }

            painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.22f));
        }
    }
}

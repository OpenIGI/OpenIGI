using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One row of a mission board, in the matches board's look: the place, the player's mark and name, their figure -- a time, or a
/// rank and its time, or an overall total -- and, where the run's replay is kept, WATCH and RACE beside it. Enter watches; a
/// pointer watches or races by the button it presses; the page binds a key to <see cref="Race"/>.
/// </summary>
public sealed class MissionRow : Control
{
    private readonly RunBoardRow _row;
    private readonly string _metric;
    private readonly bool _overall;
    private readonly int _difficulty;
    private UiRect? _watch;
    private UiRect? _race;
    private readonly RowButtonFeedback _feedback = new();

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

    /// <summary>Gets or sets whether it is laid out for a narrow page.</summary>
    public bool Compact { get; set; }

    /// <summary>Gets whether its run can be watched and raced: a mission's row whose replay is kept.</summary>
    public bool HasReplay => !_overall && _row.Replay && _row.Run is not null;

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

        if (_watch is { } watch && watch.Contains(point))
        {
            base.ActivateAt(point);
        }
    }

    /// <summary>A time as a board shows it: minutes, seconds and tenths.</summary>
    /// <param name="ticks">The ticks, thirty a second.</param>
    /// <returns>The time.</returns>
    public static string Time(int ticks)
    {
        double seconds = Math.Max(0, ticks) / 30.0;
        int minutes = (int)(seconds / 60.0);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds - (minutes * 60):00.0}");
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
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 52f : 58f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        if (IsYou)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.12f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.06f * hot * appear));
        }

        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(bounds, context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.07f * appear));

        float pad = context.Px(Compact ? 10f : 18f);
        float mid = bounds.Center.Y;
        float small = context.Px(Compact ? 13f : 14f);
        float nameSize = context.Px(Compact ? 15f : 17f);
        float figureSize = context.Px(Compact ? 17f : 20f);

        // The place, medal-coloured for the top three.
        string place = string.Create(CultureInfo.InvariantCulture, $"#{_row.Place}");
        Bgra32 placeInk = _row.Place <= 3 ? PodiumCard.MedalColour(Theme, _row.Place) : Theme.TextSecondary;
        float x = bounds.X + pad;
        painter.DrawText(Theme.Mono, place, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, small, bounds)), small, UiPalette.Fade(placeInk, appear));
        x += context.Px(Compact ? 44f : 60f);

        // The buttons at the right, where the run can be watched and raced.
        float right = bounds.Right - pad;
        _watch = null;
        _race = null;
        // A mission's rows keep the buttons' room whether or not their replay is kept, so every figure stands in one column.
        if (!_overall)
        {
            float height = context.Px(Compact ? 28f : 32f);
            float top = mid - (height * 0.5f);
            float labelSize = context.Px(Compact ? 13f : 14f);
            UiRect Button(string caption, bool lead)
            {
                float width = UiPainter.MeasureLabel(Theme.Heading, caption, labelSize, 0.16f) + context.Px(22f);
                UiRect box = new(right - width, top, width, height);
                right = box.X - context.Px(8f);
                if (!HasReplay)
                {
                    return box;
                }

                Bgra32 border = _feedback.Tint(lead ? Interpolate.Lerp(UiPalette.White(0.18f), Theme.Accent, hot) : UiPalette.White(0.18f), Theme, box);
                Bgra32 ink = _feedback.Tint(lead ? Interpolate.Lerp(Theme.Text, Theme.Accent, hot) : Theme.Text, Theme, box);
                _feedback.DrawFill(painter, Theme, box, appear);
                painter.StrokeRect(box, context.Px(1f), UiPalette.Fade(border, appear));
                painter.DrawLabel(Theme.Heading, caption, new UiVec2(box.X + context.Px(11f), UiRowStyle.CapsCenterY(Theme.Heading, labelSize, box)), labelSize, UiPalette.Fade(ink, appear), 0.16f);
                return box;
            }

            UiRect watch = Button("WATCH", lead: true);
            UiRect race = Button("RACE", lead: false);
            if (HasReplay)
            {
                (_watch, _race) = (watch, race);
            }

            right -= context.Px(10f);
        }

        // The figure, at the right of the room left: the time, a rank and its time, or an overall total.
        (string figure, string? under) = Figure();
        float figureWidth = UiPainter.MeasureLabel(Theme.Display, figure, figureSize, 0.02f);
        painter.DrawLabel(Theme.Display, figure, new UiVec2(right - figureWidth, UiRowStyle.CapsCenterY(Theme.Display, figureSize, bounds) - (under is null ? 0f : context.Px(7f))), figureSize, UiPalette.Fade(Theme.Text, appear), 0.02f);
        if (under is not null)
        {
            float underWidth = UiPainter.MeasureLabel(Theme.Mono, under, small, 0f);
            painter.DrawText(Theme.Mono, under, new UiVec2(right - underWidth, mid + context.Px(4f)), small, UiPalette.Fade(Theme.TextMuted, appear));
        }

        right -= MathF.Max(figureWidth, context.Px(70f)) + context.Px(18f);

        // The player: their mark and name in the room between.
        float side = context.Px(Compact ? 28f : 34f);
        KitDraw.PlayerMark(painter, Theme, new UiRect(x, mid - (side / 2f), side, side), _row.Name, null, signedIn: true, appear);
        x += side + context.Px(12f);
        string tag = IsYou ? "  ·  YOU" : Reported ? "  ·  REPORTED" : string.Empty;
        string name = Theme.BodyStrong.Fit(_row.Name + tag, nameSize, MathF.Max(0f, right - x));
        painter.DrawText(Theme.BodyStrong, name, new UiVec2(x, UiRowStyle.CenterY(Theme.BodyStrong, nameSize, bounds)), nameSize, UiPalette.Fade(Theme.Text, appear));
    }

    // What the row's figure says, and the smaller line under it.
    private (string Figure, string? Under) Figure()
    {
        if (_overall)
        {
            return _metric == "rank"
                ? (string.Create(CultureInfo.InvariantCulture, $"{_row.Points} PTS"), Time(_row.Ticks))
                : (Time(_row.Ticks), null);
        }

        // The rank by its name, as the mission's own screen gives it: IGI Operative, not the second of Hard's three.
        string rank = (OpenIGI.Core.Titles.GameTitle.RankName(_row.Rank, _difficulty) ?? "No rank").ToUpperInvariant();
        return _metric == "rank" ? (rank, Time(_row.Ticks)) : (Time(_row.Ticks), rank);
    }
}

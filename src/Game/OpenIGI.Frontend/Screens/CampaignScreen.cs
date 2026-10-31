using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The campaign section: the mission list down the left, and the selected mission's name set large over the
/// level, which is previewed live behind the menu, with its conditions and the PLAY button under it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The level is the picture.</b> The shell streams the selected mission in behind the menu so the
/// player sees where they are about to go; the page keeps it visible, with one column of glass-free rows
/// on the left and the briefing set directly on the scene.
/// </para>
/// <para>
/// Selection follows focus: moving to a row is choosing it, so one press of down changes the briefing and
/// the level behind it. The name changes with the design's reveal — the old one wipes out, the new one
/// wipes in from below out of a blur, a light sweeps across, the level pushes in and settles — and the
/// preview goes through <see cref="IShellHost.PreviewMission"/>, which the app debounces, so scrubbing
/// the list does not load fourteen levels.
/// </para>
/// <para>
/// <b>One campaign at a time.</b> The game's own comes first, unlocked as the profile progresses; each campaign a mod adds
/// follows it, every mission open. With more than one, a cycler over the list switches between them.
/// </para>
/// </remarks>
public sealed class CampaignScreen : ShellScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;

    // Indexed by MissionTimeOfDay and MissionWeather, whose Default is deliberately zero -- so a cycler's
    // selected index IS the enumeration value and no lookup table can fall out of step with it.
    private static readonly string[] TimesOfDay = ["Default", "Day", "Night"];

    private static readonly string[] Weathers = ["Default", "Rain", "Snow"];

    private readonly List<ListCard> _cards = [];
    private readonly TitleReveal _reveal = new();
    private List<MissionInfo> _missions = [];
    private int _campaign;

    private CampaignHeader? _header;
    private MissionHero? _hero;
    private PrimaryAction? _play;
    private OptionCycler? _difficulty;
    private int _selected = -1;
    private int _shown;

    /// <summary>Initializes a new instance of the <see cref="CampaignScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public CampaignScreen(ShellContext context)
        : base(context)
    {
    }

    /// <summary>Gets the index of the mission the list currently has selected.</summary>
    public int SelectedMission => Math.Max(0, _selected);

    /// <inheritdoc/>
    public override float BackdropScale => _reveal.BackgroundScale;

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Campaign;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("↑↓", "Select mission"), new("ENTER", "Play"), new("ESC", "Back")];

    /// <inheritdoc/>
    protected override Control? InitialFocus =>
        _selected >= 0 && _selected < _cards.Count ? _cards[_selected] : null;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        _cards.Clear();
        List<string?> campaigns = Campaigns;
        _campaign = Math.Clamp(_campaign, 0, Math.Max(0, campaigns.Count - 1));
        _missions = [.. Context.Missions.Where(mission => campaigns.Count == 0 || mission.Campaign == campaigns[_campaign])];

        if (_missions.Count == 0)
        {
            return new Label(theme.Body, "No missions were found in this installation.", 22f, theme.TextMuted)
            {
                Margin = new Thickness(SideMargin, 56f),
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        // The first time the page opens it lands on the mission the profile is up to, not on the first.
        if (_selected < 0)
        {
            _selected = Math.Clamp(Frontier(), 0, _missions.Count - 1);
            _shown = _selected;
        }

        var page = new Canvas();
        page.Add(BuildMissionList(theme), new Anchor(UiVec2.Zero, new UiVec2(0f, 1f), new Thickness(SideMargin, 56f, -(SideMargin + 560f), 48f)));
        page.Add(BuildDetail(theme), new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(720f, 0f, SideMargin, 48f)));

        Refresh();
        _reveal.Enter();
        return page;
    }

    /// <inheritdoc/>
    protected override void OnBuilt() => Preview();

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        PlayerCheatCVars.CheatsEnabled.Changed += OnUnlockCheatChanged;
        PlayerCheatCVars.UnlockAllLevels.Changed += OnUnlockCheatChanged;
    }

    /// <inheritdoc/>
    protected override void OnExit()
    {
        PlayerCheatCVars.CheatsEnabled.Changed -= OnUnlockCheatChanged;
        PlayerCheatCVars.UnlockAllLevels.Changed -= OnUnlockCheatChanged;
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _reveal.Update(elapsedSeconds);

        // Selection follows focus, so one press of down changes the briefing and the level behind it. The
        // rows are focusable in their own right, which covers the pointer, the keyboard and the pad alike.
        if (Focus?.Focused is ListCard focused)
        {
            int index = _cards.IndexOf(focused);
            if (index >= 0 && index != _selected)
            {
                Select(index);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawBackground(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        var screen = new UiRect(0f, 0f, painter.Context.Viewport.X, painter.Context.Viewport.Y);
        KitDraw.Sweep(painter, screen, _reveal.SweepProgress, _reveal.SweepOpacity);
    }

    private Grid BuildMissionList(UiTheme theme)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        // A mod's campaigns beside the game's: one cycler picks which the list shows.
        List<string?> campaigns = Campaigns;
        if (campaigns.Count > 1)
        {
            var cycler = new OptionCycler(theme, "Campaign", [.. campaigns.Select(CampaignName)], _campaign)
            {
                // A cycler fills the height it is given; the conditions' cells are this tall.
                Height = 96f,
                ContentWidth = 260f,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0f, 0f, 0f, 16f),
            };
            cycler.SelectionChanged += index =>
            {
                _campaign = index;
                _selected = -1;
                Invalidate();
            };
            layout.Add(cycler, 0, 0);
        }

        string title = IsGameCampaign ? "CAMPAIGN" : CampaignName(campaigns[_campaign]).ToUpperInvariant();
        _header = new CampaignHeader(theme, _missions.Count, title);
        layout.Add(_header, 1, 0);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        for (int i = 0; i < _missions.Count; i++)
        {
            int index = i;
            MissionInfo mission = _missions[i];
            var card = new ListCard(theme, mission.Number.ToString("00", CultureInfo.InvariantCulture), mission.Name)
            {
                Selected = i == _selected,
            };
            card.Activated += _ => Select(index);
            _cards.Add(card);
            rows.AddChild(card);
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        layout.Add(scroll, 2, 0);
        return layout;
    }

    private StackPanel BuildDetail(UiTheme theme)
    {
        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 28f,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        _hero = new MissionHero(theme, _reveal);
        column.AddChild(_hero);

        // The conditions sit in one strip of glass beside PLAY: how the mission is played, chosen right
        // where it is started. Seeded from the context rather than a fresh default, so a player three
        // missions into a night run is shown the run they are on.
        var strip = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(18, 20, 22, 184),
            TintBottom = Bgra32.FromRgba(10, 12, 13, 184),
            Blur = 20f,
            BorderEdges = UiEdges.Top | UiEdges.Left | UiEdges.Bottom,
        };
        var cells = new FlexPanel { Orientation = Orientation.Horizontal };
        _difficulty = new OptionCycler(theme, "Difficulty", Difficulties, DefaultDifficultyIndex());
        var time = new OptionCycler(theme, "Time", TimesOfDay, (int)Context.Conditions.TimeOfDay);
        time.SelectionChanged += index => ChooseConditions(Context.Conditions with { TimeOfDay = (MissionTimeOfDay)index });
        var weather = new OptionCycler(theme, "Weather", Weathers, (int)Context.Conditions.Weather);
        weather.SelectionChanged += index => ChooseConditions(Context.Conditions with { Weather = (MissionWeather)index });
        cells.Add(_difficulty);
        cells.Add(time);
        cells.Add(weather);
        strip.AddChild(cells);

        _play = new PrimaryAction(theme, "Play") { Width = 380f };
        _play.Activated += _ =>
        {
            MissionInfo mission = _missions[SelectedMission];
            Context.Host.StartMission(mission.Id, _difficulty.SelectedIndex);
        };

        var actions = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 96f,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0f, 28f, 0f, 0f),
        };
        actions.Add(strip);
        actions.Add(_play);
        column.AddChild(actions);
        return column;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _missions.Count || index == _selected)
        {
            return;
        }

        int direction = index > _selected ? 1 : -1;
        _selected = index;
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].Selected = i == index;
        }

        // The briefing changes in the middle of the reveal, once the old name has wiped away.
        _reveal.Start(direction, () =>
        {
            _shown = _selected;
            Refresh();
        });

        Refresh();
        Preview();
    }

    // Reached from the pause menu the campaign is a mission list and nothing else: the level behind it is
    // the one being played, and streaming the hovered mission over it would tear down the running mission.
    private void Preview()
    {
        if (Context.InGame || _missions.Count == 0)
        {
            return;
        }

        Context.Host.PreviewMission(_missions[SelectedMission].Id);
    }

    // The campaigns the list can show, the game's own (null) first, in the order their missions come.
    private List<string?> Campaigns => [.. Context.Missions.Select(static mission => mission.Campaign).Distinct()];

    private bool IsGameCampaign => Campaigns is var campaigns && (campaigns.Count == 0 || campaigns[_campaign] is null);

    private static string CampaignName(string? campaign) => campaign ?? "The game";

    // How far the profile is along the game's campaign; a mod's campaign keeps no progress, and starts at its first.
    private int Frontier()
    {
        if (!IsGameCampaign)
        {
            return 0;
        }

        int activeMission = Context.Config.Active?.ActiveMission ?? MissionRegistry.FirstMissionId;
        for (int i = 0; i < _missions.Count; i++)
        {
            if (_missions[i].Id == activeMission)
            {
                return i;
            }
        }

        return 0;
    }

    private void Refresh()
    {
        if (_missions.Count == 0)
        {
            return;
        }

        int frontier = Frontier();

        // The unlock-all cheat widens what may be played, not what has been done: the profile's own
        // progress still decides which missions read as completed. A mod's campaign is open throughout.
        int playable = PlayerCheatOptions.Current.UnlockAllLevels || !IsGameCampaign ? _missions.Count - 1 : frontier;

        for (int i = 0; i < _cards.Count && i < _missions.Count; i++)
        {
            _cards[i].Tag = StatusOf(i, frontier, playable);
            _cards[i].TagColor = i < frontier ? Context.Theme.TextMuted
                : i <= playable ? Context.Theme.Accent : Context.Theme.TextDisabled;
            _cards[i].IsEnabled = i <= playable;
        }

        _header?.Update(frontier);

        int shown = Math.Clamp(_shown, 0, _missions.Count - 1);
        MissionInfo mission = _missions[shown];
        _hero?.Show(
            string.Create(CultureInfo.InvariantCulture, $"MISSION {mission.Number:00} / {_missions.Count:00}"),
            StatusOf(shown, frontier, playable),
            mission.Name,
            mission.Description);

        if (_play is not null)
        {
            MissionInfo selected = _missions[SelectedMission];
            _play.IsEnabled = SelectedMission <= playable;
            _play.Subtitle = string.Create(CultureInfo.InvariantCulture, $"MISSION {selected.Number:00} · DEPLOY");
        }
    }

    // Any of the three can change what is playable while the list is showing -- from Extras or the console.
    private void OnUnlockCheatChanged(object? sender, CVarChangedEventArgs args) => Refresh();

    private static string StatusOf(int missionIndex, int frontierIndex, int playableIndex) =>
        missionIndex < frontierIndex ? "COMPLETED"
            : missionIndex <= playableIndex ? "AVAILABLE" : "LOCKED";

    // Told as it is chosen rather than held until Play: the app puts the chosen hour on the level it is
    // previewing behind this very menu, so the player sees the night they picked before they commit to it.
    private void ChooseConditions(MissionConditions conditions)
    {
        Context.Conditions = conditions;
        Context.Host.SetMissionConditions(conditions);
    }

    private int DefaultDifficultyIndex() => Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);

    /// <summary>The list's heading: OPERATIONS over the campaign's name, with how much of it is done and a segment per mission.</summary>
    private sealed class CampaignHeader(UiTheme theme, int total, string title) : UiElement
    {
        private string _count = string.Empty;
        private int _done;

        public void Update(int completed)
        {
            _done = Math.Clamp(completed, 0, total);
            _count = string.Create(CultureInfo.InvariantCulture, $"{_done} / {total} COMPLETE");
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Mono.LineHeight(context.Px(12f)) + context.Px(6f + 44f + 20f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            KitDraw.Eyebrow(painter, theme, "OPERATIONS", Bounds.TopLeft);
            float titleSize = context.Px(44f);
            var titleBox = new UiRect(Bounds.X, Bounds.Bottom - context.Px(20f) - titleSize, Bounds.Width, titleSize);
            painter.DrawLabel(theme.Display, title, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Display, titleSize, titleBox)), titleSize, theme.Text, 0.06f);

            // Right-aligned: the count over the segments, the segments sitting on the title's baseline.
            float segments = KitDraw.SegmentsWidth(context, total, total > 10 ? 14f : 24f, 4f);
            float right = Bounds.Right;
            float segmentsTop = titleBox.Bottom - context.Px(10f);
            KitDraw.Segments(painter, new UiVec2(right - segments, segmentsTop), _done, total, total > 10 ? 14f : 24f, 4f, 4f, theme.Accent);
            float countSize = context.Px(13f);
            float countWidth = theme.Mono.MeasureWidth(_count, countSize);
            painter.DrawText(theme.Mono, _count, new UiVec2(right - countWidth, segmentsTop - context.Px(8f) - theme.Mono.LineHeight(countSize)), countSize, theme.TextSecondary);

            painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.1f));
        }
    }

    /// <summary>The selected mission set large: its number and status, its name, and its briefing, revealed together.</summary>
    private sealed class MissionHero(UiTheme theme, TitleReveal reveal) : UiElement
    {
        private const float TitleUnits = 168f;
        private const float DescriptionUnits = 22f;
        private const float DescriptionWidthUnits = 640f;

        private string _meta = string.Empty;
        private string _status = string.Empty;
        private string _title = string.Empty;
        private string _description = string.Empty;
        private string _wrapped = string.Empty;
        private float _wrappedAt = -1f;

        public void Show(string meta, string status, string title, string description)
        {
            _meta = meta;
            _status = status;
            _title = title.ToUpperInvariant();
            _description = description ?? string.Empty;
            _wrappedAt = -1f;
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
        {
            float height = theme.Mono.LineHeight(context.Px(13f)) + context.Px(28f) + (context.Px(TitleUnits) * 0.85f);
            string wrapped = Wrapped(context, available.X);
            if (wrapped.Length > 0)
            {
                height += context.Px(28f) + theme.Body.Measure(wrapped, context.Px(DescriptionUnits)).Y;
            }

            return new UiVec2(available.X, height);
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float y = Bounds.Y;

            // The caption line: MISSION 01 / 14, a rule, the status.
            float metaSize = context.Px(13f);
            painter.PushOpacity(reveal.MetaOpacity);
            float metaX = Bounds.X + context.Px(reveal.MetaOffset);
            UiVec2 pen = painter.DrawLabel(theme.Mono, _meta, new UiVec2(metaX, y), metaSize, theme.Accent, 0.28f);
            float ruleX = pen.X + context.Px(16f);
            painter.FillRect(new UiRect(ruleX, y + (theme.Mono.LineHeight(metaSize) * 0.5f), context.Px(48f), context.Px(1f)), UiPalette.AccentAt(0.6f));
            painter.DrawLabel(theme.Mono, _status, new UiVec2(ruleX + context.Px(64f), y), metaSize, theme.TextSecondary, 0.28f);
            painter.PopOpacity();
            y += theme.Mono.LineHeight(metaSize) + context.Px(28f);

            // The name, as large as will fit: 168 units, smaller for a name too long for the column.
            float size = context.Px(TitleUnits);
            float natural = UiPainter.MeasureLabel(theme.Display, _title, size, reveal.LetterSpacing);
            if (natural > Bounds.Width && natural > 0f)
            {
                size *= Bounds.Width / natural;
            }

            var titleBox = new UiRect(Bounds.X, y, Bounds.Width, context.Px(TitleUnits) * 0.85f);
            var at = new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Display, size, titleBox) + context.Px(reveal.TitleOffset));
            float clipLeft = Bounds.X + (Bounds.Width * reveal.ClipLeft);
            float clipRight = Bounds.Right - (Bounds.Width * reveal.ClipRight);
            if (clipRight > clipLeft && reveal.TitleOpacity > 0.002f)
            {
                painter.PushClip(UiRect.FromEdges(clipLeft, titleBox.Y - context.Px(60f), clipRight, titleBox.Bottom + context.Px(60f)));
                painter.PushOpacity(reveal.TitleOpacity);
                KitDraw.BlurredText(painter, theme.Display, _title, at + new UiVec2(0f, context.Px(4f)), size, UiPalette.InkAt(0.32f), reveal.LetterSpacing, context.Px(14f));
                KitDraw.BlurredText(painter, theme.Display, _title, at, size, theme.Text, reveal.LetterSpacing, context.Px(reveal.TitleBlur));
                painter.PopOpacity();
                painter.PopClip();
            }

            y = titleBox.Bottom + context.Px(28f);

            string wrapped = Wrapped(context, Bounds.Width);
            if (wrapped.Length > 0)
            {
                painter.PushOpacity(reveal.DescriptionOpacity);
                painter.DrawText(theme.Body, wrapped, new UiVec2(Bounds.X + context.Px(reveal.DescriptionOffset), y), context.Px(DescriptionUnits), UiPalette.TextBody);
                painter.PopOpacity();
            }
        }

        private string Wrapped(LayoutContext context, float available)
        {
            float width = MathF.Min(available, context.Px(DescriptionWidthUnits));
            if (_wrappedAt != width)
            {
                _wrappedAt = width;
                _wrapped = _description.Length == 0 ? string.Empty : theme.Body.WrapText(_description, context.Px(DescriptionUnits), width);
            }

            return _wrapped;
        }
    }
}

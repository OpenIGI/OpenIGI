using System.Globalization;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The campaign section: a narrow mission rail down the left, and the selected mission's briefing floating
/// over the level itself, which is previewed live behind the menu.
/// </summary>
/// <remarks>
/// <para>
/// <b>The level is the picture.</b> The whole reason the shell streams the hovered mission in behind the
/// menu is so the player sees where they are about to go, so the section is laid out around keeping it
/// visible: one translucent rail holding the list, and the briefing set directly on the scene as
/// shadowed text with no panel behind it. Filling the screen with two opaque cards — which is what a
/// conventional two-pane dossier does — hides the one thing worth showing.
/// </para>
/// <para>
/// A rail rather than a carousel. A carousel shows one title at a time and hides where you are in a
/// fourteen-mission campaign; the rail shows the whole campaign at once, in the order the game itself
/// chains the missions, with each one's progress beside it — and it is what a pointer expects to be able
/// to click and scroll. Each row is its own focusable <see cref="ListCard"/>, so the mouse, the keyboard
/// and the pad all reach it through the one focus model.
/// </para>
/// <para>
/// Selection follows focus: moving to a row is choosing it, which is what makes a single press of down
/// change the briefing and the level behind it. That preview goes through
/// <see cref="IShellHost.PreviewMission"/>, which the app debounces and cross-fades — scrubbing the list
/// does not load fourteen levels.
/// </para>
/// </remarks>
public sealed class CampaignScreen : ShellScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;

    // Indexed by MissionTimeOfDay and MissionWeather, whose Default is deliberately zero -- so a row's
    // selected index IS the enumeration value and no lookup table can fall out of step with it.
    private static readonly string[] TimesOfDay = ["Default", "Day", "Night"];

    private static readonly string[] Weathers = ["Default", "Rain", "Snow"];

    private readonly List<ListCard> _cards = [];

    private Label? _missionNumber;
    private Label? _missionName;
    private Label? _status;
    private Label? _briefing;
    private Button? _play;
    private int _selected;

    /// <summary>Initializes a new instance of the <see cref="CampaignScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public CampaignScreen(ShellContext context)
        : base(context)
    {
    }

    /// <summary>Gets the index of the mission the rail currently has selected.</summary>
    public int SelectedMission => _selected;

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Campaign;

    /// <inheritdoc/>
    /// <remarks>The level behind the menu is the picture here; a backdrop over it would be covering the point.</remarks>
    protected override bool ContentOnBackdrop => false;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        _cards.Clear();

        if (Context.Missions.Count == 0)
        {
            return new Label(theme.Body, "No missions were found in this installation.", theme.BodyTextSize, theme.TextMuted)
            {
                Shadow = theme.Shadow,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        _selected = Math.Clamp(_selected, 0, Context.Missions.Count - 1);

        var columns = new Grid { ColumnSpacing = 44f };
        columns.ColumnDefinitions.Add(GridLength.Stars(0.31f));
        columns.ColumnDefinitions.Add(GridLength.Stars(0.69f));
        columns.RowDefinitions.Add(GridLength.Star);

        columns.Add(BuildMissionRail(theme), 0, 0);
        columns.Add(BuildBriefing(theme), 0, 1);

        Refresh();
        return columns;
    }

    /// <inheritdoc/>
    protected override void OnBuilt()
    {
        // Preview the current selection once the rail exists; OnEnter runs before the tree is built.
        Preview();
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        // Selection follows focus, so one press of down changes the briefing and the level behind it. The
        // rows are focusable in their own right, which means this covers the pointer, the keyboard and the
        // pad without any of them needing their own path.
        if (Focus?.Focused is ListCard focused)
        {
            int index = _cards.IndexOf(focused);
            if (index >= 0 && index != _selected)
            {
                Select(index);
            }
        }
    }

    private Panel BuildMissionRail(UiTheme theme)
    {
        // Translucent, so the level reads through the rail rather than being walled off by it.
        var panel = new Panel { Background = theme.Panel.WithAlpha(150) };

        var layout = new Grid { Padding = new Thickness(0f, 20f, 0f, 16f), RowSpacing = 12f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1f,
            Padding = new Thickness(20f, 0f),
        };
        header.AddChild(new Label(theme.Heading, "CAMPAIGN", theme.HeadingTextSize * 0.55f, theme.Text)
        {
            Shadow = theme.Shadow,
        });
        header.AddChild(new Label(
            theme.Body,
            string.Create(CultureInfo.InvariantCulture, $"{Context.Missions.Count} missions"),
            theme.BodyTextSize * 0.78f,
            theme.TextMuted));
        layout.Add(header, 0, 0);

        var scroll = new ScrollView(theme);
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1f };
        for (int i = 0; i < Context.Missions.Count; i++)
        {
            int index = i;
            MissionInfo mission = Context.Missions[i];
            var card = new ListCard(theme, mission.Number.ToString("00", CultureInfo.InvariantCulture), mission.Name)
            {
                Selected = i == _selected,
            };
            card.Activated += _ => Select(index);
            _cards.Add(card);
            rows.AddChild(card);
        }

        scroll.SetContent(rows);
        layout.Add(scroll, 1, 0);

        panel.AddChild(layout);
        return panel;
    }

    private Grid BuildBriefing(UiTheme theme)
    {
        // No panel: the briefing sits on the level, and its legibility comes from shadowed text rather than
        // from a block laid over the picture. The upper part of the column is deliberately left empty.
        var column = new Grid { RowSpacing = 10f };
        column.ColumnDefinitions.Add(GridLength.Star);
        column.RowDefinitions.Add(GridLength.Star);  // the level shows through here
        column.RowDefinitions.Add(GridLength.Auto);  // the mission number
        column.RowDefinitions.Add(GridLength.Auto);  // the mission name
        column.RowDefinitions.Add(GridLength.Auto);  // the accent rule
        column.RowDefinitions.Add(GridLength.Auto);  // the progress line
        column.RowDefinitions.Add(GridLength.Auto);  // the briefing text
        column.RowDefinitions.Add(GridLength.Auto);  // difficulty and Play

        _missionNumber = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.8f, theme.Accent)
        {
            Shadow = theme.Shadow,
        };
        column.Add(_missionNumber, 1, 0);

        _missionName = new Label(theme.Heading, string.Empty, theme.HeadingTextSize * 1.25f, theme.Text)
        {
            Shadow = theme.Shadow,
        };
        column.Add(_missionName, 2, 0);

        column.Add(
            new Panel
            {
                Background = theme.Accent,
                Height = 3f,
                Width = 110f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0f, 4f, 0f, 4f),
            },
            3,
            0);

        _status = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.85f, theme.TextMuted)
        {
            Shadow = theme.Shadow,
        };
        column.Add(_status, 4, 0);

        _briefing = new Label(theme.Body, string.Empty, theme.BodyTextSize, theme.Text)
        {
            Wrap = true,
            Shadow = theme.Shadow,
            MaxWidth = 720f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0f, 6f, 0f, 14f),
        };
        column.Add(_briefing, 5, 0);

        // A tighter gap than the row had, because the row now holds three label-and-dropdown pairs rather
        // than one: at an even eighteen everywhere, "WEATHER" sat as far from its own dropdown as from the
        // one before it, and the three pairs read as six unrelated controls. The gap is the WITHIN-pair
        // spacing; each pair after the first is pushed off the one before it by the label's own margin.
        var actions = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 10f,
            Justify = FlexJustify.Start,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        actions.Add(new Label(theme.Body, "DIFFICULTY", theme.BodyTextSize * 0.78f, theme.TextMuted)
        {
            CenterTextVertically = true,
            Shadow = theme.Shadow,
        });

        var difficulty = new Dropdown(theme, "Difficulty", Difficulties, DefaultDifficultyIndex())
        {
            Style = DropdownStyle.Compact,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Add(difficulty);

        // The hour and the weather sit beside the difficulty because they are the same kind of choice:
        // how the campaign is played rather than how it is drawn. Both are seeded from the context and
        // not from a fresh default, so a player three missions into a night run is shown the run they are
        // on -- this panel is rebuilt every time the section is opened.
        actions.Add(new Label(theme.Body, "TIME", theme.BodyTextSize * 0.78f, theme.TextMuted)
        {
            CenterTextVertically = true,
            Shadow = theme.Shadow,
            Margin = new Thickness(20f, 0f, 0f, 0f),
        });

        var timeOfDay = new Dropdown(theme, "Time of day", TimesOfDay, (int)Context.Conditions.TimeOfDay)
        {
            Style = DropdownStyle.Compact,
            VerticalAlignment = VerticalAlignment.Center,
        };
        timeOfDay.SelectionChanged += index => ChooseConditions(
            Context.Conditions with { TimeOfDay = (MissionTimeOfDay)index });
        actions.Add(timeOfDay);

        actions.Add(new Label(theme.Body, "WEATHER", theme.BodyTextSize * 0.78f, theme.TextMuted)
        {
            CenterTextVertically = true,
            Shadow = theme.Shadow,
            Margin = new Thickness(20f, 0f, 0f, 0f),
        });

        var weather = new Dropdown(theme, "Weather", Weathers, (int)Context.Conditions.Weather)
        {
            Style = DropdownStyle.Compact,
            VerticalAlignment = VerticalAlignment.Center,
        };
        weather.SelectionChanged += index => ChooseConditions(
            Context.Conditions with { Weather = (MissionWeather)index });
        actions.Add(weather);

        _play = new Button(theme, "PLAY")
        {
            TextSize = theme.BodyTextSize * 1.2f,
            Padding = new Thickness(56f, 14f),
            Margin = new Thickness(14f, 0f, 0f, 0f),
            Accent = ButtonAccent.Solid,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _play.Activated += _ =>
        {
            MissionInfo mission = Context.Missions[_selected];
            Context.Host.StartMission(mission.Id, difficulty.SelectedIndex);
        };
        actions.Add(_play);

        column.Add(actions, 6, 0);
        return column;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= Context.Missions.Count)
        {
            return;
        }

        _selected = index;
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].Selected = i == index;
        }

        Refresh();
        Preview();
    }

    // Reached from the pause menu the campaign is a mission list and nothing else: the level behind it is
    // the one being played, and streaming the hovered mission over it would tear down the running mission's
    // scene. The briefing still follows the selection, which is the part that was worth having here.
    private void Preview()
    {
        if (Context.InGame || Context.Missions.Count == 0)
        {
            return;
        }

        Context.Host.PreviewMission(Context.Missions[_selected].Id);
    }

    private void Refresh()
    {
        if (_selected < 0 || _selected >= Context.Missions.Count)
        {
            return;
        }

        MissionInfo mission = Context.Missions[_selected];
        int activeMission = Context.Config.Active?.ActiveMission ?? MissionRegistry.FirstMissionId;
        int frontier = -1;
        for (int i = 0; i < Context.Missions.Count; i++)
        {
            if (Context.Missions[i].Id == activeMission)
            {
                frontier = i;
            }
        }
        if (frontier < 0)
        {
            frontier = 0;
        }

        if (_missionNumber is not null)
        {
            _missionNumber.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"MISSION {mission.Number:00}  /  {Context.Missions.Count:00}");
        }

        if (_missionName is not null)
        {
            _missionName.Text = mission.Name;
        }

        if (_status is not null)
        {
            _status.Text = StatusOf(_selected, frontier);
        }

        if (_briefing is not null)
        {
            _briefing.Text = mission.Description;
        }

        for (int i = 0; i < _cards.Count && i < Context.Missions.Count; i++)
        {
            _cards[i].Tag = StatusOf(i, frontier);
            _cards[i].TagColor = i < frontier ? Context.Theme.Accent
                : i == frontier ? Context.Theme.TextMuted : Context.Theme.TextDisabled;
            _cards[i].IsEnabled = i <= frontier;
        }

        if (_play is not null)
        {
            _play.IsEnabled = _selected <= frontier;
        }
    }

    private static string StatusOf(int missionIndex, int frontierIndex) =>
        missionIndex < frontierIndex ? "COMPLETED"
            : missionIndex == frontierIndex ? "AVAILABLE" : "LOCKED";

    // Told as it is chosen rather than held until Play: the app puts the chosen hour on the level it is
    // previewing behind this very menu, so the player sees the night they picked before they commit to it.
    private void ChooseConditions(MissionConditions conditions)
    {
        Context.Conditions = conditions;
        Context.Host.SetMissionConditions(conditions);
    }

    private int DefaultDifficultyIndex() => Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
}

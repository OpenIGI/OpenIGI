using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The campaign: a carousel of the real missions, a briefing that follows the selection, a difficulty
/// picker and a Play button. Changing the selection previews that mission's level behind the menu.
/// </summary>
/// <remarks>
/// The mission list is the port's own <c>DefineMission</c> data, already resolved to display strings by
/// the composition root. As the carousel moves it calls <see cref="IShellHost.PreviewMission"/>, which is
/// where the app streams the hovered level in on a worker thread and moves the showcase camera to it —
/// the screen itself only says which mission is under the cursor.
/// </remarks>
public sealed class CampaignScreen : Screen
{
    private static readonly string[] Difficulties = ["Easy", "Normal", "Hard"];

    private readonly ShellContext _context;
    private Carousel? _carousel;
    private Label? _briefing;
    private Label? _progress;

    /// <summary>Initializes a new instance of the <see cref="CampaignScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public CampaignScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Name = "Campaign";
    }

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        UiTheme theme = _context.Theme;
        var canvas = new Canvas();
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 18f };

        column.AddChild(new Label(theme.Heading, "CAMPAIGN", theme.HeadingTextSize, theme.Accent) { Shadow = theme.Shadow });

        if (_context.Missions.Count == 0)
        {
            column.AddChild(new Label(theme.Body, "No missions were found in this installation.", theme.BodyTextSize, theme.TextMuted));
            canvas.Add(column, Anchor.Filled(64f));
            return canvas;
        }

        string[] names = new string[_context.Missions.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = $"{_context.Missions[i].Number:00}  {_context.Missions[i].Name}";
        }

        _carousel = new Carousel(theme, names) { Margin = new Thickness(0f, 12f, 0f, 12f) };
        _carousel.SelectionChanged += OnMissionSelected;
        column.AddChild(_carousel);

        _progress = new Label(theme.Body, string.Empty, theme.BodyTextSize, theme.Accent)
        {
            TextAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        column.AddChild(_progress);

        _briefing = new Label(theme.Body, string.Empty, theme.BodyTextSize, theme.TextMuted)
        {
            TextAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0f, 0f, 0f, 24f),
        };
        column.AddChild(_briefing);

        var actions = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 24f,
            Justify = FlexJustify.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var difficulty = new Dropdown(theme, "Difficulty", Difficulties, DefaultDifficultyIndex());
        var play = new Button(theme, "Play") { TextSize = theme.BodyTextSize * 1.2f };
        play.Activated += _ =>
        {
            MissionInfo mission = _context.Missions[_carousel!.SelectedIndex];
            _context.Host.StartMission(mission.Id, difficulty.SelectedIndex);
        };
        actions.Add(difficulty);
        actions.Add(play);
        column.AddChild(actions);

        canvas.Add(column, Anchor.Filled(64f));
        Refresh(0);
        return canvas;
    }

    /// <inheritdoc/>
    protected override void OnBuilt()
    {
        // Preview the current selection once the carousel exists; OnEnter runs before the tree is built.
        if (_context.Missions.Count > 0 && _carousel is not null)
        {
            _context.Host.PreviewMission(_context.Missions[_carousel.SelectedIndex].Id);
        }
    }

    private void OnMissionSelected(int index)
    {
        Refresh(index);
        if (index >= 0 && index < _context.Missions.Count)
        {
            _context.Host.PreviewMission(_context.Missions[index].Id);
        }
    }

    private void Refresh(int index)
    {
        if (index < 0 || index >= _context.Missions.Count)
        {
            return;
        }

        MissionInfo mission = _context.Missions[index];
        if (_briefing is not null)
        {
            _briefing.Text = mission.Description;
        }

        if (_progress is not null)
        {
            int furthest = _context.Config.Active?.ActiveMission ?? 0;
            _progress.Text = mission.Id <= furthest ? "Completed" : mission.Number == 1 || mission.Id <= furthest + 1 ? "Available" : "Locked";
        }
    }

    private int DefaultDifficultyIndex() => Math.Clamp(_context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
}

using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The campaign section: a carousel of the real missions, a briefing and progress that follow the
/// selection, a difficulty picker and Play. Changing the selection previews that mission behind the menu.
/// </summary>
/// <remarks>
/// The mission list is the port's own <c>DefineMission</c> data, already resolved to display strings by
/// the composition root. As the carousel moves it calls <see cref="IShellHost.PreviewMission"/>, which is
/// where the app streams the hovered level in and moves the showcase camera to it.
/// </remarks>
public sealed class CampaignScreen : ShellScreen
{
    private static readonly string[] Difficulties = ["Easy", "Normal", "Hard"];

    private Carousel? _carousel;
    private Label? _briefing;
    private Label? _progress;

    /// <summary>Initializes a new instance of the <see cref="CampaignScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public CampaignScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Campaign;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };

        if (Context.Missions.Count == 0)
        {
            column.AddChild(new Label(theme.Body, "No missions were found in this installation.", theme.BodyTextSize, theme.TextMuted));
            return column;
        }

        string[] names = new string[Context.Missions.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = $"{Context.Missions[i].Number:00}  {Context.Missions[i].Name}";
        }

        _carousel = new Carousel(theme, names) { Margin = new Thickness(0f, 24f, 0f, 12f) };
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
        var difficulty = new Dropdown(theme, "Difficulty", Difficulties, DefaultDifficultyIndex()) { Width = 320f };
        var play = new Button(theme, "Play") { TextSize = theme.BodyTextSize * 1.2f };
        play.Activated += _ =>
        {
            MissionInfo mission = Context.Missions[_carousel!.SelectedIndex];
            Context.Host.StartMission(mission.Id, difficulty.SelectedIndex);
        };
        actions.Add(difficulty);
        actions.Add(play);
        column.AddChild(actions);

        Refresh(0);
        return column;
    }

    /// <inheritdoc/>
    protected override void OnBuilt()
    {
        // Preview the current selection once the carousel exists; OnEnter runs before the tree is built.
        if (Context.Missions.Count > 0 && _carousel is not null)
        {
            Context.Host.PreviewMission(Context.Missions[_carousel.SelectedIndex].Id);
        }
    }

    private void OnMissionSelected(int index)
    {
        Refresh(index);
        if (index >= 0 && index < Context.Missions.Count)
        {
            Context.Host.PreviewMission(Context.Missions[index].Id);
        }
    }

    private void Refresh(int index)
    {
        if (index < 0 || index >= Context.Missions.Count)
        {
            return;
        }

        MissionInfo mission = Context.Missions[index];
        if (_briefing is not null)
        {
            _briefing.Text = mission.Description;
        }

        if (_progress is not null)
        {
            int furthest = Context.Config.Active?.ActiveMission ?? 0;
            _progress.Text = mission.Id <= furthest ? "Completed"
                : mission.Number == 1 || mission.Id <= furthest + 1 ? "Available"
                : "Locked";
        }
    }

    private int DefaultDifficultyIndex() => Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
}

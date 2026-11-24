using System.Globalization;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The player's replays of one mission, opened from the campaign page: each run kept -- when it was played, at what difficulty,
/// under which rules, how it ended and how long it took, the fastest at its difficulty and rules marked -- and watched from here.
/// </summary>
public sealed class ReplaysScreen : ShellScreen
{
    private readonly IShellReplays _replays;
    private readonly MissionInfo _mission;
    private Label? _message;

    /// <summary>Initializes a new instance of the <see cref="ReplaysScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="replays">The player's replays.</param>
    /// <param name="mission">The mission whose replays it lists.</param>
    public ReplaysScreen(ShellContext context, IShellReplays replays, MissionInfo mission)
        : base(context)
    {
        _replays = replays ?? throw new ArgumentNullException(nameof(replays));
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Campaign;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("Enter", "Watch"), new("Esc", "Back")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        bool compact = Size == ShellSize.Compact;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 8f : 14f };
        column.AddChild(new SectionTitle(theme, string.Create(CultureInfo.InvariantCulture, $"Mission {_mission.Number}"), "Replays")
        {
            TitleSize = compact ? 32f : 88f,
            EyebrowColor = theme.Accent,
            Margin = new Thickness(0f, 0f, 0f, compact ? 6f : 16f),
        });
        column.AddChild(new Label(theme.Body, _mission.Name, compact ? 15f : 20f, theme.TextSecondary));

        IReadOnlyList<ShellReplay> replays = _replays.KeptOf(_mission.Id);
        foreach (ShellReplay replay in replays)
        {
            string id = replay.Id;
            var row = new ActionRow(theme, Caption(replay), "Watch")
            {
                Value = replay.RecordedAt.ToLocalTime().DateTime.ToString(compact ? "d MMM" : "d MMM yyyy  HH:mm", CultureInfo.InvariantCulture),
                FieldWidth = compact ? 84f : 220f,
            };
            row.Activated += _ => Watch(id);
            column.AddChild(row);
        }

        _message = new Label(theme.Body, string.Empty, compact ? 14f : 17f, theme.Accent);
        column.AddChild(_message);
        if (replays.Count == 0)
        {
            _message.Text = "No runs of this mission are kept yet. Every mission played is recorded from its start.";
        }

        if (compact)
        {
            return ScrollPage(column);
        }

        column.Width = 1040f;
        column.Margin = new Thickness(SideMargin, 40f, SideMargin, 24f);
        var scroll = new ScrollView(theme);
        scroll.SetContent(column);
        return scroll;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);

        // Back is to the campaign page this was opened from, not the home page.
        if (nav.Back)
        {
            Context.Stack.Pop();
            return false;
        }

        return base.HandleInput(nav, input);
    }

    // A run as its row says it, in the row's sentence: how it ended in how long, how hard and by which rules; the best marked, and
    // one another build recorded said so. When it was played is the row's field.
    private static string Caption(ShellReplay replay)
    {
        int seconds = (int)Math.Max(0.0, replay.Seconds);
        string best = replay.Best ? "Best run: " : string.Empty;
        string note = replay.Note.Length > 0 ? $", {replay.Note.ToLowerInvariant()}" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{best}{replay.Outcome} in {seconds / 60}:{seconds % 60:00} ({replay.Difficulty}, {replay.Rules}{note})");
    }

    private void Watch(string id)
    {
        if (_replays.Watch(id) is { } problem && _message is not null)
        {
            _message.Text = $"It cannot be watched: {problem}.";
        }
    }
}

using System.Globalization;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The running mission's own save games, opened from the pause menu for a title that keeps them: the saves it has, as tiles, each
/// of which puts the mission back as it holds it (<c>Game_FillSaveGameScrollListBox</c>, I.G.I. 2's list of a mission's saves).
/// Saves are made at the player's computer, not here.
/// </summary>
public sealed class MissionSavesScreen : ShellScreen
{
    private readonly ITitleSaveGames _saves;
    private Label? _message;
    private int _confirmed = -1;

    /// <summary>Initializes a new instance of the <see cref="MissionSavesScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="saves">The title's save games.</param>
    public MissionSavesScreen(ShellContext context, ITitleSaveGames saves)
        : base(context)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("ENTER", "Load"), new("ESC", "Back")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16f,
            Width = 880f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(SideMargin, 0f, 0f, 0f),
        };

        column.AddChild(new SectionTitle(theme, "Operation", "Load game")
        {
            TitleSize = Size == ShellSize.Compact ? 32f : 88f,
            EyebrowColor = theme.Accent,
            Margin = new Thickness(0f, 0f, 0f, 20f),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f };
        int saves = 0;
        foreach (TitleSaveSlot slot in _saves.Slots)
        {
            // Only the slots that hold a save of this mission, the quick save's after the computer's five.
            if (!slot.IsUsed)
            {
                continue;
            }

            int number = slot.Slot;
            string name = number >= QuickSaveSlot ? "Quick save" : string.Create(CultureInfo.InvariantCulture, $"Slot {number + 1:00}");
            var tile = new SlotTile(theme, name) { Width = Size == ShellSize.Compact ? 118f : 200f, Height = Size == ShellSize.Compact ? 96f : 132f, Content = "Mission time", Detail = slot.Description };
            tile.Activated += _ => Choose(number);
            row.AddChild(tile);
            saves++;
        }

        column.AddChild(row);
        _message = new Label(theme.Body, string.Empty, 17f, theme.Accent);
        column.AddChild(_message);
        if (saves == 0)
        {
            _message.Text = "This mission has no saves. Save from the map computer.";
        }

        return column;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // Back returns to the pause menu this page was opened from.
        if (base.HandleInput(nav, input))
        {
            Context.Stack.Reset(new PauseScreen(Context));
        }

        return false;
    }

    // The quick save's slot, the one past the computer's five.
    private const int QuickSaveSlot = 5;

    private void Choose(int slot)
    {
        // A save made with mods that change play, some of them off now, loads only once the player has been told and
        // chooses it again: the level it holds may need what they added.
        if (_saves.Slots.FirstOrDefault(s => s.Slot == slot).MissingMods is { Count: > 0 } missing && _confirmed != slot)
        {
            _confirmed = slot;
            SetMessage($"Saved with mods that are off: {string.Join(", ", missing)}. Choose it again to load it anyway.");
            return;
        }

        _confirmed = -1;
        if (_saves.Load(slot))
        {
            // A load that starts the mission afresh has the host bring it up, as a restart does; one read onto the level
            // that is up plays on from here.
            if (!_saves.LoadRestartsMission)
            {
                Context.Host.ResumeMission();
            }

            return;
        }

        SetMessage("Loading failed.");
    }

    private void SetMessage(string text)
    {
        if (_message is not null)
        {
            _message.Text = text;
        }
    }
}

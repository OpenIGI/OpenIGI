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
/// The running mission's own save games, opened from the pause menu for a title that keeps them: its slots as tiles, what each holds,
/// and how many saves the mission still allows. Saving writes the slot and stays; loading puts the mission back and resumes it.
/// </summary>
public sealed class MissionSavesScreen : ShellScreen
{
    private readonly ITitleSaveGames _saves;
    private readonly bool _isSaving;
    private readonly List<SlotTile> _tiles = [];
    private Label? _left;
    private Label? _message;

    /// <summary>Initializes a new instance of the <see cref="MissionSavesScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="saves">The title's save games.</param>
    /// <param name="isSaving">Whether a slot is saved to, rather than loaded from.</param>
    public MissionSavesScreen(ShellContext context, ITitleSaveGames saves, bool isSaving)
        : base(context)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _isSaving = isSaving;
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("ENTER", _isSaving ? "Save" : "Load"), new("ESC", "Back")];

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
            Margin = new Thickness(64f, 0f, 0f, 0f),
        };

        column.AddChild(new SectionTitle(theme, "Operation", _isSaving ? "Save game" : "Load game")
        {
            TitleSize = 88f,
            EyebrowColor = theme.Accent,
            Margin = new Thickness(0f, 0f, 0f, 20f),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f };
        _tiles.Clear();
        foreach (TitleSaveSlot slot in _saves.Slots)
        {
            int number = slot.Slot;
            var tile = new SlotTile(theme, string.Create(CultureInfo.InvariantCulture, $"Slot {number + 1:00}")) { Width = 200f, Height = 132f };
            tile.Activated += _ => Choose(number);
            _tiles.Add(tile);
            row.AddChild(tile);
        }

        column.AddChild(row);
        _left = new Label(theme.Body, string.Empty, 17f, UiPalette.TextBody);
        _message = new Label(theme.Body, string.Empty, 17f, theme.Accent);
        column.AddChild(_left);
        column.AddChild(_message);
        if (_tiles.Count == 0)
        {
            _message.Text = "This difficulty keeps no saves.";
        }

        Refresh();
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

    private void Choose(int slot)
    {
        if (_isSaving)
        {
            if (_saves.SavesLeft == 0)
            {
                SetMessage("No saves are left for this mission.");
                return;
            }

            SetMessage(_saves.Save(slot) ? "Saving complete." : "Saving failed.");
            Refresh();
            return;
        }

        if (_saves.Load(slot))
        {
            Context.Host.ResumeMission();
            return;
        }

        SetMessage("Loading failed.");
    }

    private void Refresh()
    {
        IReadOnlyList<TitleSaveSlot> slots = _saves.Slots;
        for (int i = 0; i < _tiles.Count && i < slots.Count; i++)
        {
            _tiles[i].Content = slots[i].IsUsed ? "Mission time" : null;
            _tiles[i].Detail = slots[i].Description;
        }

        if (_left is not null)
        {
            int left = _saves.SavesLeft;
            _left.Text = left < 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"Saves left this mission: {left}");
        }
    }

    private void SetMessage(string text)
    {
        if (_message is not null)
        {
            _message.Text = text;
        }
    }
}

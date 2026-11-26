using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The profile's saves: eight slots, four by two, and along the panel's foot the slot picked with DELETE SAVE, SAVE HERE
/// and LOAD.
/// </content>
public sealed partial class ProfilesScreen
{
    private const int SlotCount = 8;
    private const int SlotColumns = 4;

    private readonly List<SlotTile> _slots = [];
    private int _selectedSlot;
    private Label? _savesTitle;
    private Label? _slotNote;
    private Button? _saveButton;
    private Button? _loadButton;
    private Button? _deleteButton;
    private int _confirmedSlot = -1;

    private GlassPanel BuildSaves(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        GlassPanel panel = DetailPanel(theme);
        var layout = new Grid { RowSpacing = compact ? 10f : 16f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // how many are used
        layout.RowDefinitions.Add(GridLength.Auto); // the slots
        layout.RowDefinitions.Add(GridLength.Star); // what room is left
        layout.RowDefinitions.Add(GridLength.Auto); // the slot picked, and what can be done with it

        _savesTitle = new Label(theme.Heading, "SAVES", compact ? 13f : 14f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center };
        layout.Add(_savesTitle, 0, 0);

        // Eight slots, four by two, the way the design lays them out.
        float tile = Size switch { ShellSize.Wide => 152f, ShellSize.Medium => 128f, _ => 84f };
        var grid = new Grid { RowSpacing = compact ? 8f : 12f, ColumnSpacing = compact ? 8f : 12f, Id = "profile-saves" };
        grid.RowDefinitions.Add(GridLength.Absolute(tile));
        grid.RowDefinitions.Add(GridLength.Absolute(tile));
        for (int column = 0; column < SlotColumns; column++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);
        }

        _slots.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            int index = i;
            var slot = new SlotTile(theme, string.Create(CultureInfo.InvariantCulture, $"Slot {i + 1:00}")) { Selected = i == _selectedSlot };
            slot.Activated += _ => SelectSlot(index);
            _slots.Add(slot);
            grid.Add(slot, i / SlotColumns, i % SlotColumns);
        }

        layout.Add(grid, 1, 0);

        var save = new Button(theme, compact ? "Save" : "Save here")
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Save),
            GlyphSize = 16f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(compact ? 14f : 20f, 0f),
            Height = 44f,
        };
        save.Activated += _ => SaveToSelectedSlot();
        var load = new Button(theme, "Load")
        {
            Accent = ButtonAccent.Solid,
            CutCorner = 10f,
            Key = compact || Context.Host.TouchScreen ? string.Empty : "Enter",
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(compact ? 18f : 28f, 0f),
            Height = 44f,
        };
        load.Activated += _ => LoadSelectedSlot();
        var delete = new Button(theme, compact ? "Delete" : "Delete save")
        {
            Accent = ButtonAccent.Destructive,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Trash),
            GlyphSize = 16f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(compact ? 14f : 20f, 0f),
            Height = 44f,
        };
        delete.Activated += _ => DeleteSelectedSlot();

        // Along the panel's foot: which slot is picked and what is in it, then what can be done with it, LOAD last and loudest.
        var foot = new Grid { ColumnSpacing = 10f };
        foot.RowDefinitions.Add(GridLength.Auto);
        foot.RowDefinitions.Add(GridLength.Auto);
        foot.ColumnDefinitions.Add(GridLength.Star);
        foot.ColumnDefinitions.Add(GridLength.Auto);
        foot.ColumnDefinitions.Add(GridLength.Auto);
        foot.ColumnDefinitions.Add(GridLength.Auto);
        foot.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(0f, 0f, 0f, compact ? 10f : 16f) }, 0, 0, columnSpan: 4);
        _slotNote = new Label(theme.Body, string.Empty, compact ? 15f : 17f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center };
        foot.Add(_slotNote, 1, 0);
        foot.Add(delete, 1, 1);
        foot.Add(save, 1, 2);
        foot.Add(load, 1, 3);
        layout.Add(foot, 3, 0);
        (_saveButton, _loadButton, _deleteButton) = (save, load, delete);
        panel.AddChild(layout);
        return panel;
    }

    private void SelectSlot(int index)
    {
        // A second press on the selected slot loads it, which is what ENTER on a slot means in the footer.
        if (index == _selectedSlot && _slots[index].Content is not null)
        {
            LoadSelectedSlot();
            return;
        }

        _selectedSlot = index;
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].Selected = i == index;
        }

        RefreshSlots();
    }

    private void SaveToSelectedSlot()
    {
        // There is something to save only inside a mission: the menus' own game is no game.
        if (!Context.InGame || Context.Config.Active is not { } profile)
        {
            return;
        }

        int slot = _selectedSlot;
        int missionId = MissionReached(profile);
        Bgra32[] thumb = Context.Host.CaptureThumbnail(out int width, out int height);
        Context.Saves.Save(
            slot,
            profile.Name,
            missionId,
            MissionName(missionId),
            DateTimeOffset.Now,
            thumb,
            width,
            height,
            Context.Host.PlayedMods);
        RefreshSlots();
    }

    private void LoadSelectedSlot()
    {
        if (Context.Config.Active is not { } profile)
        {
            return;
        }

        if (Context.Saves.Find(profile.Name, _selectedSlot) is not { } save)
        {
            return;
        }

        // A slot saved with mods that change play, some of them off now, starts only once the player has been told and
        // chooses it again: its mission may be one of theirs.
        IReadOnlyList<OpenIGI.Core.Content.ModReference> missing = OpenIGI.Core.Content.ModReference.Missing(save.Mods, Context.Host.PlayedMods);
        if (missing.Count > 0 && _confirmedSlot != _selectedSlot)
        {
            _confirmedSlot = _selectedSlot;
            SetDetailsMessage($"Saved with mods that are off: {string.Join(", ", missing)}. Load it again to play it anyway.");
            return;
        }

        _confirmedSlot = -1;
        Context.Host.StartMission(save.MissionId, profile.Difficulty);
    }

    private void DeleteSelectedSlot()
    {
        if (Context.Config.Active is { } profile
            && Context.Saves.Delete(profile.Name, _selectedSlot))
        {
            RefreshSlots();
        }
    }

    private void RefreshSlots()
    {
        string? profileName = Context.Config.Active?.Name;
        for (int i = 0; i < _slots.Count; i++)
        {
            SaveSlot? slot = profileName is null ? null : Context.Saves.Find(profileName, i);
            _slots[i].Content = slot?.MissionName;
            _slots[i].Detail = slot is null ? string.Empty : slot.SavedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        }

        int used = _slots.Count(static slot => !slot.IsEmpty);
        if (_savesTitle is not null)
        {
            _savesTitle.Text = string.Create(CultureInfo.InvariantCulture, $"SAVES  ·  {used} OF {_slots.Count} USED");
        }

        if (_slotNote is not null && _selectedSlot >= 0 && _selectedSlot < _slots.Count)
        {
            SaveSlot? picked = profileName is null ? null : Context.Saves.Find(profileName, _selectedSlot);
            _slotNote.Text = picked is null
                ? string.Create(CultureInfo.InvariantCulture, $"Slot {_selectedSlot + 1:00} · Empty")
                : string.Create(CultureInfo.InvariantCulture, $"Slot {_selectedSlot + 1:00} · {picked.MissionName}");
            ShowSlotActions(profileName is not null, picked is not null);
        }
    }

    // What can be done with the slot picked: saved into only from inside a mission; loaded or deleted only when it holds a save.
    private void ShowSlotActions(bool hasProfile, bool holdsSave)
    {
        if (_saveButton is not null)
        {
            _saveButton.IsEnabled = hasProfile && Context.InGame;
            _saveButton.Tooltip = Context.InGame ? string.Empty : "Saving is done inside a mission: pause it and come here.";
        }

        if (_loadButton is not null)
        {
            _loadButton.IsEnabled = holdsSave;
        }

        if (_deleteButton is not null)
        {
            _deleteButton.IsEnabled = holdsSave;
        }
    }
}

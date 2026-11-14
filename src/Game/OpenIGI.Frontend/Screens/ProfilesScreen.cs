using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The player-profile workspace: a campaign-aware profile rail, a selected-profile dossier, and profile-scoped saves.
/// </summary>
public sealed class ProfilesScreen : ShellScreen
{
    private enum DetailMode
    {
        Profile,
        Create,
        Rename,
        ConfirmDelete,
    }

    private readonly List<ProfileCard> _cards = [];
    private DetailMode _mode;

    private const int SlotCount = 8;
    private const int SlotColumns = 4;

    private readonly List<SlotTile> _slots = [];
    private GlassPanel? _profileDetails;
    private GlassPanel? _createDetails;
    private GlassPanel? _deleteDetails;
    private GlassPanel? _renameDetails;
    private TextBox? _newName;
    private Label? _renameError;
    private Button? _renameProfile;
    private int _selectedSlot;
    private TextBox? _name;
    private Dropdown? _difficulty;
    private Toggle? _unlockAllMissions;
    private Label? _profileName;
    private Stat? _profileDifficulty;
    private Stat? _profileProgress;
    private Stat? _profileMission;
    private Label? _detailsMessage;
    private Label? _formError;
    private Label? _deletePrompt;
    private Button? _deleteProfile;
    private Label? _profileCount;
    private Label? _savesTitle;
    private Label? _slotNote;
    private Control? _pendingFocus;
    private int _confirmedSlot = -1;

    /// <summary>Initializes the screen.</summary>
    public ProfilesScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Profile;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("↑ ↓ ← →", "Move"), new("Enter", "Load"), new("Esc", "Back")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        // A phone has no room for the list and the workspace side by side: there the active profile comes first, the
        // list and its New profile under it, the saves last, in one column the finger drags.
        if (Size == ShellSize.Compact)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
            stack.AddChild(new SectionTitle(theme, string.Empty, "Profile") { TitleSize = 24f, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 4f, 0f, 0f) });
            stack.AddChild(BuildDetails(theme));
            stack.AddChild(BuildProfileRail(theme));
            stack.AddChild(BuildSaves(theme));
            Refresh();
            return ScrollPage(stack);
        }

        var columns = new Grid { ColumnSpacing = Size == ShellSize.Wide ? 48f : 32f, Padding = new Thickness(SideMargin, 32f, SideMargin, 24f) };
        columns.ColumnDefinitions.Add(GridLength.Absolute(Size == ShellSize.Wide ? 520f : 440f));
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Star);

        columns.Add(BuildProfileRail(theme), 0, 0);
        columns.Add(BuildWorkspace(theme), 0, 1);
        Refresh();
        return columns;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, OpenIGI.Input.InputState input)
    {
        // Escape backs out of an in-page workflow before leaving the whole profile workspace.
        if (nav.Back && _mode != DetailMode.Profile)
        {
            ShowProfileDetails();
            return false;
        }

        // ENTER in a name's box does what the form's own button does.
        if (Focus?.Focused is TextBox box && EnteredText(nav, input))
        {
            if (ReferenceEquals(box, _newName))
            {
                RenameProfile();
                return false;
            }

            if (ReferenceEquals(box, _name))
            {
                CreateProfile();
                return false;
            }
        }

        return base.HandleInput(nav, input);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_pendingFocus is { CanFocus: true } focusTarget)
        {
            Focus?.Focus(focusTarget);
            _pendingFocus = null;
        }
    }

    private UiElement BuildProfileRail(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        var cards = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        _cards.Clear();
        for (int i = 0; i < GameConfig.MaxProfiles; i++)
        {
            int index = i;
            var card = new ProfileCard(theme, string.Empty) { Visibility = Visibility.Collapsed, Id = $"profile-card-{i}" };
            card.Activated += _ => SelectProfile(index);
            _cards.Add(card);
            cards.AddChild(card);
        }

        var create = new Button(theme, "New profile")
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Plus, centre, size, ink),
            GlyphSize = 14f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Height = 44f,
            Tooltip = "Create a new player profile with its own campaign difficulty.",
        };
        create.Activated += _ => ShowCreateDetails();

        if (compact)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
            stack.AddChild(new Eyebrow(theme, "Profiles"));
            stack.AddChild(cards);
            stack.AddChild(create);
            return stack;
        }

        var layout = new Grid { RowSpacing = 16f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        // The page's name, and against the column's other edge how many operatives there are of how many there may be.
        var heading = new Grid();
        heading.RowDefinitions.Add(GridLength.Auto);
        heading.ColumnDefinitions.Add(GridLength.Star);
        heading.ColumnDefinitions.Add(GridLength.Auto);
        heading.Add(new SectionTitle(theme, "Operatives", "Profiles") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        _profileCount = new Label(theme.Mono, string.Empty, 14f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0f, 0f, 0f, 6f) };
        heading.Add(_profileCount, 0, 1);
        layout.Add(heading, 0, 0);
        layout.Add(new Label(theme.Body, "Each operative keeps its own difficulty, progress and saves.", 17f, theme.TextSecondary)
        {
            Wrap = true,
        }, 1, 0);

        // The operatives, and straight under the last of them the way to another.
        var list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        list.AddChild(cards);
        list.AddChild(create);
        var scroll = new ScrollView(theme);
        scroll.SetContent(list);
        layout.Add(scroll, 2, 0);
        return layout;
    }

    private Grid BuildWorkspace(UiTheme theme)
    {
        var workspace = new Grid { RowSpacing = 20f };
        workspace.ColumnDefinitions.Add(GridLength.Star);
        workspace.RowDefinitions.Add(GridLength.Auto);
        workspace.RowDefinitions.Add(GridLength.Star);
        workspace.Add(BuildDetails(theme), 0, 0);
        workspace.Add(BuildSaves(theme), 1, 0);
        return workspace;
    }

    // The active profile, or in its place the form that makes one or the question before one is deleted.
    private Panel BuildDetails(UiTheme theme)
    {
        var details = new Panel();
        _profileDetails = BuildProfileDetails(theme);
        _createDetails = BuildCreateDetails(theme);
        _deleteDetails = BuildDeleteDetails(theme);
        _renameDetails = BuildRenameDetails(theme);
        _createDetails.Visibility = Visibility.Collapsed;
        _deleteDetails.Visibility = Visibility.Collapsed;
        _renameDetails.Visibility = Visibility.Collapsed;
        details.AddChild(_profileDetails);
        details.AddChild(_createDetails);
        details.AddChild(_renameDetails);
        details.AddChild(_deleteDetails);
        return details;
    }

    private GlassPanel BuildProfileDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        bool compact = Size == ShellSize.Compact;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 6f : 10f };
        column.AddChild(new Eyebrow(theme, "Active profile") { Color = theme.Accent, TextSize = compact ? 12f : 14f });
        _profileName = new Label(theme.Display, string.Empty, Size switch { ShellSize.Wide => 84f, ShellSize.Medium => 72f, _ => 32f }, theme.Text) { VerticalAlignment = VerticalAlignment.Center };

        var stats = new FlexPanel { Orientation = Orientation.Horizontal, Gap = compact ? 20f : 36f, VerticalAlignment = VerticalAlignment.Center };
        float statSize = compact ? 18f : 24f;
        _profileDifficulty = new Stat(theme, "Difficulty") { ValueColor = theme.Accent, ValueSize = statSize };
        _profileProgress = new Stat(theme, "Campaign") { ValueSize = statSize };
        _profileMission = new Stat(theme, "Current mission") { ValueSize = statSize };
        stats.Add(_profileDifficulty);
        stats.Add(_profileProgress);
        stats.Add(_profileMission);

        _deleteProfile = new Button(theme, compact ? string.Empty : "Delete")
        {
            Accent = ButtonAccent.Destructive,
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Trash, centre, size, ink),
            GlyphSize = 16f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(compact ? 16f : 20f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Permanently delete this profile and every save slot belonging to it.",
        };
        _deleteProfile.Activated += _ => ShowDeleteDetails();

        _renameProfile = new Button(theme, compact ? string.Empty : "Rename")
        {
            Accent = ButtonAccent.Text,
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Pencil, centre, size, ink),
            GlyphSize = 16f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(compact ? 14f : 12f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Give this profile another name. Its progress, settings and saves stay with it.",
        };
        _renameProfile.Activated += _ => ShowRenameDetails();
        var manage = new StackPanel { Orientation = Orientation.Horizontal, Spacing = compact ? 4f : 12f, VerticalAlignment = VerticalAlignment.Center };
        manage.AddChild(_renameProfile);
        manage.AddChild(_deleteProfile);

        if (compact)
        {
            // A phone's width holds the name and two figures; the mission reached is on the operative's card below.
            _profileMission.Visibility = Visibility.Collapsed;
        }

        // The name large, its figures beside it, and DELETE against the panel's other edge.
        var row = new Grid { ColumnSpacing = compact ? 20f : 40f };
        row.RowDefinitions.Add(GridLength.Auto);
        row.ColumnDefinitions.Add(GridLength.Auto);
        row.ColumnDefinitions.Add(GridLength.Star);
        row.ColumnDefinitions.Add(GridLength.Auto);
        row.Add(_profileName, 0, 0);
        row.Add(stats, 0, 1);
        row.Add(manage, 0, 2);
        column.AddChild(row);

        _detailsMessage = new Label(theme.Body, string.Empty, 16f, theme.TextMuted) { Visibility = Visibility.Collapsed };
        column.AddChild(_detailsMessage);
        panel.AddChild(column);
        return panel;
    }

    private GlassPanel BuildCreateDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        column.AddChild(new SectionTitle(theme, "New operative", "Create profile") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 0f, 0f, 6f) });
        column.AddChild(new Label(theme.Body, "Campaign difficulty belongs to the profile and applies whenever it is active.", 16f, theme.TextMuted)
        {
            Wrap = true,
        });

        _name = new TextBox(theme)
        {
            Id = "profile-name",
            Placeholder = "Enter profile name",
            MaxLength = PlayerProfile.NameCapacity - 1,
            Tooltip = "Names must be unique and can be up to 31 characters.",
        };
        column.AddChild(_name);

        _difficulty = new Dropdown(theme, "Difficulty", AiDifficultyNames.All, 1)
        {
            Tooltip = "Easy, Normal, and Hard are saved with this profile.",
        };
        column.AddChild(_difficulty);

        _unlockAllMissions = new Toggle(theme, "Temporary: unlock all missions")
        {
            Tooltip = "Testing option: make every campaign mission available for this profile.",
        };
        column.AddChild(_unlockAllMissions);

        _formError = new Label(theme.Body, string.Empty, 16f, theme.Danger);
        column.AddChild(_formError);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f };
        var create = new Button(theme, "CREATE PROFILE") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        create.Activated += _ => CreateProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(create);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    // The active profile's name in a box to change it in, where the profile's own panel stands.
    private GlassPanel BuildRenameDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        column.AddChild(new SectionTitle(theme, "Operative", "Rename profile") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 0f, 0f, 6f) });
        column.AddChild(new Label(theme.Body, "The profile keeps its difficulty, progress, settings and saves under its new name.", 16f, theme.TextMuted)
        {
            Wrap = true,
        });

        _newName = new TextBox(theme)
        {
            Id = "profile-rename",
            Placeholder = "Enter profile name",
            MaxLength = PlayerProfile.NameCapacity - 1,
            Tooltip = "Names must be unique and can be up to 31 characters.",
        };
        column.AddChild(_newName);

        _renameError = new Label(theme.Body, string.Empty, 16f, theme.Danger);
        column.AddChild(_renameError);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f };
        var rename = new Button(theme, "RENAME PROFILE") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        rename.Activated += _ => RenameProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(rename);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    private GlassPanel BuildDeleteDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        column.AddChild(new SectionTitle(theme, "Delete profile", "Remove this operative?") { EyebrowColor = theme.Destructive, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 0f, 0f, 6f) });
        _deletePrompt = new Label(theme.Body, string.Empty, 17f, UiPalette.TextBody) { Wrap = true };
        column.AddChild(_deletePrompt);
        column.AddChild(new Label(theme.Body, "This cannot be undone.", 16f, theme.Destructive));

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f, Margin = new Thickness(0f, 8f, 0f, 0f) };
        var confirm = new Button(theme, "DELETE PERMANENTLY") { Accent = ButtonAccent.Destructive, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        confirm.Activated += _ => DeleteProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(confirm);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

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
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Save, centre, size, ink),
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
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Trash, centre, size, ink),
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
        panel.AddChild(layout);
        return panel;
    }

    private GlassPanel DetailPanel(UiTheme theme) => new(theme)
    {
        Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, Size == ShellSize.Compact ? (byte)235 : (byte)199),
        Border = UiPalette.White(0.12f),
        Padding = Size == ShellSize.Compact ? new Thickness(16f, 12f) : new Thickness(28f, 28f),
    };

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

    private void SelectProfile(int index)
    {
        if (index < 0 || index >= Context.Config.Profiles.Count)
        {
            return;
        }

        Context.Config.ActiveIndex = index;
        ShowProfileDetails();
        Refresh();
    }

    private void ShowCreateDetails()
    {
        if (Context.Config.Profiles.Count >= GameConfig.MaxProfiles)
        {
            ShowProfileDetails();
            SetDetailsMessage($"Profile limit reached ({GameConfig.MaxProfiles}).");
            return;
        }

        _mode = DetailMode.Create;
        if (_name is not null)
        {
            _name.Text = string.Empty;
            _pendingFocus = _name;
        }

        if (_difficulty is not null)
        {
            _difficulty.SelectedIndex = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, AiDifficultyNames.All.Length - 1);
        }

        if (_unlockAllMissions is not null)
        {
            _unlockAllMissions.Value = false;
        }

        if (_formError is not null)
        {
            _formError.Text = string.Empty;
        }

        UpdateModeVisibility();
    }

    // Opens the box to rename the active profile in, its name in it and the keys on it.
    private void ShowRenameDetails()
    {
        if (Context.Config.Active is not { } profile)
        {
            return;
        }

        _mode = DetailMode.Rename;
        if (_newName is not null)
        {
            _newName.Text = profile.Name;
            _pendingFocus = _newName;
        }

        if (_renameError is not null)
        {
            _renameError.Text = string.Empty;
        }

        UpdateModeVisibility();
    }

    // Renames the active profile to what the box holds, and moves its saves to the new name.
    private void RenameProfile()
    {
        if (Context.Config.Active is not { } profile)
        {
            ShowProfileDetails();
            return;
        }

        string trimmed = (_newName?.Text ?? string.Empty).TrimEnd(' ', '\t');
        string? problem = trimmed.Length == 0 ? "Enter a profile name to continue."
            : Context.Config.Profiles.Any(other => !ReferenceEquals(other, profile) && string.Equals(other.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ? "That profile name is already in use."
            : null;
        if (problem is not null)
        {
            if (_renameError is not null)
            {
                _renameError.Text = problem;
            }

            _pendingFocus = _newName;
            return;
        }

        string before = profile.Name;
        if (string.Equals(before, GameConfig.Truncate(trimmed), StringComparison.Ordinal))
        {
            ShowProfileDetails();
            return;
        }

        if (!Context.Config.RenameProfile(Context.Config.ActiveIndex, trimmed))
        {
            if (_renameError is not null)
            {
                _renameError.Text = "The profile could not be renamed.";
            }

            return;
        }

        // The slots are kept by their profile's name, so they follow it.
        Context.Saves.RenameProfile(before, profile.Name);
        Context.Host.SaveProfiles();
        ShowProfileDetails();
        Refresh();
        SetDetailsMessage($"Renamed {before} to {profile.Name}.");
    }

    private void ShowDeleteDetails()
    {
        if (Context.Config.Profiles.Count <= 1 || Context.Config.Active is not { } profile)
        {
            return;
        }

        _mode = DetailMode.ConfirmDelete;
        int saves = Context.Saves.Slots.Count(slot => string.Equals(slot.ProfileName, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (_deletePrompt is not null)
        {
            _deletePrompt.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Delete {profile.Name} and {saves} associated {(saves == 1 ? "save slot" : "save slots")}? Their profile settings, campaign progress, and screenshots will be removed.");
        }

        UpdateModeVisibility();
        _pendingFocus = _deleteDetails?.Descendants().OfType<Button>().FirstOrDefault();
    }

    private void ShowProfileDetails()
    {
        _mode = DetailMode.Profile;
        _pendingFocus = null;
        UpdateModeVisibility();
    }

    private void UpdateModeVisibility()
    {
        if (_profileDetails is null || _createDetails is null || _deleteDetails is null || _renameDetails is null)
        {
            return;
        }

        _renameDetails.Visibility = _mode == DetailMode.Rename ? Visibility.Visible : Visibility.Collapsed;

        _profileDetails.Visibility = _mode == DetailMode.Profile ? Visibility.Visible : Visibility.Collapsed;
        _createDetails.Visibility = _mode == DetailMode.Create ? Visibility.Visible : Visibility.Collapsed;
        _deleteDetails.Visibility = _mode == DetailMode.ConfirmDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CreateProfile()
    {
        string name = _name?.Text ?? string.Empty;
        int difficulty = _difficulty?.SelectedIndex ?? 1;
        bool unlockAllMissions = _unlockAllMissions?.Value == true;
        string trimmed = name.TrimEnd(' ', '\t');

        if (Context.Config.Profiles.Count >= GameConfig.MaxProfiles)
        {
            SetFormError($"You can keep at most {GameConfig.MaxProfiles} profiles.");
            return;
        }

        if (trimmed.Length == 0)
        {
            SetFormError("Enter a profile name to continue.");
            _pendingFocus = _name;
            return;
        }

        if (Context.Config.Profiles.Any(profile => string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetFormError("That profile name is already in use.");
            _pendingFocus = _name;
            return;
        }

        if (!Context.Config.CreateProfile(name, difficulty))
        {
            SetFormError("The profile could not be created.");
            return;
        }

        if (unlockAllMissions && Context.Missions.Count > 0 && Context.Config.Active is { } created)
        {
            created.ActiveMission = Context.Missions[^1].Id;
        }

        Context.Host.SaveProfiles();
        ShowProfileDetails();
        Refresh();
        SetDetailsMessage($"Created {Context.Config.Active?.Name} on {AiDifficultyNames.Of(difficulty)}.");
    }

    private void DeleteProfile()
    {
        if (Context.Config.Profiles.Count <= 1 || Context.Config.Active is not { } profile)
        {
            ShowProfileDetails();
            return;
        }

        string deletedName = profile.Name;
        int deletedSaves = Context.Saves.DeleteByProfile(deletedName);
        if (!Context.Config.DeleteProfile(Context.Config.ActiveIndex))
        {
            return;
        }

        Context.Host.SaveProfiles();
        ShowProfileDetails();
        Refresh();
        SetDetailsMessage($"Deleted {deletedName} and {deletedSaves} {(deletedSaves == 1 ? "save slot" : "save slots")}.");
    }

    private void SaveToSelectedSlot()
    {
        if (Context.Config.Active is not { } profile)
        {
            return;
        }

        int slot = _selectedSlot;
        int missionId = profile.ActiveMission;
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

    private void Refresh()
    {
        int count = Context.Config.Profiles.Count;
        for (int i = 0; i < _cards.Count; i++)
        {
            ProfileCard card = _cards[i];
            if (i >= count)
            {
                card.Visibility = Visibility.Collapsed;
                continue;
            }

            PlayerProfile profile = Context.Config.Profiles[i];
            card.Visibility = Visibility.Visible;
            card.Name = profile.Name;
            card.Active = i == Context.Config.ActiveIndex;
            card.Difficulty = AiDifficultyNames.Of(profile.Difficulty);
            card.Progress = ProgressOf(profile);
            card.Unlocked = UnlockedOf(profile);
            card.Total = Context.Missions.Count;
            card.Mission = Context.Missions.FirstOrDefault(mission => mission.Id == profile.ActiveMission) is { } reached
                ? string.Create(CultureInfo.InvariantCulture, $"mission {reached.Number} · {reached.Name}")
                : MissionName(profile.ActiveMission);
        }

        if (Context.Config.Active is { } active)
        {
            if (_profileName is not null)
            {
                _profileName.Text = active.Name;
            }

            if (_profileDifficulty is not null)
            {
                _profileDifficulty.Value = AiDifficultyNames.Of(active.Difficulty);
            }

            if (_profileProgress is not null)
            {
                _profileProgress.Value = string.Create(CultureInfo.InvariantCulture, $"{UnlockedOf(active)} / {Context.Missions.Count}");
            }

            if (_profileMission is not null)
            {
                _profileMission.Value = Context.Missions.FirstOrDefault(mission => mission.Id == active.ActiveMission) is { } current
                    ? string.Create(CultureInfo.InvariantCulture, $"{current.Number:00} · {current.Name}")
                    : MissionName(active.ActiveMission);
            }
        }

        if (_profileCount is not null)
        {
            _profileCount.Text = string.Create(CultureInfo.InvariantCulture, $"{count} / {GameConfig.MaxProfiles}");
        }

        if (_deleteProfile is not null)
        {
            _deleteProfile.IsEnabled = count > 1;
            _deleteProfile.Tooltip = count > 1
                ? "Permanently delete this profile and every save slot belonging to it."
                : "Keep at least one profile so campaign progress and difficulty always have an owner.";
        }

        RefreshSlots();
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
        }
    }

    private int UnlockedOf(PlayerProfile profile)
    {
        for (int i = 0; i < Context.Missions.Count; i++)
        {
            if (Context.Missions[i].Id == profile.ActiveMission)
            {
                return i + 1;
            }
        }

        return Context.Missions.Count == 0 ? 0 : 1;
    }

    private string ProgressOf(PlayerProfile profile)
    {
        if (Context.Missions.Count == 0)
        {
            return "CAMPAIGN PROGRESS  ·  mission data unavailable";
        }

        int index = 0;
        for (int i = 0; i < Context.Missions.Count; i++)
        {
            if (Context.Missions[i].Id == profile.ActiveMission)
            {
                index = i;
                break;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"CAMPAIGN PROGRESS  ·  {index + 1}/{Context.Missions.Count} UNLOCKED");
    }

    private string MissionName(int missionId)
    {
        foreach (MissionInfo mission in Context.Missions)
        {
            if (mission.Id == missionId)
            {
                return $"{mission.Number:00}  {mission.Name}";
            }
        }

        return $"MISSION {missionId}";
    }

    private void SetFormError(string message)
    {
        if (_formError is not null)
        {
            _formError.Text = message;
        }
    }

    private void SetDetailsMessage(string message)
    {
        if (_detailsMessage is not null)
        {
            // The line takes room only while it has something to say.
            _detailsMessage.Text = message;
            _detailsMessage.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

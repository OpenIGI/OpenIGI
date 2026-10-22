using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.UI;
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
    protected override IReadOnlyList<KeyHint> Hints => [new("CLICK", "Set active"), new("ENTER", "Load save"), new("ESC", "Back")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var columns = new Grid { ColumnSpacing = 32f, Padding = new Thickness(SideMargin, 48f, SideMargin, 40f) };
        columns.ColumnDefinitions.Add(GridLength.Absolute(600f));
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

    private Grid BuildProfileRail(UiTheme theme)
    {
        var layout = new Grid { RowSpacing = 20f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);

        layout.Add(new SectionTitle(theme, "Operatives", "Player profiles") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        layout.Add(new Label(theme.Body, "Choose the operative whose progress and difficulty you want to use.", 16f, UiPalette.TextTertiary)
        {
            Wrap = true,
        }, 1, 0);

        var scroll = new ScrollView(theme);
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

        scroll.SetContent(cards);
        layout.Add(scroll, 2, 0);

        var create = new Button(theme, "NEW PROFILE")
        {
            Accent = ButtonAccent.Solid,
            Font = theme.Display,
            TextSize = 22f,
            Tracking = 0.2f,
            Padding = new Thickness(24f, 20f),
            Tooltip = "Create a new player profile with its own campaign difficulty.",
        };
        create.Activated += _ => ShowCreateDetails();
        layout.Add(create, 3, 0);
        return layout;
    }

    private Grid BuildWorkspace(UiTheme theme)
    {
        var workspace = new Grid { RowSpacing = 24f };
        workspace.ColumnDefinitions.Add(GridLength.Star);
        workspace.RowDefinitions.Add(GridLength.Auto);
        workspace.RowDefinitions.Add(GridLength.Star);

        var details = new Panel();
        _profileDetails = BuildProfileDetails(theme);
        _createDetails = BuildCreateDetails(theme);
        _deleteDetails = BuildDeleteDetails(theme);
        _createDetails.Visibility = Visibility.Collapsed;
        _deleteDetails.Visibility = Visibility.Collapsed;
        details.AddChild(_profileDetails);
        details.AddChild(_createDetails);
        details.AddChild(_deleteDetails);
        workspace.Add(details, 0, 0);
        workspace.Add(BuildSaves(theme), 1, 0);
        return workspace;
    }

    private GlassPanel BuildProfileDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        var row = new Grid { ColumnSpacing = 24f };
        row.ColumnDefinitions.Add(GridLength.Star);
        row.ColumnDefinitions.Add(GridLength.Auto);
        row.RowDefinitions.Add(GridLength.Auto);

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 18f };
        column.AddChild(new Eyebrow(theme, "Active profile"));
        _profileName = new Label(theme.Display, string.Empty, 112f, theme.Text);
        column.AddChild(_profileName);

        var stats = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 40f, Margin = new Thickness(0f, 8f, 0f, 0f) };
        _profileDifficulty = new Stat(theme, "Difficulty") { ValueColor = theme.Accent };
        _profileProgress = new Stat(theme, "Campaign progress");
        _profileMission = new Stat(theme, "Current mission");
        stats.Add(_profileDifficulty);
        stats.Add(_profileProgress);
        stats.Add(_profileMission);
        column.AddChild(stats);

        _detailsMessage = new Label(theme.Body, string.Empty, 16f, theme.TextMuted);
        column.AddChild(_detailsMessage);
        row.Add(column, 0, 0);

        _deleteProfile = new Button(theme, "DELETE PROFILE")
        {
            Accent = ButtonAccent.Destructive,
            TextSize = 17f,
            Tracking = 0.18f,
            Padding = new Thickness(24f, 16f),
            VerticalAlignment = VerticalAlignment.Bottom,
            Tooltip = "Permanently delete this profile and every save slot belonging to it.",
        };
        _deleteProfile.Activated += _ => ShowDeleteDetails();
        row.Add(_deleteProfile, 0, 1);

        panel.AddChild(row);
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
        var create = new Button(theme, "CREATE PROFILE") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        create.Activated += _ => CreateProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(create);
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
        var confirm = new Button(theme, "DELETE PERMANENTLY") { Accent = ButtonAccent.Destructive, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        confirm.Activated += _ => DeleteProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(confirm);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    private GlassPanel BuildSaves(UiTheme theme)
    {
        var panel = new GlassPanel(theme) { Padding = new Thickness(44f, 32f) };
        var layout = new Grid { RowSpacing = 20f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        layout.Add(new Eyebrow(theme, "Profile saves") { VerticalAlignment = VerticalAlignment.Center }, 0, 0);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        var save = new Button(theme, "SAVE") { Accent = ButtonAccent.Solid, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 12f) };
        save.Activated += _ => SaveToSelectedSlot();
        var load = new Button(theme, "LOAD") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 12f) };
        load.Activated += _ => LoadSelectedSlot();
        var delete = new Button(theme, "DELETE SAVE") { Accent = ButtonAccent.Destructive, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 12f) };
        delete.Activated += _ => DeleteSelectedSlot();
        actions.Add(save);
        actions.Add(load);
        actions.Add(delete);
        layout.Add(actions, 0, 1);

        // Eight slots, four by two, the way the design lays them out.
        var grid = new Grid { RowSpacing = 12f, ColumnSpacing = 12f, Id = "profile-saves" };
        grid.RowDefinitions.Add(GridLength.Star);
        grid.RowDefinitions.Add(GridLength.Star);
        for (int column = 0; column < SlotColumns; column++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);
        }

        _slots.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            int index = i;
            var tile = new SlotTile(theme, string.Create(CultureInfo.InvariantCulture, $"Slot {i + 1:00}")) { Selected = i == _selectedSlot };
            tile.Activated += _ => SelectSlot(index);
            _slots.Add(tile);
            grid.Add(tile, i / SlotColumns, i % SlotColumns);
        }

        layout.Add(grid, 1, 0, columnSpan: 2);
        panel.AddChild(layout);
        return panel;
    }

    private static GlassPanel DetailPanel(UiTheme theme) => new(theme) { Padding = new Thickness(44f, 40f) };

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
        if (_profileDetails is null || _createDetails is null || _deleteDetails is null)
        {
            return;
        }

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
            card.Difficulty = $"{AiDifficultyNames.Of(profile.Difficulty)} difficulty";
            card.Progress = ProgressOf(profile);
            card.Unlocked = UnlockedOf(profile);
            card.Total = Context.Missions.Count;
            card.Mission = $"CURRENT  ·  {MissionName(profile.ActiveMission)}";
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
                _profileProgress.Value = string.Create(CultureInfo.InvariantCulture, $"{UnlockedOf(active)}/{Context.Missions.Count} unlocked");
            }

            if (_profileMission is not null)
            {
                _profileMission.Value = MissionName(active.ActiveMission);
            }
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
            _detailsMessage.Text = message;
        }
    }
}

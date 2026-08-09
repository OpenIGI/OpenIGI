using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.UI;
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

    private Panel? _profileDetails;
    private Panel? _createDetails;
    private Panel? _deleteDetails;
    private TextBox? _name;
    private Dropdown? _difficulty;
    private Toggle? _unlockAllMissions;
    private Label? _profileName;
    private Label? _profileDifficulty;
    private Label? _profileProgress;
    private Label? _profileMission;
    private Label? _detailsMessage;
    private Label? _formError;
    private Label? _deletePrompt;
    private Button? _deleteProfile;
    private ListView? _slots;
    private Control? _pendingFocus;

    /// <summary>Initializes the screen.</summary>
    public ProfilesScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Profile;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var columns = new Grid { ColumnSpacing = 28f };
        columns.ColumnDefinitions.Add(GridLength.Stars(0.42f));
        columns.ColumnDefinitions.Add(GridLength.Stars(0.58f));
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

    private Panel BuildProfileRail(UiTheme theme)
    {
        var panel = new Panel { Background = theme.Panel.WithAlpha(165), Padding = new Thickness(20f) };
        var layout = new Grid { RowSpacing = 14f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);

        layout.Add(new Label(theme.Heading, "PLAYER PROFILES", theme.HeadingTextSize * 0.53f, theme.Text)
        {
            Shadow = theme.Shadow,
        }, 0, 0);
        layout.Add(new Label(theme.Body, "Choose the operative whose progress and difficulty you want to use.", theme.BodyTextSize * 0.78f, theme.TextMuted)
        {
            Wrap = true,
        }, 1, 0);

        var scroll = new ScrollView(theme);
        var cards = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
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
            TextSize = theme.BodyTextSize * 1.02f,
            Padding = new Thickness(24f, 12f),
            Tooltip = "Create a new player profile with its own campaign difficulty.",
        };
        create.Activated += _ => ShowCreateDetails();
        layout.Add(create, 3, 0);

        panel.AddChild(layout);
        return panel;
    }

    private Grid BuildWorkspace(UiTheme theme)
    {
        var workspace = new Grid { RowSpacing = 20f };
        workspace.ColumnDefinitions.Add(GridLength.Star);
        workspace.RowDefinitions.Add(GridLength.Stars(0.44f));
        workspace.RowDefinitions.Add(GridLength.Stars(0.56f));

        var details = new Canvas();
        _profileDetails = BuildProfileDetails(theme);
        _createDetails = BuildCreateDetails(theme);
        _deleteDetails = BuildDeleteDetails(theme);
        _createDetails.Visibility = Visibility.Collapsed;
        _deleteDetails.Visibility = Visibility.Collapsed;
        details.Add(_profileDetails);
        details.Add(_createDetails);
        details.Add(_deleteDetails);
        workspace.Add(details, 0, 0);
        workspace.Add(BuildSaves(theme), 1, 0);
        return workspace;
    }

    private Panel BuildProfileDetails(UiTheme theme)
    {
        var panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 9f };
        column.AddChild(new Label(theme.Body, "ACTIVE PROFILE", theme.BodyTextSize * 0.72f, theme.Accent));
        _profileName = new Label(theme.Heading, string.Empty, theme.HeadingTextSize * 0.9f, theme.Text) { Shadow = theme.Shadow };
        column.AddChild(_profileName);
        column.AddChild(AccentRule(theme));
        _profileDifficulty = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.9f, theme.Accent);
        _profileProgress = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.82f, theme.TextMuted);
        _profileMission = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.82f, theme.Text);
        column.AddChild(_profileDifficulty);
        column.AddChild(_profileProgress);
        column.AddChild(_profileMission);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f, Margin = new Thickness(0f, 8f, 0f, 0f) };
        var newProfile = new Button(theme, "NEW") { Padding = new Thickness(22f, 9f), Accent = ButtonAccent.Solid };
        newProfile.Activated += _ => ShowCreateDetails();
        _deleteProfile = new Button(theme, "DELETE PROFILE")
        {
            Padding = new Thickness(18f, 9f),
            Tooltip = "Permanently delete this profile and every save slot belonging to it.",
        };
        _deleteProfile.Activated += _ => ShowDeleteDetails();
        actions.Add(newProfile);
        actions.Add(_deleteProfile);
        column.AddChild(actions);

        _detailsMessage = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.72f, theme.TextMuted);
        column.AddChild(_detailsMessage);
        panel.AddChild(column);
        return panel;
    }

    private Panel BuildCreateDetails(UiTheme theme)
    {
        var panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        column.AddChild(new Label(theme.Body, "NEW OPERATIVE", theme.BodyTextSize * 0.72f, theme.Accent));
        column.AddChild(new Label(theme.Heading, "Create profile", theme.HeadingTextSize * 0.72f, theme.Text));
        column.AddChild(new Label(theme.Body, "Campaign difficulty belongs to the profile and applies whenever it is active.", theme.BodyTextSize * 0.76f, theme.TextMuted)
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

        _formError = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.72f, theme.Accent);
        column.AddChild(_formError);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f };
        var create = new Button(theme, "CREATE PROFILE") { Accent = ButtonAccent.Solid, Padding = new Thickness(22f, 9f) };
        create.Activated += _ => CreateProfile();
        var cancel = new Button(theme, "CANCEL") { Padding = new Thickness(18f, 9f) };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(create);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    private Panel BuildDeleteDetails(UiTheme theme)
    {
        var panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        column.AddChild(new Label(theme.Body, "DELETE PROFILE", theme.BodyTextSize * 0.72f, theme.Accent));
        column.AddChild(new Label(theme.Heading, "Remove this operative?", theme.HeadingTextSize * 0.68f, theme.Text));
        _deletePrompt = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.82f, theme.TextMuted) { Wrap = true };
        column.AddChild(_deletePrompt);
        column.AddChild(new Label(theme.Body, "This cannot be undone.", theme.BodyTextSize * 0.76f, theme.Accent));

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f };
        var confirm = new Button(theme, "DELETE PERMANENTLY") { Padding = new Thickness(22f, 9f), Accent = ButtonAccent.Solid };
        confirm.Activated += _ => DeleteProfile();
        var cancel = new Button(theme, "CANCEL") { Padding = new Thickness(18f, 9f) };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(confirm);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    private Panel BuildSaves(UiTheme theme)
    {
        var panel = new Panel { Background = theme.Panel.WithAlpha(165), Padding = new Thickness(22f) };
        var layout = new Grid { RowSpacing = 10f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);

        layout.Add(new Label(theme.Heading, "PROFILE SAVES", theme.HeadingTextSize * 0.43f, theme.Text), 0, 0);
        _slots = new ListView(theme, SlotSummaries()) { Id = "profile-saves", VisibleRows = 5, RowHeight = 38f };
        layout.Add(_slots, 1, 0);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f };
        var save = new Button(theme, "SAVE") { Padding = new Thickness(17f, 8f) };
        save.Activated += _ => SaveToSelectedSlot();
        var load = new Button(theme, "LOAD") { Padding = new Thickness(17f, 8f) };
        load.Activated += _ => LoadSelectedSlot();
        var delete = new Button(theme, "DELETE SAVE") { Padding = new Thickness(17f, 8f) };
        delete.Activated += _ => DeleteSelectedSlot();
        actions.Add(save);
        actions.Add(load);
        actions.Add(delete);
        layout.Add(actions, 2, 0);

        panel.AddChild(layout);
        return panel;
    }

    private static Panel DetailPanel(UiTheme theme) => new()
    {
        Background = theme.Panel.WithAlpha(220),
        Padding = new Thickness(26f, 22f),
    };

    private static Panel AccentRule(UiTheme theme) => new()
    {
        Background = theme.Accent,
        Height = 3f,
        Width = 98f,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0f, 2f, 0f, 5f),
    };

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
        if (_slots is null || Context.Config.Active is not { } profile)
        {
            return;
        }

        int slot = _slots.SelectedIndex;
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
            height);
        RefreshSlots();
    }

    private void LoadSelectedSlot()
    {
        if (_slots is null || Context.Config.Active is not { } profile)
        {
            return;
        }

        if (Context.Saves.Find(profile.Name, _slots.SelectedIndex) is { } save)
        {
            Context.Host.StartMission(save.MissionId, profile.Difficulty);
        }
    }

    private void DeleteSelectedSlot()
    {
        if (_slots is not null && Context.Config.Active is { } profile
            && Context.Saves.Delete(profile.Name, _slots.SelectedIndex))
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
                _profileDifficulty.Text = $"{AiDifficultyNames.Of(active.Difficulty).ToUpperInvariant()} DIFFICULTY";
            }

            if (_profileProgress is not null)
            {
                _profileProgress.Text = ProgressOf(active);
            }

            if (_profileMission is not null)
            {
                _profileMission.Text = $"CURRENT MISSION  ·  {MissionName(active.ActiveMission)}";
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

    private void RefreshSlots() => _slots?.SetItems(SlotSummaries());

    private string[] SlotSummaries()
    {
        const int slotCount = 8;
        var summaries = new string[slotCount];
        string? profileName = Context.Config.Active?.Name;
        for (int i = 0; i < slotCount; i++)
        {
            SaveSlot? slot = profileName is null ? null : Context.Saves.Find(profileName, i);
            summaries[i] = slot is null
                ? $"SLOT {i + 1:00}  —  EMPTY"
                : $"SLOT {i + 1:00}  —  {slot.MissionName}  ·  {slot.SavedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}";
        }

        return summaries;
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

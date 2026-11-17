using System.Globalization;
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
/// The player's profile: one operative, shared by every game in the build. Who they are -- their name, and the Discord
/// account they may sign in with -- their online record, the campaign they are through, and their saves.
/// </summary>
/// <remarks>
/// The original kept several profiles on a computer, one a player; an account does that job now, and signing out and in
/// is how a second player takes the computer. Profiles an earlier version made stay in the file, untouched; the one that was
/// active goes on, and nothing makes new ones.
/// </remarks>
public sealed partial class ProfilesScreen : ShellScreen
{
    private enum DetailMode
    {
        Profile,
        Rename,
    }

    private DetailMode _mode;
    private GlassPanel? _identity;
    private GlassPanel? _renameDetails;
    private TextBox? _newName;
    private Label? _renameError;
    private Label? _profileName;
    private Label? _identityNote;
    private Label? _detailsMessage;
    private ProfileCard? _progress;
    private OptionCycler? _difficulty;
    private Control? _pendingFocus;

    /// <summary>Initializes the screen.</summary>
    /// <param name="context">The shell's context.</param>
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
        _shownAccount = AccountKey;

        // A phone has no room for two columns: who the player is first, then their record, the campaign and the saves, in
        // one column the finger drags.
        if (Size == ShellSize.Compact)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
            stack.AddChild(new SectionTitle(theme, string.Empty, "Profile") { TitleSize = 24f, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 4f, 0f, 0f) });
            stack.AddChild(BuildIdentity(theme));
            stack.AddChild(BuildAccount(theme));
            stack.AddChild(BuildProgress(theme));
            stack.AddChild(BuildRecord(theme));
            stack.AddChild(BuildSaves(theme));

            Refresh();
            return ScrollPage(stack);
        }

        var columns = new Grid { ColumnSpacing = Size == ShellSize.Wide ? 48f : 32f, Padding = new Thickness(SideMargin, 32f, SideMargin, 24f) };
        columns.ColumnDefinitions.Add(GridLength.Absolute(Size == ShellSize.Wide ? 560f : 460f));
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Star);
        columns.Add(BuildPlayerColumn(theme), 0, 0);
        columns.Add(BuildGameColumn(theme), 0, 1);
        Refresh();
        return columns;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, OpenIGI.Input.InputState input)
    {
        // Escape backs out of renaming or a question before leaving the page.
        if (nav.Back && (_mode != DetailMode.Profile || _confirmingDelete))
        {
            _confirmingDelete = false;
            ShowProfileDetails();
            Invalidate();
            return false;
        }

        // ENTER in the name's box renames, as its button does.
        if (Focus?.Focused is TextBox box && ReferenceEquals(box, _newName) && EnteredText(nav, input))
        {
            RenameProfile();
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

        // The account answers in its own time: the page is built again when what it shows of it changes.
        if (AccountKey != _shownAccount)
        {
            Invalidate();
        }
    }

    // Left: who the player is, their account and their online record.
    private ScrollView BuildPlayerColumn(UiTheme theme)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };
        column.AddChild(new SectionTitle(theme, "Operative", "Profile") { HorizontalAlignment = HorizontalAlignment.Left });
        column.AddChild(BuildIdentity(theme));
        column.AddChild(BuildAccount(theme));
        column.AddChild(BuildRecord(theme));

        var scroll = new ScrollView(theme);
        scroll.SetContent(column);
        return scroll;
    }

    // Right: the campaign of the game being played, and its saves.
    private Grid BuildGameColumn(UiTheme theme)
    {
        var workspace = new Grid { RowSpacing = 20f };
        workspace.ColumnDefinitions.Add(GridLength.Star);
        workspace.RowDefinitions.Add(GridLength.Auto);
        workspace.RowDefinitions.Add(GridLength.Star);
        workspace.Add(BuildProgress(theme), 0, 0);
        workspace.Add(BuildSaves(theme), 1, 0);
        return workspace;
    }

    // The player's mark and name, large; RENAME beside it, and the box to rename in where the panel stands while it is open.
    private Panel BuildIdentity(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        _identity = DetailPanel(theme);
        var row = new Grid { ColumnSpacing = compact ? 14f : 22f };
        row.RowDefinitions.Add(GridLength.Auto);
        row.ColumnDefinitions.Add(GridLength.Auto);
        row.ColumnDefinitions.Add(GridLength.Star);
        row.ColumnDefinitions.Add(GridLength.Auto);

        AccountSummary? account = Context.Host.Account?.Account;
        row.Add(new OperativeMark(theme, Context.Config.Active?.Name ?? account?.Name ?? "?") { SignedIn = account is not null, Side = compact ? 56f : 88f, VerticalAlignment = VerticalAlignment.Center }, 0, 0);

        var names = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f, VerticalAlignment = VerticalAlignment.Center };
        names.AddChild(new Eyebrow(theme, "Operative") { Color = theme.Accent, TextSize = compact ? 12f : 14f });
        _profileName = new Label(theme.Display, string.Empty, Size switch { ShellSize.Wide => 56f, ShellSize.Medium => 48f, _ => 28f }, theme.Text);
        names.AddChild(_profileName);
        _identityNote = new Label(theme.Body, string.Empty, compact ? 14f : 16f, theme.TextSecondary) { Wrap = true };
        names.AddChild(_identityNote);
        row.Add(names, 0, 1);

        var rename = new Button(theme, compact ? string.Empty : "Rename")
        {
            Accent = ButtonAccent.Text,
            LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Pencil, centre, size, ink),
            GlyphSize = 16f,
            TextSize = compact ? 15f : 17f,
            Tracking = 0.12f,
            Padding = new Thickness(12f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Change the name you play under offline. Signed in, multiplayer shows your Discord name.",
        };
        rename.Activated += _ => ShowRenameDetails();
        row.Add(rename, 0, 2);

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        column.AddChild(row);
        _detailsMessage = new Label(theme.Body, string.Empty, 16f, theme.TextMuted) { Visibility = Visibility.Collapsed, Wrap = true };
        column.AddChild(_detailsMessage);
        _identity.AddChild(column);

        _renameDetails = BuildRenameDetails(theme);
        var details = new Panel();
        details.AddChild(_identity);
        details.AddChild(_renameDetails);
        UpdateModeVisibility();
        return details;
    }

    // The name in a box to change it in, where the identity panel stands.
    private GlassPanel BuildRenameDetails(UiTheme theme)
    {
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        column.AddChild(new SectionTitle(theme, "Operative", "Rename") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 0f, 0f, 6f) });
        column.AddChild(new Label(theme.Body, "Your progress, settings and saves stay with you under the new name.", 16f, theme.TextMuted) { Wrap = true });

        _newName = new TextBox(theme)
        {
            Id = "profile-rename",
            Placeholder = "Enter a name",
            MaxLength = PlayerProfile.NameCapacity - 1,
            Tooltip = "Up to 31 characters.",
        };
        column.AddChild(_newName);
        _renameError = new Label(theme.Body, string.Empty, 16f, theme.Danger);
        column.AddChild(_renameError);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f };
        var rename = new Button(theme, "RENAME") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        rename.Activated += _ => RenameProfile();
        var cancel = new Button(theme, "CANCEL") { Accent = ButtonAccent.Ghost, TextSize = 17f, Tracking = 0.12f, Padding = new Thickness(24f, 0f), Height = 44f };
        cancel.Activated += _ => ShowProfileDetails();
        actions.Add(rename);
        actions.Add(cancel);
        column.AddChild(actions);
        panel.AddChild(column);
        return panel;
    }

    // The campaign of the game being played: how far through, at which difficulty -- which the player may change here.
    private GlassPanel BuildProgress(UiTheme theme)
    {
        bool compact = Size == ShellSize.Compact;
        GlassPanel panel = DetailPanel(theme);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 8f : 12f };
        string game = Context.Games.FirstOrDefault(g => g.Title.Id == Context.ActiveTitleId)?.Title.DisplayName ?? "Campaign";
        column.AddChild(new Eyebrow(theme, "Campaign") { Color = theme.Accent, TextSize = compact ? 12f : 14f });

        _progress = new ProfileCard(theme, game) { Active = true, Id = "profile-progress" };
        column.AddChild(_progress);

        _difficulty = new OptionCycler(theme, "Difficulty", AiDifficultyNames.All, Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, AiDifficultyNames.All.Length - 1))
        {
            Id = "profile-difficulty",
            Inline = true,
            Tooltip = "The campaign's difficulty. A mission already started keeps the one it began with.",
        };
        _difficulty.SelectionChanged += SetDifficulty;
        column.AddChild(_difficulty);
        panel.AddChild(column);
        return panel;
    }

    private GlassPanel DetailPanel(UiTheme theme) => new(theme)
    {
        Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, Size == ShellSize.Compact ? (byte)235 : (byte)199),
        Border = UiPalette.White(0.12f),
        Padding = Size == ShellSize.Compact ? new Thickness(16f, 12f) : new Thickness(28f, 24f),
    };

    private void SetDifficulty(int difficulty)
    {
        if (Context.Config.Active is not { } profile || profile.Difficulty == difficulty)
        {
            return;
        }

        profile.Difficulty = difficulty;
        Context.Host.SaveProfiles();
        Refresh();
    }

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

    // Renames the profile to what the box holds, and moves its saves to the new name.
    private void RenameProfile()
    {
        if (Context.Config.Active is not { } profile)
        {
            ShowProfileDetails();
            return;
        }

        string trimmed = (_newName?.Text ?? string.Empty).TrimEnd(' ', '\t');
        string? problem = trimmed.Length == 0 ? "Enter a name to continue."
            : Context.Config.Profiles.Any(other => !ReferenceEquals(other, profile) && string.Equals(other.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ? "Another profile on this computer has that name."
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

    private void ShowProfileDetails()
    {
        _mode = DetailMode.Profile;
        _pendingFocus = null;
        UpdateModeVisibility();
    }

    private void UpdateModeVisibility()
    {
        if (_identity is null || _renameDetails is null)
        {
            return;
        }

        _identity.Visibility = _mode == DetailMode.Profile ? Visibility.Visible : Visibility.Collapsed;
        _renameDetails.Visibility = _mode == DetailMode.Rename ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        if (Context.Config.Active is { } active)
        {
            if (_profileName is not null)
            {
                _profileName.Text = active.Name;
            }

            if (_progress is not null)
            {
                _progress.Difficulty = AiDifficultyNames.Of(active.Difficulty);
                int reachedId = MissionReached(active);
                _progress.Mission = Context.Missions.FirstOrDefault(mission => mission.Id == reachedId) is { } reached
                    ? string.Create(CultureInfo.InvariantCulture, $"mission {reached.Number} · {reached.Name}")
                    : MissionName(reachedId);
                _progress.Unlocked = UnlockedOf(active);
                _progress.Total = Context.Missions.Count;
            }
        }

        if (_identityNote is not null)
        {
            _identityNote.Text = IdentityNote();
        }

        RefreshSlots();
    }

    private int UnlockedOf(PlayerProfile profile)
    {
        for (int i = 0; i < Context.Missions.Count; i++)
        {
            if (Context.Missions[i].Id == MissionReached(profile))
            {
                return i + 1;
            }
        }

        return Context.Missions.Count == 0 ? 0 : 1;
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

using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Profiles and saves: the player's profiles on the left, the save slots on the right, each with the
/// actions that operate on it.
/// </summary>
/// <remarks>
/// Profiles are the retail surface — a name, a difficulty and the furthest mission reached — read and
/// written through <see cref="OpenIGI.Game.Config.GameConfig"/> and its byte-compatible
/// <c>openigi-config.qvm</c>; the retail <c>config.qvm</c> is never touched. Save slots are the port's
/// own new format (<see cref="SaveSlotStore"/>): a named, timestamped, screenshotted point per slot,
/// separate from the profile so it can carry detail a profile does not.
/// </remarks>
public sealed class ProfilesScreen : Screen
{
    private readonly ShellContext _context;
    private ListView? _profiles;
    private ListView? _slots;

    /// <summary>Initializes a new instance of the <see cref="ProfilesScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public ProfilesScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Name = "Profiles";
    }

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        UiTheme theme = _context.Theme;
        var canvas = new Canvas();
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };
        column.AddChild(new Label(theme.Heading, "PROFILES & SAVES", theme.HeadingTextSize, theme.Accent) { Shadow = theme.Shadow });

        var columns = new Grid { Height = 560f, ColumnSpacing = 40f };
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Star);

        columns.Add(BuildProfiles(theme), 0, 0);
        columns.Add(BuildSaves(theme), 0, 1);
        column.AddChild(columns);

        canvas.Add(column, Anchor.Filled(64f));
        return canvas;
    }

    private StackPanel BuildProfiles(UiTheme theme)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        stack.AddChild(new Label(theme.Body, "Profiles", theme.BodyTextSize * 1.1f, theme.Text));

        _profiles = new ListView(theme, ProfileNames()) { VisibleRows = 8, SelectedIndex = _context.Config.ActiveIndex };
        _profiles.SelectionChanged += i => _context.Config.ActiveIndex = i;
        stack.AddChild(_profiles);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 16f };
        var create = new Button(theme, "New");
        create.Activated += _ =>
        {
            _context.Config.CreateProfile(null, _context.Config.Active?.Difficulty ?? 1);
            RefreshProfiles();
        };
        var delete = new Button(theme, "Delete");
        delete.Activated += _ =>
        {
            if (_context.Config.DeleteProfile(_context.Config.ActiveIndex))
            {
                RefreshProfiles();
            }
        };
        actions.Add(create);
        actions.Add(delete);
        stack.AddChild(actions);
        return stack;
    }

    private StackPanel BuildSaves(UiTheme theme)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        stack.AddChild(new Label(theme.Body, "Save slots", theme.BodyTextSize * 1.1f, theme.Text));

        _slots = new ListView(theme, SlotSummaries()) { VisibleRows = 8 };
        stack.AddChild(_slots);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 16f };
        var save = new Button(theme, "Save");
        save.Activated += _ => SaveToSelectedSlot();
        var load = new Button(theme, "Load");
        load.Activated += _ => LoadSelectedSlot();
        var delete = new Button(theme, "Delete");
        delete.Activated += _ => DeleteSelectedSlot();
        actions.Add(save);
        actions.Add(load);
        actions.Add(delete);
        stack.AddChild(actions);
        return stack;
    }

    private void SaveToSelectedSlot()
    {
        int slot = _slots?.SelectedIndex ?? 0;
        var profile = _context.Config.Active;
        int missionId = profile?.ActiveMission ?? 1;
        string missionName = MissionName(missionId);

        Bgra32[] thumb = _context.Host.CaptureThumbnail(out int width, out int height);
        _context.Saves.Save(
            slot,
            profile?.Name ?? "Profile",
            missionId,
            missionName,
            DateTimeOffset.Now,
            thumb,
            width,
            height);
        RefreshSlots();
    }

    private void LoadSelectedSlot()
    {
        if (_slots is null)
        {
            return;
        }

        SaveSlot? save = _context.Saves.Find(_slots.SelectedIndex);
        if (save is not null)
        {
            _context.Host.StartMission(save.MissionId, _context.Config.Active?.Difficulty ?? 1);
        }
    }

    private void DeleteSelectedSlot()
    {
        if (_slots is not null && _context.Saves.Delete(_slots.SelectedIndex))
        {
            RefreshSlots();
        }
    }

    private void RefreshProfiles() => _profiles?.SetItems(ProfileNames());

    private void RefreshSlots() => _slots?.SetItems(SlotSummaries());

    private string[] ProfileNames()
    {
        var names = new string[_context.Config.Profiles.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = _context.Config.Profiles[i].Name;
        }

        return names;
    }

    private string[] SlotSummaries()
    {
        const int slotCount = 8;
        var summaries = new string[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            SaveSlot? slot = _context.Saves.Find(i);
            summaries[i] = slot is null
                ? $"Slot {i + 1}  —  empty"
                : $"Slot {i + 1}  —  {slot.MissionName}  ·  {slot.SavedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}";
        }

        return summaries;
    }

    private string MissionName(int missionId)
    {
        foreach (MissionInfo mission in _context.Missions)
        {
            if (mission.Id == missionId)
            {
                return mission.Name;
            }
        }

        return $"Mission {missionId}";
    }
}

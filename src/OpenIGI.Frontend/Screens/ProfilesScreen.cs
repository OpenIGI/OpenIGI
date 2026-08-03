using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Frontend.Saves;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The profiles and saves section: the player's profiles on the left, the save slots on the right, each
/// with the actions that operate on it.
/// </summary>
/// <remarks>
/// Profiles are the retail surface — a name, a difficulty and the furthest mission reached — read and
/// written through <see cref="OpenIGI.Game.Config.GameConfig"/> and its byte-compatible
/// <c>openigi-config.qvm</c>; the retail <c>config.qvm</c> is never touched. Save slots are the port's
/// own new format (<see cref="SaveSlotStore"/>): a named, timestamped, screenshotted point per slot.
/// </remarks>
public sealed class ProfilesScreen : ShellScreen
{
    private ListView? _profiles;
    private ListView? _slots;

    /// <summary>Initializes a new instance of the <see cref="ProfilesScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public ProfilesScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Profile;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        var columns = new Grid { ColumnSpacing = 40f };
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Star);

        columns.Add(BuildProfiles(Context.Theme), 0, 0);
        columns.Add(BuildSaves(Context.Theme), 0, 1);
        return columns;
    }

    private StackPanel BuildProfiles(UiTheme theme)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        stack.AddChild(new Label(theme.Body, "Profiles", theme.BodyTextSize * 1.15f, theme.Accent));

        _profiles = new ListView(theme, ProfileNames()) { VisibleRows = 8, SelectedIndex = Context.Config.ActiveIndex };
        _profiles.SelectionChanged += i => Context.Config.ActiveIndex = i;
        stack.AddChild(_profiles);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 16f };
        var create = new Button(theme, "New");
        create.Activated += _ =>
        {
            Context.Config.CreateProfile(null, Context.Config.Active?.Difficulty ?? 1);
            RefreshProfiles();
        };
        var delete = new Button(theme, "Delete");
        delete.Activated += _ =>
        {
            if (Context.Config.DeleteProfile(Context.Config.ActiveIndex))
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
        stack.AddChild(new Label(theme.Body, "Save slots", theme.BodyTextSize * 1.15f, theme.Accent));

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
        var profile = Context.Config.Active;
        int missionId = profile?.ActiveMission ?? 1;

        Bgra32[] thumb = Context.Host.CaptureThumbnail(out int width, out int height);
        Context.Saves.Save(
            slot,
            profile?.Name ?? "Profile",
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
        if (_slots is null)
        {
            return;
        }

        SaveSlot? save = Context.Saves.Find(_slots.SelectedIndex);
        if (save is not null)
        {
            Context.Host.StartMission(save.MissionId, Context.Config.Active?.Difficulty ?? 1);
        }
    }

    private void DeleteSelectedSlot()
    {
        if (_slots is not null && Context.Saves.Delete(_slots.SelectedIndex))
        {
            RefreshSlots();
        }
    }

    private void RefreshProfiles() => _profiles?.SetItems(ProfileNames());

    private void RefreshSlots() => _slots?.SetItems(SlotSummaries());

    private string[] ProfileNames()
    {
        var names = new string[Context.Config.Profiles.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = Context.Config.Profiles[i].Name;
        }

        return names;
    }

    private string[] SlotSummaries()
    {
        const int slotCount = 8;
        var summaries = new string[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            SaveSlot? slot = Context.Saves.Find(i);
            summaries[i] = slot is null
                ? $"Slot {i + 1}  —  empty"
                : $"Slot {i + 1}  —  {slot.MissionName}  ·  {slot.SavedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}";
        }

        return summaries;
    }

    private string MissionName(int missionId)
    {
        foreach (MissionInfo mission in Context.Missions)
        {
            if (mission.Id == missionId)
            {
                return mission.Name;
            }
        }

        return $"Mission {missionId}";
    }
}

using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// Hosting: one panel for every mode. The mode first -- co-op against the mission's guards, or a versus mode on the mission's
/// map made an arena -- then the mission, how this player appears, where the game runs (in this game, which friends reach
/// through a room, or on a dedicated server started on this machine), whether it is listed, and co-op's own rules.
/// </content>
public sealed partial class MultiplayerScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;
    private static readonly string[] Listings = ["Public", "Private"];
    private static readonly string[] OffOn = ["Off", "On"];
    private static readonly string[] RunsOn = ["This game", "Dedicated server"];
    private static readonly int[] ReviveChoices = [3, 5, 10];
    private static readonly int[] ToughnessChoices = [0, 50, 100, 150];

    // The modes a game is hosted in, co-op first.
    private static readonly string[] HostModes = [GameModes.Coop, GameModes.TeamDeathmatch, GameModes.FreeForAll, GameModes.Elimination, GameModes.SearchAndDestroy];

    private int _hostMode;
    private int _mission;
    private int _difficulty = -1;
    private int _listing;
    private int _runsOn;

    private string HostMode => HostModes[Math.Clamp(_hostMode, 0, HostModes.Length - 1)];

    private GlassPanel BuildHosting(UiTheme theme, ICoopRooms coop)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.AddChild(new Label(theme.Heading, "HOST A GAME", 22f, theme.TextBright) { Margin = new Thickness(20f, 18f, 20f, 14f) });
        column.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.08f) });

        if (coop.ServerStatus is { } running)
        {
            column.AddChild(Section(theme, "Dedicated server"));
            column.AddChild(new Label(theme.Body, running, 15f, UiPalette.TextBody) { Wrap = true, Margin = new Thickness(20f, 4f, 20f, 8f) });
            var stop = new Button(theme, "STOP SERVER") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), Margin = new Thickness(20f, 4f, 20f, 20f), HorizontalAlignment = HorizontalAlignment.Left };
            stop.Activated += _ =>
            {
                coop.StopServer();
                Invalidate();
            };
            column.AddChild(stop);
            return Wrap(theme, column);
        }

        bool coopMode = HostMode == GameModes.Coop;
        column.AddChild(Section(theme, "Game"));
        OptionCycler mode = Row(theme, "Mode", [.. HostModes.Select(GameModes.Title)], _hostMode, "Co-op plays the mission against its guards, together. The others play its map as an arena, the guards gone, one side against the other or everyone for themselves.");
        mode.SelectionChanged += index =>
        {
            _hostMode = index;
            Invalidate();
        };
        column.AddChild(mode);

        IReadOnlyList<MissionInfo> missions = Context.Missions;
        if (missions.Count > 0)
        {
            _mission = Math.Clamp(_mission, 0, missions.Count - 1);
            OptionCycler mission = Row(theme, coopMode ? "Mission" : "Map", [.. missions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Number}. {m.Name}"))], _mission);
            mission.SelectionChanged += index => _mission = index;
            column.AddChild(mission);
        }

        List<PlayerSkin> looks = [.. PlayerSkins.All];
        int chosen = Math.Max(0, looks.FindIndex(s => s.Id == PlayerSkins.Of(PlayerSkins.Chosen.Value).Id));
        OptionCycler operative = Row(theme, "Operative", [.. looks.Select(static s => s.Name)], chosen, "How you appear to the others.");
        operative.SelectionChanged += index =>
        {
            PlayerSkins.Chosen.Value = looks[index].Id;
            Context.Host.SaveSettings();
        };
        column.AddChild(operative);

        if (coop.CanHostServer)
        {
            _runsOn = Math.Clamp(_runsOn, 0, RunsOn.Length - 1);
            OptionCycler runs = Row(theme, "Runs on", RunsOn, _runsOn, "This game: you host, and friends reach you through a room, nothing to open. Dedicated server: a server runs on this machine on its own, on UDP port 26001, and you join it like everyone else.");
            runs.SelectionChanged += index => _runsOn = index;
            column.AddChild(runs);
        }

        OptionCycler listing = Row(theme, "Listing", Listings, _listing, "Public games show in everyone's server browser; a private one only lets in those you give the code to.");
        listing.SelectionChanged += index => _listing = index;
        column.AddChild(listing);

        if (coopMode)
        {
            AddCoopRules(theme, column);
        }

        PrimaryAction? host = null;
        if (missions.Count > 0)
        {
            host = new PrimaryAction(theme, "Host") { Margin = new Thickness(20f, 12f, 20f, 20f) };
            host.Activated += _ => Host(coop);
        }

        return Wrap(theme, column, host);
    }

    // Co-op's own rules: the difficulty and how the squad plays.
    private void AddCoopRules(UiTheme theme, StackPanel column)
    {
        column.AddChild(Section(theme, "Co-op rules"));
        if (_difficulty < 0)
        {
            _difficulty = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
        }

        OptionCycler difficulty = Row(theme, "Difficulty", Difficulties, _difficulty);
        difficulty.SelectionChanged += index => _difficulty = index;
        column.AddChild(difficulty);

        OptionCycler friendlyFire = Row(theme, "Friendly fire", OffOn, CoopCVars.FriendlyFire.Value ? 1 : 0, "Whether your rounds hurt your teammates, and theirs you.");
        friendlyFire.SelectionChanged += index =>
        {
            CoopCVars.FriendlyFire.Value = index == 1;
            Context.Host.SaveSettings();
        };
        column.AddChild(friendlyFire);

        OptionCycler reviveTime = Row(
            theme,
            "Revive time",
            [.. ReviveChoices.Select(static s => string.Create(CultureInfo.InvariantCulture, $"{s} s"))],
            Math.Max(0, Array.IndexOf(ReviveChoices, CoopCVars.ReviveSeconds.Value)),
            "How long a teammate stands beside a downed player to bring them back.");
        reviveTime.SelectionChanged += index =>
        {
            CoopCVars.ReviveSeconds.Value = ReviveChoices[index];
            Context.Host.SaveSettings();
        };
        column.AddChild(reviveTime);

        OptionCycler toughness = Row(
            theme,
            "Enemies",
            [.. ToughnessChoices.Select(static p => p == 0 ? "As alone" : string.Create(CultureInfo.InvariantCulture, $"+{p}%"))],
            Math.Max(0, Array.IndexOf(ToughnessChoices, CoopCVars.EnemyHealthPerPlayer.Value)),
            "How much more health the guards have for each player past the first.");
        toughness.SelectionChanged += index =>
        {
            CoopCVars.EnemyHealthPerPlayer.Value = ToughnessChoices[index];
            Context.Host.SaveSettings();
        };
        column.AddChild(toughness);
    }

    // HOST: the mode, mission and listing chosen, in this game or on a dedicated server.
    private void Host(ICoopRooms coop)
    {
        IReadOnlyList<MissionInfo> missions = Context.Missions;
        if (missions.Count == 0)
        {
            return;
        }

        int missionId = missions[Math.Clamp(_mission, 0, missions.Count - 1)].Id;
        bool listed = _listing == 0;
        if (coop.CanHostServer && _runsOn == 1)
        {
            coop.HostServer(missionId, HostMode, listed);
            Invalidate();
            return;
        }

        int difficulty = Math.Clamp(_difficulty < 0 ? Context.Config.Active?.Difficulty ?? 1 : _difficulty, 0, Difficulties.Length - 1);
        if (Context.Config.Active is { } profile)
        {
            profile.Difficulty = difficulty;
        }

        coop.Host(missionId, difficulty, listed, HostMode);
    }

    // The panel: its form scrolling when the window is short, and HOST held at its foot.
    private static GlassPanel Wrap(UiTheme theme, StackPanel column, UiElement? foot = null)
    {
        var scroll = new ScrollView(theme);
        scroll.SetContent(column);
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.Add(scroll, 0, 0);
        if (foot is not null)
        {
            body.Add(foot, 1, 0);
        }

        var panel = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(16, 17, 19, 214),
            TintBottom = Bgra32.FromRgba(11, 12, 13, 214),
            Blur = 20f,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        panel.AddChild(body);
        return panel;
    }

    // A form's line: its label on the left, its value between arrows on the right.
    private static OptionCycler Row(UiTheme theme, string label, IEnumerable<string> options, int selected, string tooltip = "") =>
        new(theme, label, options, selected) { Inline = true, ContentWidth = 200f, Tooltip = tooltip };

    // A section's name in the panel, small and in the accent.
    private static Eyebrow Section(UiTheme theme, string name) =>
        new(theme, name) { Margin = new Thickness(20f, 16f, 20f, 4f), HorizontalAlignment = HorizontalAlignment.Left };
}

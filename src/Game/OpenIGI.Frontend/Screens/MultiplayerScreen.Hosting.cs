using System.Globalization;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// Hosting, in three panels. GAME: the mode first -- co-op against the mission's guards, or a versus mode on the mission's map
/// made an arena -- then the mission, how this player appears, where the game runs (in this game, which friends reach through
/// a room, or on a dedicated server started on this machine) and whether it is listed. The mode's rules: co-op's to set, a
/// versus mode's as they stand. And the game as it will be hosted, with HOST.
/// </content>
public sealed partial class MultiplayerScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;
    private static readonly string[] Listings = ["Public", "Private"];
    private static readonly string[] OffOn = ["Off", "On"];
    private static readonly string[] RunsOn = ["This game", "Dedicated"];
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

    private bool HostingCoop => HostMode == GameModes.Coop;

    private Grid BuildHostTab(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { ColumnSpacing = PanelGap };
        layout.RowDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Absolute(SideWidth));
        layout.Add(BuildGameSettings(theme, coop), 0, 0);
        layout.Add(HostingCoop ? BuildCoopRules(theme) : BuildMatchRules(theme), 0, 1);
        layout.Add(BuildHostSide(theme, coop), 0, 2);
        return layout;
    }

    private GlassPanel BuildGameSettings(UiTheme theme, ICoopRooms coop)
    {
        StackPanel column = FormColumn(theme, "Game");
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
            OptionCycler mission = Row(theme, HostingCoop ? "Mission" : "Map", [.. missions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Number}. {m.Name}"))], _mission);
            mission.SelectionChanged += index =>
            {
                _mission = index;
                Invalidate();
            };
            column.AddChild(mission);
        }

        if (HostingCoop)
        {
            List<PlayerSkin> looks = [.. PlayerSkins.All];
            int chosen = Math.Max(0, looks.FindIndex(s => s.Id == PlayerSkins.Of(PlayerSkins.Chosen.Value).Id));
            OptionCycler operative = Row(theme, "Operative", [.. looks.Select(static s => s.Name)], chosen, "How you appear to the others.");
            operative.SelectionChanged += index =>
            {
                PlayerSkins.Chosen.Value = looks[index].Id;
                Context.Host.SaveSettings();
            };
            column.AddChild(operative);
        }

        if (coop.CanHostServer)
        {
            _runsOn = Math.Clamp(_runsOn, 0, RunsOn.Length - 1);
            OptionCycler runs = Row(theme, "Runs on", RunsOn, _runsOn, "This game: you host, and friends reach you through a room, nothing to open. Dedicated: a server runs on this machine on its own, on UDP port 26001, and you join it like everyone else.");
            runs.SelectionChanged += index =>
            {
                _runsOn = index;
                Invalidate();
            };
            column.AddChild(runs);
        }

        OptionCycler listing = Row(theme, "Listing", Listings, _listing, "Public games show in everyone's server browser; a private one only lets in those you give the code to.");
        listing.SelectionChanged += index =>
        {
            _listing = index;
            Invalidate();
        };
        column.AddChild(listing);
        return FormPanel(theme, column);
    }

    // Co-op's own rules: the difficulty and how the squad plays.
    private GlassPanel BuildCoopRules(UiTheme theme)
    {
        StackPanel column = FormColumn(theme, "Co-op rules");
        if (_difficulty < 0)
        {
            _difficulty = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
        }

        OptionCycler difficulty = Row(theme, "Difficulty", Difficulties, _difficulty);
        difficulty.SelectionChanged += index =>
        {
            _difficulty = index;
            Invalidate();
        };
        column.AddChild(difficulty);

        OptionCycler friendlyFire = Row(theme, "Friendly fire", OffOn, CoopCVars.FriendlyFire.Value ? 1 : 0, "Whether your rounds hurt your teammates, and theirs you.");
        friendlyFire.SelectionChanged += index =>
        {
            CoopCVars.FriendlyFire.Value = index == 1;
            Context.Host.SaveSettings();
            Invalidate();
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
        return FormPanel(theme, column);
    }

    // A versus mode's rules, as the mode plays them.
    private GlassPanel BuildMatchRules(UiTheme theme)
    {
        StackPanel column = FormColumn(theme, "Match rules");
        var deathmatch = new DeathmatchSettings();
        var rounds = new RoundSettings();
        (string Label, string Value)[] rules = HostMode switch
        {
            GameModes.TeamDeathmatch or GameModes.FreeForAll =>
            [
                ("Score limit", string.Create(CultureInfo.InvariantCulture, $"{deathmatch.ScoreLimit} kills")),
                ("Time limit", string.Create(CultureInfo.InvariantCulture, $"{deathmatch.TimeLimitMinutes:0} min")),
                ("Respawn", string.Create(CultureInfo.InvariantCulture, $"{deathmatch.RespawnSeconds:0} s")),
            ],
            _ =>
            [
                ("Rounds to win", rounds.RoundsToWin.ToString(CultureInfo.InvariantCulture)),
                ("Round time", string.Create(CultureInfo.InvariantCulture, $"{rounds.RoundSeconds / 60:0} min")),
                ("Buy time", string.Create(CultureInfo.InvariantCulture, $"{rounds.BuySeconds:0} s")),
                ("Sides swap", rounds.SwapSides ? "At half" : "Never"),
            ],
        };
        foreach ((string label, string value) in rules)
        {
            var line = new Grid { Margin = new Thickness(28f, 0f), Height = 66f };
            line.RowDefinitions.Add(GridLength.Star);
            line.ColumnDefinitions.Add(GridLength.Star);
            line.ColumnDefinitions.Add(GridLength.Auto);
            line.Add(new Label(theme.Body, label, 17f, theme.TextLabel) { VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            line.Add(new Label(theme.Heading, value.ToUpperInvariant(), 20f, theme.Text) { VerticalAlignment = VerticalAlignment.Center }, 0, 1);
            column.AddChild(line);
        }

        column.AddChild(new Label(theme.Body, "The mode's rules as it plays them; a dedicated server's own cfg can change them.", 14f, theme.TextMuted) { Wrap = true, Margin = new Thickness(28f, 8f, 28f, 0f) });
        return FormPanel(theme, column);
    }

    // The game as it will be hosted: its mission, a line saying it, the party coming, and HOST. While a dedicated server started
    // here runs, what it says and STOP SERVER instead.
    private GlassPanel BuildHostSide(UiTheme theme, ICoopRooms coop)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);

        IReadOnlyList<MissionInfo> missions = Context.Missions;
        MissionInfo? mission = missions.Count > 0 ? missions[Math.Clamp(_mission, 0, missions.Count - 1)] : null;
        string name = mission?.Name ?? "No missions";
        var top = new StackPanel { Orientation = Orientation.Vertical };
        top.AddChild(new MissionPlate(theme, mission?.Number.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty, $"Mission · {name}", 220f) { Margin = new Thickness(20f, 20f, 20f, 0f) });

        if (coop.ServerStatus is { } running)
        {
            top.AddChild(new Eyebrow(theme, "Dedicated server") { Margin = new Thickness(24f, 22f, 24f, 8f) });
            top.AddChild(Body(theme, running, theme.TextLabel, 16f));
            top.Children[^1].Margin = new Thickness(24f, 0f);
            body.Add(top, 0, 0);
            var stop = new PrimaryAction(theme, "Stop") { Subtitle = "STOPS THE SERVER STARTED HERE", Margin = new Thickness(20f, 0f, 20f, 20f) };
            stop.Activated += _ =>
            {
                coop.StopServer();
                Invalidate();
            };
            body.Add(stop, 2, 0);
            return Panel(theme, body, side: true);
        }

        var about = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(24f, 22f, 24f, 0f) };
        about.AddChild(new Eyebrow(theme, "Your game"));
        about.AddChild(new Label(theme.Heading, name.ToUpperInvariant(), 38f, theme.TextBright) { Wrap = true });
        about.AddChild(Body(theme, HostSummary(), theme.TextLabel, 16f));
        top.AddChild(about);

        {
            int going = Parties?.Party is { Code: not null } party ? party.Members.Count : 1;
            int most = HostingCoop ? 4 : Coop?.CanHostServer == true && _runsOn == 1 ? 32 : 16;
            var coming = new Grid { Margin = new Thickness(24f, 16f, 24f, 0f) };
            coming.RowDefinitions.Add(GridLength.Auto);
            coming.ColumnDefinitions.Add(GridLength.Star);
            coming.ColumnDefinitions.Add(GridLength.Auto);
            coming.Add(new Label(theme.Body, "Party joins automatically", 15f, theme.TextSecondary), 0, 0);
            coming.Add(new Label(theme.Mono, string.Create(CultureInfo.InvariantCulture, $"{going}/{most}"), 15f, theme.Text), 0, 1);
            top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(24f, 16f, 24f, 0f) });
            top.AddChild(coming);
        }

        body.Add(top, 0, 0);
        bool dedicated = coop.CanHostServer && _runsOn == 1;
        var host = new PrimaryAction(theme, "Host")
        {
            TitleSize = 46f,
            Subtitle = $"{(dedicated ? "DEDICATED" : "THIS GAME")} · {Listings[_listing].ToUpperInvariant()}",
            IsEnabled = mission is not null,
            Margin = new Thickness(20f, 0f, 20f, 20f),
        };
        host.Activated += _ => Host(coop);
        body.Add(host, 2, 0);
        return Panel(theme, body, side: true);
    }

    // The game in a sentence, as it will be hosted.
    private string HostSummary()
    {
        string listed = _listing == 0 ? "Public listing" : "Private: only those with the code";
        if (HostingCoop)
        {
            int difficulty = Math.Clamp(_difficulty < 0 ? Context.Config.Active?.Difficulty ?? 1 : _difficulty, 0, Difficulties.Length - 1);
            return $"Co-op, {Difficulties[difficulty].ToLowerInvariant()} difficulty, friendly fire {(CoopCVars.FriendlyFire.Value ? "on" : "off")}. {listed}, up to 4 players.";
        }

        int most = Coop?.CanHostServer == true && _runsOn == 1 ? 32 : 16;
        return HostMode switch
        {
            GameModes.TeamDeathmatch => $"Team deathmatch to {new DeathmatchSettings().ScoreLimit} kills or {new DeathmatchSettings().TimeLimitMinutes:0} minutes. {listed}, up to {most} players.",
            GameModes.FreeForAll => $"Everyone for themselves to {new DeathmatchSettings().ScoreLimit} kills or {new DeathmatchSettings().TimeLimitMinutes:0} minutes. {listed}, up to {most} players.",
            _ => $"{GameModes.Title(HostMode)}, first to {new RoundSettings().RoundsToWin} rounds, one life a round. {listed}, up to {most} players.",
        };
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

    // A form panel's column, its group's name at the top.
    private static StackPanel FormColumn(UiTheme theme, string title)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0f, 28f, 0f, 20f) };
        column.AddChild(GroupTitle(theme, title, new Thickness(28f, 0f, 28f, 12f)));
        return column;
    }

    // A form panel: as tall as its rows, at the top of its column.
    private static GlassPanel FormPanel(UiTheme theme, StackPanel column)
    {
        GlassPanel panel = Panel(theme, column);
        panel.VerticalAlignment = VerticalAlignment.Top;
        return panel;
    }

    // A form's line: its label on the left, its value between arrows on the right.
    private static OptionCycler Row(UiTheme theme, string label, IEnumerable<string> options, int selected, string tooltip = "") =>
        new(theme, label, options, selected) { Inline = true, ContentWidth = 260f, Tooltip = tooltip, Margin = new Thickness(28f, 4f) };
}

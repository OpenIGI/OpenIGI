using OpenIGI.Host.Dedicated;
using OpenIGI.Host.Multiplayer;
using Xunit;

namespace OpenIGI.Desktop.Tests.Dedicated;

public sealed class DedicatedServerTests
{
    [Fact]
    public void A_config_reads_its_settings_quoted_values_comments_and_rotation_and_names_what_it_did_not_understand()
    {
        List<string> problems = [];
        ServerConfig config = ServerConfig.Parse(
            [
                "# a server",
                "name \"OpenIGI | S&D  EU\"   // shown in the list",
                "port 27015",
                "maxplayers 12",
                "rcon_password hunter2",
                "public 0",
                "map 1 sd",
                "map 3",
                "map 2 nosuchmode",
                "frobnicate 1",
            ],
            problems);

        Assert.Equal("OpenIGI | S&D  EU", config.Name);
        Assert.Equal((27015, 12, "hunter2", false), (config.Port, config.MaxPlayers, config.RconPassword, config.Public));
        Assert.Equal([new RotationEntry(1, "sd"), new RotationEntry(3, string.Empty)], config.Rotation);
        Assert.Equal(2, problems.Count);
        Assert.Contains("line 9", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_hosted_from_a_form_writes_a_config_the_server_reads_as_it_was_filled_in()
    {
        Core.Titles.DedicatedServerLaunch form = new()
        {
            Name = "Jones \"the\" host",
            Port = 26005,
            MaxPlayers = 40,
            Password = "pw",
            Mission = 3,
            Mode = "sd",
            Region = "eu",
            Public = true,
            Room = false,
        };

        List<string> problems = [];
        ServerConfig config = ServerConfig.Parse(form.ToConfig().Split('\n', StringSplitOptions.TrimEntries), problems);

        Assert.Empty(problems);
        Assert.Equal("Jones 'the' host", config.Name);
        Assert.Equal((26005, 32, "pw", "eu", true, false), (config.Port, config.MaxPlayers, config.Password, config.Region, config.Public, config.Room));
        Assert.Equal([new RotationEntry(3, "sd")], config.Rotation);
    }

    [Fact]
    public void A_ban_keeps_a_key_out_until_it_runs_out_and_outlives_the_list_that_made_it()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bans-{Guid.NewGuid():N}.json");
        DateTimeOffset now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        try
        {
            BanList bans = new(path, () => now);
            bans.Add("key-a", "Joe", 30, "spawn camping");
            bans.Add("key-b", "Kim", 0, "cheating");
            Assert.Contains("spawn camping", bans.Check("key-a"), StringComparison.Ordinal);

            BanList again = new(path, () => now.AddMinutes(31));
            Assert.Null(again.Load());
            Assert.Null(again.Check("key-a"));
            Assert.Contains("cheating", again.Check("key-b"), StringComparison.Ordinal);
            Assert.Equal("Kim", again.Remove("kim")?.Name);
            Assert.Null(again.Check("key-b"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_console_finds_players_by_slot_or_a_start_of_their_name_and_kicks_and_bans_them()
    {
        FakeServer server = new();
        ServerConsole console = new(server);

        Assert.Contains("could be", console.Execute("kick jo")[0], StringComparison.Ordinal);
        Assert.StartsWith("Kicked Joseph", console.Execute("kick #2 too loud")[0], StringComparison.Ordinal);
        Assert.Equal((2, "too loud"), server.Removed[0]);

        Assert.StartsWith("Banned Anya for 60 minutes", console.Execute("ban an 60 team killing")[0], StringComparison.Ordinal);
        Assert.Contains("team killing", server.Bans.Check("key-anya"), StringComparison.Ordinal);
        Assert.Equal(3, server.Removed[1].Slot);
    }

    [Fact]
    public void A_config_and_the_console_set_the_buy_time_by_its_name_and_refuse_a_value_it_does_not_take()
    {
        try
        {
            List<string> problems = [];
            ServerConfig.Parse(["mp_buytime 30", "MP_TIMELIMIT 15"], problems);
            Assert.Empty(problems);
            Assert.Equal(30f, Game.Multiplayer.NetCVars.BuyTime.Value);
            Assert.Equal(15f, Game.Multiplayer.NetCVars.TimeLimit.Value);

            ServerConfig.Parse(["mp_buytime soon"], problems);
            Assert.Contains("line 1: mp_buytime", Assert.Single(problems), StringComparison.Ordinal);
            Assert.Equal(30f, Game.Multiplayer.NetCVars.BuyTime.Value);

            ServerConsole console = new(new FakeServer());
            Assert.Equal(["mp_buytime is 45."], console.Execute("mp_buytime 45"));
            Assert.Equal(45f, Game.Multiplayer.NetCVars.BuyTime.Value);
            Assert.StartsWith("mp_buytime is 45 (default 20", console.Execute("mp_buytime")[0], StringComparison.Ordinal);
            Assert.Contains("sv_shotlog is", console.Execute("sv_shotlog 0")[0], StringComparison.Ordinal);
            Assert.False(Game.Multiplayer.NetCVars.ShotLog.Value);

            // The money, by I.G.I. 2's names under mp_.
            problems.Clear();
            ServerConfig.Parse(["mp_moneykill 500", "mp_moneycap 9000", "mp_moneyteamkill 0"], problems);
            Assert.Empty(problems);
            Assert.Equal(500, Game.Multiplayer.NetCVars.MoneyKill.Value);
            Assert.Equal(9000, Game.Multiplayer.NetCVars.MoneyCap.Value);
            Assert.Equal(["mp_moneywin is 2000."], console.Execute("mp_moneywin 2000"));
            Assert.Equal(["mp_bombendtime is 6."], console.Execute("mp_bombendtime 6"));
            Assert.Equal(6f, Game.Multiplayer.NetCVars.BombEndTime.Value);
            Game.Multiplayer.MultiplayerRules money = Game.Multiplayer.NetCVars.WithMoney(new Game.Multiplayer.MultiplayerRules());
            Assert.Equal((500, 9000, 0, 2000), (money.MoneyKill, money.MoneyCap, money.MoneyTeamKill, money.MoneyMissionWin));
        }
        finally
        {
            Game.Multiplayer.NetCVars.MoneyKill.Reset();
            Game.Multiplayer.NetCVars.MoneyCap.Reset();
            Game.Multiplayer.NetCVars.MoneyTeamKill.Reset();
            Game.Multiplayer.NetCVars.MoneyWin.Reset();
            Game.Multiplayer.NetCVars.BombEndTime.Reset();
            Game.Multiplayer.NetCVars.BuyTime.Reset();
            Game.Multiplayer.NetCVars.TimeLimit.Reset();
            Game.Multiplayer.NetCVars.ShotLog.Reset();
        }
    }

    [Fact]
    public void The_console_changes_map_and_mode_and_refuses_what_is_not_one()
    {
        FakeServer server = new();
        ServerConsole console = new(server);

        Assert.StartsWith("Going on to mission 4", console.Execute("map 4 tdm")[0], StringComparison.Ordinal);
        Assert.Equal((4, "tdm"), server.WentTo);
        Assert.Contains("is not a mode", console.Execute("mode chess")[0], StringComparison.Ordinal);
        Assert.Contains("Unknown command", console.Execute("launch missiles")[0], StringComparison.Ordinal);
        Assert.Equal(["Stopping."], console.Execute("quit"));
        Assert.True(server.QuitAsked);
    }

    private sealed class FakeServer : IServerControl
    {
        public List<(int Slot, string Reason)> Removed { get; } = [];

        public (int Mission, string? Mode)? WentTo { get; private set; }

        public bool QuitAsked { get; private set; }

        public string Name => "Test";

        public string Map => "1";

        public string Mode => "sd";

        public int MaxPlayers => 16;

        public IReadOnlyList<ServerPlayer> Players { get; } =
        [
            new ServerPlayer(1, "Jones", "key-jones", 40, 0, 3, 1),
            new ServerPlayer(2, "Joseph", "key-joseph", 80, 1, 0, 2),
            new ServerPlayer(3, "Anya", "key-anya", 20, 1, 5, 0),
        ];

        public BanList Bans { get; } = new(null);

        public IReadOnlyList<RotationEntry> Rotation => [];

        public bool Remove(int slot, string reason)
        {
            Removed.Add((slot, reason));
            return true;
        }

        public void Say(string text)
        {
        }

        public string? ChangeMap(int mission, string? mode)
        {
            WentTo = (mission, mode);
            return null;
        }

        public RotationEntry? NextMap() => null;

        public bool UseMode(string mode) => Game.Multiplayer.Modes.GameModes.All.Contains(mode);

        public void Quit() => QuitAsked = true;
    }
}

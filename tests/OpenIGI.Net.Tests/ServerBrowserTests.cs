using System.Text;
using System.Text.Json.Nodes;
using OpenIGI.Net.Browsing;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>The server browser: the master's list, servers' own answers and the player's book, one line a server.</summary>
public sealed class ServerBrowserTests
{
    // A server's answer in these tests: "name|players|max".
    private static BrowserInfo? Decode(byte[] info)
    {
        string[] parts = Encoding.UTF8.GetString(info).Split('|');
        return parts.Length == 3
            ? new BrowserInfo("igi1", parts[0], "1", "tdm", int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), false, "v", true)
            : null;
    }

    [Fact]
    public void The_masters_list_reads_servers_by_address_and_rooms_by_code_and_skips_other_games()
    {
        JsonNode list = JsonNode.Parse("""
            { "servers": [
              { "title": "igi1", "address": "203.0.113.7", "port": 26001, "name": "Desert", "map": "3", "mode": "sd", "players": 4, "maxPlayers": 16, "region": "eu", "room": "ABC234" },
              { "title": "igi1", "room": "XYZ789", "name": "Friends", "map": "1", "mode": "coop", "players": 1, "maxPlayers": 4 },
              { "title": "igi2", "address": "203.0.113.8", "port": 26001, "name": "Other game" },
              { "title": "igi1", "name": "Nowhere" }
            ] }
            """)!;

        List<BrowserServer> servers = ServerBrowser.ReadMasterList(list, "igi1");

        Assert.Equal(2, servers.Count);
        BrowserServer dedicated = servers[0];
        Assert.Equal("udp:203.0.113.7:26001", dedicated.Key);
        Assert.Equal("ABC234", dedicated.Room);
        Assert.Equal("eu", dedicated.Region);
        Assert.False(dedicated.Answered);
        Assert.Equal(BrowserServer.KeyOfRoom("XYZ789"), servers[1].Key);
        Assert.Null(servers[1].Address);
    }

    [Fact]
    public void A_room_the_master_stops_listing_leaves_the_list_and_a_saved_one_stays_saved()
    {
        static JsonNode Rooms(params string[] codes) => JsonNode.Parse(
            "{ \"servers\": [" + string.Join(',', codes.Select(c => $"{{ \"title\": \"igi1\", \"room\": \"{c}\", \"name\": \"Joe's game\", \"map\": \"1\", \"mode\": \"coop\", \"players\": 1, \"maxPlayers\": 4 }}")) + "] }")!;
        ServerBook book = new();
        using ServerBrowser browser = new(new ServerBrowserOptions(null, "igi1", Decode), null, book);

        browser.TakeMasterList(ServerBrowser.ReadMasterList(Rooms("AAAAAA", "BBBBBB", "CCCCCC"), "igi1"));
        Assert.Equal(3, browser.Servers.Count());
        browser.SetFavourite(browser.Servers.Single(s => s.Room == "CCCCCC"), true);

        // Two rooms closed and one opened: the list is what the master says now, the saved room kept as saved alone.
        browser.TakeMasterList(ServerBrowser.ReadMasterList(Rooms("BBBBBB", "DDDDDD"), "igi1"));
        Assert.Equal(["BBBBBB", "CCCCCC", "DDDDDD"], browser.Servers.Select(s => s.Room).Order());
        BrowserServer saved = browser.Servers.Single(s => s.Room == "CCCCCC");
        Assert.Equal(BrowserSource.Favourite, saved.Sources);
    }

    [Fact]
    public void A_line_of_another_protocol_is_not_compatible()
    {
        JsonNode list = JsonNode.Parse("""{ "servers": [ { "title": "igi1", "room": "XYZ789", "protocol": "0498fad9e47c6b7f", "name": "Old" }, { "title": "igi1", "room": "ABC234", "protocol": "7478398C138C41A2", "name": "Ours" } ] }""")!;
        List<BrowserServer> servers = ServerBrowser.ReadMasterList(list, "igi1", "7478398c138c41a2");
        Assert.False(servers[0].Info.Compatible);
        Assert.True(servers[1].Info.Compatible);
    }

    [Fact]
    public void A_udp_key_reads_back_to_its_address()
    {
        Assert.True(NetAddress.TryParseIp("192.168.1.20", 26005, out NetAddress v4));
        Assert.True(ServerBrowser.TryAddressOf(BrowserServer.KeyOf(v4), out NetAddress back));
        Assert.Equal(v4, back);

        Assert.True(NetAddress.TryParseIp("2001:db8::1", 26001, out NetAddress v6));
        Assert.True(ServerBrowser.TryAddressOf(BrowserServer.KeyOf(v6), out NetAddress back6));
        Assert.Equal(v6, back6);

        Assert.False(ServerBrowser.TryAddressOf("room:ABC234", out _));
        Assert.False(NetAddress.TryParseIp("not an address", 26001, out _));
    }

    [Fact]
    public void A_favourite_is_asked_after_and_its_answer_fills_its_line_with_a_ping()
    {
        LoopbackNetwork network = new();
        double now = 0;
        using NetServer server = new(network.Open(26001), new NetServerOptions { ProtocolId = 7, Info = _ => "Desert|3|16"u8.ToArray() });
        ServerBook book = new();
        book.SetFavourite(BrowserServer.KeyOf(server.LocalAddress), "Desert (last seen)", true);
        using ServerBrowser browser = new(new ServerBrowserOptions(null, "igi1", Decode), new NetQueryClient(network.Open(), 7), book);

        browser.Refresh(now);
        BrowserServer before = Assert.Single(browser.Servers);
        Assert.Equal("Desert (last seen)", before.Info.Name);
        Assert.False(before.Answered);

        now += 0.04;
        server.Update(now);
        Assert.True(browser.Update(now));

        BrowserServer line = Assert.Single(browser.Servers);
        Assert.Equal("Desert", line.Info.Name);
        Assert.Equal(3, line.Info.Players);
        Assert.True(line.Answered);
        Assert.True(line.Sources.HasFlag(BrowserSource.Favourite));
    }

    [Fact]
    public void The_filter_hides_what_was_asked_and_puts_the_nearest_first_with_the_silent_last()
    {
        static BrowserServer Line(string name, int players, int max, double? ping, bool passworded = false, string mode = "tdm") =>
            new($"room:{name}", null, name, new BrowserInfo("igi1", name, "1", mode, players, max, passworded, "v", true), "eu", BrowserSource.Internet, ping);

        BrowserServer[] all =
        [
            Line("far", 2, 16, 0.2),
            Line("near", 2, 16, 0.02),
            Line("silent", 2, 16, null),
            Line("full", 16, 16, 0.01),
            Line("locked", 2, 16, 0.01, passworded: true),
            Line("empty", 0, 16, 0.01),
            Line("sd", 2, 16, 0.01, mode: "sd"),
        ];

        BrowserFilter filter = new() { HideFull = true, HidePassworded = true, HideEmpty = true, Mode = "tdm" };
        Assert.Equal(["near", "far", "silent"], filter.Apply(all).Select(s => s.Info.Name));
        Assert.Equal(["near", "silent"], (filter with { MaxPing = 0.1 }).Apply(all).Select(s => s.Info.Name));
        Assert.Equal(["far"], (filter with { Text = "FA" }).Apply(all).Select(s => s.Info.Name));
    }

    [Fact]
    public void Quick_play_picks_a_near_game_with_people_in_it_over_an_empty_one_and_never_a_full_or_locked_one()
    {
        static BrowserServer Line(string name, int players, double? ping, bool passworded = false, bool compatible = true, string mode = "tdm") =>
            new($"room:{name}", null, name, new BrowserInfo("igi1", name, "1", mode, players, 16, passworded, "v", compatible), "", BrowserSource.Internet, ping);

        BrowserServer[] all =
        [
            Line("empty-near", 0, 0.010),
            Line("busy", 6, 0.060),
            Line("full", 16, 0.005),
            Line("locked", 8, 0.005, passworded: true),
            Line("old", 8, 0.005, compatible: false),
            Line("sd", 8, 0.020, mode: "sd"),
        ];

        Assert.Equal("busy", BrowserFilter.QuickPlay(all, "tdm")?.Info.Name);
        Assert.Equal("sd", BrowserFilter.QuickPlay(all)?.Info.Name);
        Assert.Null(BrowserFilter.QuickPlay(all, "ctf"));
    }

    [Fact]
    public void The_book_keeps_favourites_and_recent_servers_across_runs_with_the_newest_first()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openigi-book-{Guid.NewGuid():N}.json");
        try
        {
            ServerBook book = new(path);
            book.SetFavourite("udp:1.2.3.4:26001", "A", true);
            for (int i = 0; i < ServerBook.HistoryLength + 3; i++)
            {
                book.Played($"room:R{i}", $"R{i}");
            }

            ServerBook again = new(path);
            Assert.True(again.IsFavourite("udp:1.2.3.4:26001"));
            Assert.Equal(ServerBook.HistoryLength, again.History.Count);
            Assert.Equal($"room:R{ServerBook.HistoryLength + 2}", again.History[0].Key);

            again.SetFavourite("udp:1.2.3.4:26001", "A", false);
            Assert.False(new ServerBook(path).IsFavourite("udp:1.2.3.4:26001"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

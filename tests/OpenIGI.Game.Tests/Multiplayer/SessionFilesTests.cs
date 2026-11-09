using System.Security.Cryptography;
using System.Text;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using Xunit;
using ToyWorld = OpenIGI.Game.Tests.Multiplayer.SessionLoopbackTests.ToyWorld;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>
/// A server's files made a client's: which files count, the list and its checks, the fetch over a sync connection, and
/// everything a hostile server could send refused.
/// </summary>
public sealed class SessionFilesTests : IDisposable
{
    private const int TickRate = 30;
    private const ulong Protocol = 0x46494C4553594E43UL;
    private const string Objects = "missions/location0/level1/objects.qvm";
    private const string Weapons = "weapons/weapons.qvm";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"openigi-files-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    [Theory]
    [InlineData("missions/location0/level1/objects.qvm", true)]
    [InlineData("missions/location0/level1/ai/1350.qvm", true)]
    [InlineData("missions/location0/level1/terrain/terrain.cmd", true)]
    [InlineData("missions/location0/level1/models/level1.res", true)]
    [InlineData("weapons/weapons.qvm", true)]
    [InlineData("common/anims/000.iff", true)]
    [InlineData("missions/location0/level1/textures/level1.res", false)]
    [InlineData("missions/location0/level1/terrain/terrain.tex", false)]
    [InlineData("missions/location0/level1/sounds/sounds.res", false)]
    [InlineData("missions/location0/level1/lightmaps/lightmaps.res", false)]
    [InlineData("missions/location0/level1/objects_lightmap_tmp.qsc", false)]
    [InlineData("weapons/evil.dll", false)]
    [InlineData("menusystem/menusystem.res", false)]
    [InlineData("igi.exe", false)]
    public void Only_the_files_a_level_plays_from_are_compared(string path, bool gameplay) =>
        Assert.Equal(gameplay, GameplayFiles.IsGameplay("igi1", path));

    [Theory]
    [InlineData("../igi.exe")]
    [InlineData("missions/location0/level1/../../../x.qvm")]
    [InlineData("/etc/passwd")]
    [InlineData("c:/windows/x.qvm")]
    [InlineData("missions//objects.qvm")]
    [InlineData("Missions/Location0/level1/objects.qvm")]
    [InlineData("missions\\location0\\level1\\objects.qvm")]
    public void A_list_naming_a_path_outside_the_installation_is_no_list(string path)
    {
        BitWriter writer = new();
        new SessionFileList("1", [new SessionFile(path, 4, new byte[32])]).Write(writer);
        BitReader reader = new(writer.ToArray());
        reader.ReadBits(8);

        Assert.Null(SessionFileList.Read(ref reader));
    }

    [Fact]
    public void A_list_reads_back_as_it_was_written()
    {
        SessionFileList list = new("1", [File(Objects, "LOOP objects"), File(Weapons, "LOOP weapons")]);
        BitWriter writer = new();
        list.Write(writer);
        BitReader reader = new(writer.ToArray());
        Assert.Equal(SessionMessage.Files, (SessionMessage)reader.ReadBits(8));

        SessionFileList read = SessionFileList.Read(ref reader)!;
        Assert.Equal("1", read.Map);
        Assert.Equal([Objects, Weapons], read.Files.Select(f => f.Path));
        Assert.True(read.Files[0].Matches(Bytes("LOOP objects")));
    }

    [Fact]
    public void A_server_lists_only_the_maps_level_and_what_every_level_plays_from()
    {
        MemorySource server = new()
        {
            [Objects] = Bytes("LOOP objects"),
            ["missions/location0/level2/objects.qvm"] = Bytes("LOOP other map"),
            ["missions/location0/level1/textures/level1.res"] = Bytes("ILFF textures"),
            [Weapons] = Bytes("LOOP weapons"),
        };

        SessionFileList list = GameplayFiles.Build("1", server);

        Assert.Equal([Objects, Weapons], list.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_client_whose_level_differs_fetches_the_servers_over_a_sync_connection_and_reads_it_in_place()
    {
        byte[] theirs = Bytes("LOOP the server's objects, which are longer than the client's");
        MemorySource server = new() { [Objects] = theirs, [Weapons] = Bytes("LOOP weapons") };
        MemorySource client = new() { [Objects] = Bytes("LOOP edited objects"), [Weapons] = Bytes("LOOP weapons") };
        using Room room = new(server);
        SessionFileSync sync = new(client, _cache);

        ClientSession session = room.Sync();
        room.Run(1, () =>
        {
            if (session.TakeFileList() is { } list)
            {
                sync.Accept(list, session.WantFiles);
            }

            sync.Take(session.TakeFilePieces());
        });

        Assert.True(sync.Settled, sync.Failure);
        string pinned = Assert.Single(sync.Pins, p => p.Key == Objects).Value;
        Assert.Equal(theirs, System.IO.File.ReadAllBytes(pinned));
        Assert.DoesNotContain(Weapons, sync.Pins.Keys);
        Assert.Null(session.EntityId);
        Assert.Empty(room.Server.Clients);
        Assert.True(room.Server.FileBytesSent >= theirs.Length);
    }

    [Fact]
    public void A_list_naming_a_file_no_level_plays_from_stops_the_join()
    {
        MemorySource client = new() { [Objects] = Bytes("LOOP objects"), ["weapons/evil.dll"] = Bytes("MZ") };
        SessionFileSync sync = new(client, _cache);

        sync.Accept(new SessionFileList("1", [File("weapons/evil.dll", "MZ evil")]), (_, _) => Assert.Fail("nothing may be asked for"));

        Assert.Contains("isn't a file map 1 plays from", sync.Failure, StringComparison.Ordinal);
        Assert.Empty(sync.Pins);
    }

    [Fact]
    public void A_list_naming_another_maps_level_stops_the_join()
    {
        const string other = "missions/location0/level2/objects.qvm";
        SessionFileSync sync = new(new MemorySource { [other] = Bytes("LOOP mine") }, _cache);

        sync.Accept(new SessionFileList("1", [File(other, "LOOP theirs")]), (_, _) => Assert.Fail("nothing may be asked for"));

        Assert.NotNull(sync.Failure);
    }

    [Fact]
    public void A_server_cannot_add_a_file_the_game_does_not_have()
    {
        SessionFileSync sync = new(new MemorySource(), _cache);

        sync.Accept(new SessionFileList("1", [File(Objects, "LOOP new")]), (_, _) => Assert.Fail("nothing may be asked for"));

        Assert.Contains("doesn't have", sync.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_of_another_kind_in_place_of_the_games_own_is_refused()
    {
        SessionFileSync sync = new(new MemorySource { [Objects] = Bytes("LOOP mine") }, _cache);
        byte[] disguised = Bytes("MZ\u0090\u0000 a program, not a script");
        SessionFile listed = new(Objects, disguised.Length, SHA256.HashData(disguised));
        sync.Accept(new SessionFileList("1", [listed]), (_, _) => { });

        sync.Take([new FilePiece("1", 0, 0, disguised)]);

        Assert.Contains("different kind", sync.Failure, StringComparison.Ordinal);
        Assert.Empty(sync.Pins);
        Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
    }

    [Fact]
    public void A_file_whose_bytes_are_not_the_ones_listed_is_refused()
    {
        SessionFileSync sync = new(new MemorySource { [Objects] = Bytes("LOOP mine") }, _cache);
        sync.Accept(new SessionFileList("1", [File(Objects, "LOOP listed")]), (_, _) => { });

        sync.Take([new FilePiece("1", 0, 0, Bytes("LOOP sentxx"))]);

        Assert.Contains("didn't arrive whole", sync.Failure, StringComparison.Ordinal);
        Assert.Empty(sync.Pins);
    }

    [Fact]
    public void A_file_kept_from_before_is_not_fetched_again()
    {
        MemorySource client = new() { [Objects] = Bytes("LOOP mine") };
        SessionFile listed = File(Objects, "LOOP theirs");
        SessionFileSync first = new(client, _cache);
        first.Accept(new SessionFileList("1", [listed]), (_, _) => { });
        first.Take([new FilePiece("1", 0, 0, Bytes("LOOP theirs"))]);
        Assert.True(first.Settled);

        SessionFileSync again = new(client, _cache);
        again.Accept(new SessionFileList("1", [listed]), (_, _) => Assert.Fail("a kept file is not asked for"));

        Assert.True(again.Settled);
        Assert.Contains(Objects, again.Pins.Keys);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static SessionFile File(string path, string text) => new(path, Bytes(text).Length, SHA256.HashData(Bytes(text)));

    /// <summary>An installation in memory.</summary>
    private sealed class MemorySource : Dictionary<string, byte[]>, ISessionFileSource
    {
        public string TitleId => "igi1";

        public IEnumerable<string> ListFiles(string directory) => Keys.Where(k => k.StartsWith(directory + "/", StringComparison.Ordinal));

        public byte[]? Read(string path) => TryGetValue(path, out byte[]? data) ? data : null;
    }

    /// <summary>A toy world's server on mission 1 with its files, and the sync connections that come to it.</summary>
    private sealed class Room : IDisposable
    {
        private readonly LoopbackNetwork _network = new();
        private readonly NetServer _net;
        private readonly List<NetClient> _lines = [];
        private readonly List<ClientSession> _sessions = [];
        private double _now;
        private long _tick;

        public Room(ISessionFileSource files)
        {
            _net = new NetServer(_network.Open(26001), new NetServerOptions { ProtocolId = Protocol });
            Server = new ServerSession(_net, new ToyWorld(), TickRate, "1", protocolId: Protocol) { Files = files };
        }

        public ServerSession Server { get; }

        public ClientSession Sync()
        {
            NetClient net = new(_network.Open(), Protocol);
            ClientSession session = new(net, ToyWorld.Channels, null, Protocol);
            session.Connect(_net.LocalAddress, new JoinRequest("files", "test", string.Empty, Sync: true), _now);
            _lines.Add(net);
            _sessions.Add(session);
            return session;
        }

        public void Run(double seconds, Action afterReceive)
        {
            for (int i = 0; i < seconds * TickRate; i++, _tick++)
            {
                _now = _tick / (double)TickRate;
                Server.Receive(_now, _tick);
                Server.Send(_now, _tick);
                foreach (ClientSession session in _sessions)
                {
                    session.Poll(_now);
                    session.Receive(_now);
                    afterReceive();
                    session.Flush(_now);
                }
            }
        }

        public void Dispose()
        {
            foreach (NetClient line in _lines)
            {
                line.Dispose();
            }

            _net.Dispose();
        }
    }
}

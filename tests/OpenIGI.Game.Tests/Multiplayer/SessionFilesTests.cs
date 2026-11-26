using System.Security.Cryptography;
using System.Text;
using OpenIGI.Game.Igi1.Multiplayer;
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

    /// <summary>Project I.G.I.'s rules, with mission n's folder <c>missions/location0/level&lt;n&gt;</c> as its table names it.</summary>
    internal static readonly GameplayFileRules Igi1 = new(
        Igi1GameplayFiles.Layout,
        static map => int.TryParse(map, System.Globalization.CultureInfo.InvariantCulture, out int n) ? [$"missions/location0/level{n}", "missions/location0/common"] : null);

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
        Assert.Equal(gameplay, Igi1.IsGameplay(path));

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

        SessionFileList list = Igi1.Build("1", server);

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

            sync.TakeTables(session.TakeFileTables(), session.WantChunks);
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

        sync.Take([new FilePiece("1", 0, 0, false, disguised)]);

        Assert.Contains("different kind", sync.Failure, StringComparison.Ordinal);
        Assert.Empty(sync.Pins);
        Assert.False(Directory.Exists(_cache) && Directory.EnumerateFiles(_cache).Any());
    }

    [Fact]
    public void A_file_whose_bytes_are_not_the_ones_listed_is_refused()
    {
        SessionFileSync sync = new(new MemorySource { [Objects] = Bytes("LOOP mine") }, _cache);
        sync.Accept(new SessionFileList("1", [File(Objects, "LOOP listed")]), (_, _) => { });

        sync.Take([new FilePiece("1", 0, 0, false, Bytes("LOOP sentxx"))]);

        Assert.Contains("didn't arrive whole", sync.Failure, StringComparison.Ordinal);
        Assert.Empty(sync.Pins);
    }

    [Fact]
    public void A_large_file_differing_in_one_place_fetches_only_the_pieces_around_it()
    {
        // An archive of a hundred "meshes"; the server's has one changed and one inserted in the middle.
        Random random = new(7);
        List<byte[]> meshes = [.. Enumerable.Range(0, 100).Select(_ => { byte[] m = new byte[12_000]; random.NextBytes(m); return m; })];
        byte[] mine = Archive(meshes);
        byte[] changed = new byte[12_000];
        random.NextBytes(changed);
        byte[] inserted = new byte[5_000];
        random.NextBytes(inserted);
        List<byte[]> theirMeshes = [.. meshes];
        theirMeshes[30] = changed;
        theirMeshes.Insert(70, inserted);
        byte[] theirs = Archive(theirMeshes);

        const string models = "missions/location0/level1/models/level1.res";
        MemorySource server = new() { [models] = theirs };
        MemorySource client = new() { [models] = mine };
        using Room room = new(server);
        SessionFileSync sync = new(client, _cache);
        ClientSession session = room.Sync();
        room.Run(4, () =>
        {
            if (session.TakeFileList() is { } list)
            {
                sync.Accept(list, session.WantFiles);
            }

            sync.TakeTables(session.TakeFileTables(), session.WantChunks);
            sync.Take(session.TakeFilePieces());
        });

        Assert.True(sync.Settled, sync.Failure);
        Assert.Equal(theirs, System.IO.File.ReadAllBytes(sync.Pins[models]));

        // The two changes and the pieces they straddle, not the 1.2 MB archive: under a tenth of it on the line.
        Assert.True(room.Server.FileBytesSent < theirs.Length / 10, $"{room.Server.FileBytesSent} bytes sent for a {theirs.Length}-byte file");
        Assert.True(sync.BytesReused > theirs.Length * 8 / 10, $"{sync.BytesReused} bytes reused");
    }

    [Fact]
    public void A_file_the_client_lacks_wholly_comes_deflated()
    {
        const string graph = "missions/location0/level1/graphs/graph1.dat";
        byte[] theirs = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("GRPH node 1 2 3 4 link 5 6;", 8_000)));
        MemorySource server = new() { [graph] = theirs };
        MemorySource client = new() { [graph] = Bytes("GRPH an old graph of another shape") };
        using Room room = new(server);
        SessionFileSync sync = new(client, _cache);
        ClientSession session = room.Sync();
        room.Run(4, () =>
        {
            if (session.TakeFileList() is { } list)
            {
                sync.Accept(list, session.WantFiles);
            }

            sync.TakeTables(session.TakeFileTables(), session.WantChunks);
            sync.Take(session.TakeFilePieces());
        });

        Assert.True(sync.Settled, sync.Failure);
        Assert.Equal(theirs, System.IO.File.ReadAllBytes(sync.Pins[graph]));
        Assert.True(room.Server.FileBytesSent < theirs.Length / 5, $"{room.Server.FileBytesSent} bytes sent for {theirs.Length}");
    }

    [Fact]
    public void A_table_that_does_not_add_up_to_the_file_is_refused()
    {
        const string models = "missions/location0/level1/models/level1.res";
        byte[] mine = new byte[100_000];
        mine[0] = (byte)'I';
        SessionFileSync sync = new(new MemorySource { [models] = mine }, _cache);
        SessionFile listed = new(models, 200_000, new byte[32]);
        sync.Accept(new SessionFileList("1", [listed]), (_, _) => { });

        sync.TakeTables([new FileChunkTable("1", 0, [new ContentChunk(ContentChunks.MaxLength, 1)])], _ => Assert.Fail("nothing may be asked for"));

        Assert.Contains("doesn't add up", sync.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_that_inflates_past_its_length_is_refused()
    {
        byte[] bomb = ChunkCodec.Pack(new byte[ContentChunks.MaxLength]).Data;

        Assert.Null(ChunkCodec.Unpack(true, bomb, 100));
        Assert.Null(ChunkCodec.Unpack(true, [1, 2, 3, 4, 5], 100));
        Assert.Equal(new byte[ContentChunks.MaxLength], ChunkCodec.Unpack(true, bomb, ContentChunks.MaxLength));
    }

    [Fact]
    public void Pieces_are_cut_by_content_so_an_insertion_moves_them_rather_than_changing_them()
    {
        Random random = new(3);
        byte[] data = new byte[400_000];
        random.NextBytes(data);
        byte[] shifted = [.. data[..150_000], .. new byte[] { 1, 2, 3, 4, 5, 6, 7 }, .. data[150_000..]];

        IReadOnlyList<ContentChunk> before = ContentChunks.Cut(data);
        IReadOnlyList<ContentChunk> after = ContentChunks.Cut(shifted);

        Assert.Equal(before, ContentChunks.Cut(data));
        Assert.All(before, c => Assert.InRange(c.Length, 1, ContentChunks.MaxLength));
        Assert.Equal(data.Length, before.Sum(c => c.Length));
        Assert.True(after.Except(before).Count() <= 2, $"{after.Except(before).Count()} pieces changed");
    }

    // Meshes one after another, as an archive holds them.
    private static byte[] Archive(IEnumerable<byte[]> meshes) => [.. "ILFF"u8.ToArray(), .. meshes.SelectMany(static m => m)];

    [Fact]
    public void A_file_kept_from_before_is_not_fetched_again()
    {
        MemorySource client = new() { [Objects] = Bytes("LOOP mine") };
        SessionFile listed = File(Objects, "LOOP theirs");
        SessionFileSync first = new(client, _cache);
        first.Accept(new SessionFileList("1", [listed]), (_, _) => { });
        first.Take([new FilePiece("1", 0, 0, false, Bytes("LOOP theirs"))]);
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
        public GameplayFileRules Rules => Igi1;

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

using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;
using OpenIGI.Net.Identity;
using Xunit;
using ToyWorld = OpenIGI.Game.Tests.Multiplayer.SessionLoopbackTests.ToyWorld;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Who a player is to a server: its key, not its name -- for coming back, for being put out, and for sharing a name.</summary>
public sealed class PlayerIdentityTests
{
    private const int TickRate = 30;
    private const ulong Protocol = 0x4944454E54495459UL;

    [Fact]
    public async Task A_signature_is_good_for_its_key_and_its_challenge_only()
    {
        using EcdsaPlayerIdentity one = EcdsaPlayerIdentity.CreateEphemeral();
        using EcdsaPlayerIdentity other = EcdsaPlayerIdentity.CreateEphemeral();
        byte[] proof = PlayerIdentities.ProofOf(Protocol, new byte[16]);
        byte[] signature = await one.SignAsync(proof);

        Assert.True(EcdsaPlayerIdentity.Verify(one.PublicKey, proof, signature));
        Assert.False(EcdsaPlayerIdentity.Verify(other.PublicKey, proof, signature));
        Assert.False(EcdsaPlayerIdentity.Verify(one.PublicKey, PlayerIdentities.ProofOf(Protocol + 1, new byte[16]), signature));
        Assert.NotEqual(one.Id, other.Id);
        Assert.Equal(16, one.Id.Length);
    }

    [Fact]
    public void A_key_kept_in_a_file_is_the_same_player_next_time()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openigi-identity-{Guid.NewGuid():N}.key");
        try
        {
            string first;
            using (EcdsaPlayerIdentity made = EcdsaPlayerIdentity.LoadOrCreate(path))
            {
                first = made.Id;
            }

            using EcdsaPlayerIdentity loaded = EcdsaPlayerIdentity.LoadOrCreate(path);
            Assert.Equal(first, loaded.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Two_players_of_one_name_are_told_apart()
    {
        using Room room = new();
        Room.Player a = room.Join("Joe");
        Room.Player b = room.Join("Joe");
        room.Run(2);

        Assert.NotNull(a.Session.EntityId);
        Assert.NotNull(b.Session.EntityId);
        string[] names = [.. room.Server.Clients.Select(room.Server.NameOf).OfType<string>()];
        Assert.Equal(["Joe", "Joe (2)"], names.Order());
    }

    [Fact]
    public void A_player_who_drops_comes_back_to_its_own_body_and_one_of_its_name_does_not()
    {
        using Room room = new();
        room.World.KeepsTheAway = true;
        using EcdsaPlayerIdentity anya = EcdsaPlayerIdentity.CreateEphemeral();
        Room.Player first = room.Join("Anya", anya);
        room.Run(2);
        int body = first.Session.EntityId!.Value;

        first.Drop();
        room.Run(1);

        // Someone else under the same name: a new body, not hers.
        Room.Player impostor = room.Join("Anya");
        room.Run(2);
        Assert.NotEqual(body, impostor.Session.EntityId);

        // She, with her key: her body back.
        Room.Player back = room.Join("Anya", anya);
        room.Run(2);
        Assert.Equal(body, back.Session.EntityId);
    }

    [Fact]
    public void Two_games_on_one_key_that_both_leave_and_come_back_leave_nobody_behind_on_the_scoreboard()
    {
        // Two copies of the game on one machine share its key. Both gone, the second kept aside overwrote the first, which
        // stayed in the match for good: a player with no line on the board at nought ping, alive.
        using Room room = new();
        room.World.KeepsTheAway = true;
        using EcdsaPlayerIdentity machine = EcdsaPlayerIdentity.CreateEphemeral();
        Room.Player jones = room.Join("Jones", machine);
        room.Run(2);
        Room.Player anya = room.Join("Anya", machine);
        room.Run(2);
        jones.Drop();
        room.Run(1);
        anya.Drop();
        room.Run(1);

        // While both are away the board shows nobody: a player kept for its return is not in the game.
        Assert.Empty(room.Server.CurrentScoreboard().Lines);

        room.Join("Jones", machine);
        room.Run(2);
        room.Join("Anya", machine);
        room.Run(2);
        Assert.Equal(2, room.Server.CurrentScoreboard().Lines.Length);
        Assert.Equal(2, room.Server.Match.Players.Count);
    }

    [Fact]
    public void Players_kept_for_their_return_do_not_count_on_their_side_when_a_player_switches_or_one_joins()
    {
        // Live on the EU server: two of a side dropped and were kept for their return, so the side looked full to the
        // team menu's switch (and to a newcomer's side) though nobody was playing on it.
        using Room room = new();
        room.World.KeepsTheAway = true;
        room.Server.Mode = GameModes.For("tdm", room.World, TickRate);
        EcdsaPlayerIdentity[] keys = [.. Enumerable.Range(0, 5).Select(_ => EcdsaPlayerIdentity.CreateEphemeral())];
        try
        {
            Room.Player[] players = [.. Enumerable.Range(0, 4).Select(i => room.Join($"p{i}", keys[i]))];
            room.Run(2);
            Assert.Equal(2, room.Server.Match.Players.Values.Count(p => p.Team == TeamRoster.Good));

            // Both of one side drop and are kept.
            MatchPlayer[] good = [.. room.Server.Match.Players.Values.Where(p => p.Team == TeamRoster.Good)];
            foreach (MatchPlayer gone in good)
            {
                players[int.Parse(gone.Name[1..], System.Globalization.CultureInfo.InvariantCulture)].Drop();
            }

            room.Run(1);
            Assert.Equal(2, room.Server.Match.Players.Keys.Count(k => k < 0));

            // A newcomer goes to the side nobody is playing on (counting the kept, the sides were level: a coin toss).
            room.Join("p4", keys[4]);
            room.Run(2);
            Assert.Equal(TeamRoster.Good, room.Server.Match.Players.Values.Single(p => p.Name == "p4").Team);

            // One playing against two: one of the two may go over (counting the kept, that side held three).
            MatchPlayer mover = room.Server.Match.Players.Values.First(p => p.Slot >= 0 && p.Team == TeamRoster.Bad);
            Assert.StartsWith("You are on ", room.Server.ChooseTeam(mover.Slot, "igi"), StringComparison.Ordinal);
            Assert.Equal(TeamRoster.Good, mover.Team);
        }
        finally
        {
            foreach (EcdsaPlayerIdentity key in keys)
            {
                key.Dispose();
            }
        }
    }

    [Fact]
    public void A_player_put_out_stays_out_but_another_of_its_name_comes_in()
    {
        using Room room = new();
        using EcdsaPlayerIdentity troll = EcdsaPlayerIdentity.CreateEphemeral();
        room.Join("Joe", troll);
        room.Run(2);

        room.Server.Kick(room.Server.Clients.Single());
        room.Run(1);
        Assert.True(room.Server.IsBarred("Joe"));

        Room.Player again = room.Join("Joe", troll);
        Room.Player someoneElse = room.Join("Joe");
        room.Run(3);

        Assert.Null(again.Session.EntityId);
        Assert.Contains("removed", again.Session.EndReason ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(someoneElse.Session.EntityId);
    }

    [Fact]
    public void A_client_whose_signature_is_wrong_is_never_let_in()
    {
        using Room room = new();
        Room.Player forger = room.Join("Mallory", new ForgedIdentity());
        room.Run(3);

        Assert.Null(forger.Session.EntityId);
        Assert.Empty(room.Server.Clients);
        Assert.Contains("identity", forger.Session.EndReason ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Signs with one key and shows another: what an impostor of a known player would send.</summary>
    private sealed class ForgedIdentity : IPlayerIdentity
    {
        private readonly EcdsaPlayerIdentity _signer = EcdsaPlayerIdentity.CreateEphemeral();
        private readonly EcdsaPlayerIdentity _shown = EcdsaPlayerIdentity.CreateEphemeral();

        public bool IsReady => true;

        public byte[] PublicKey => _shown.PublicKey;

        public Task<byte[]> SignAsync(byte[] data) => _signer.SignAsync(data);
    }

    /// <summary>A toy world's server and the clients that join it, all on one clock.</summary>
    private sealed class Room : IDisposable
    {
        private readonly LoopbackNetwork _network = new();
        private readonly NetServer _net;
        private readonly List<Player> _players = [];
        private double _now;
        private long _tick;

        public Room()
        {
            _net = new NetServer(_network.Open(26001), new NetServerOptions { ProtocolId = Protocol });
            Server = new ServerSession(_net, World, TickRate, "toy", protocolId: Protocol);
        }

        public ToyWorld World { get; } = new();

        public ServerSession Server { get; }

        public Player Join(string name, IPlayerIdentity? identity = null)
        {
            NetClient net = new(_network.Open(), Protocol);
            Player player = new(net, new ClientSession(net, ToyWorld.Channels, identity, Protocol));
            player.Session.Connect(_net.LocalAddress, new JoinRequest(name, "test", string.Empty), _now);
            _players.Add(player);
            return player;
        }

        public void Run(double seconds)
        {
            float[] idle = new float[ToyWorld.Channels];
            for (int i = 0; i < seconds * TickRate; i++, _tick++)
            {
                _now = _tick / (double)TickRate;
                Server.Receive(_now, _tick);
                World.Step(_tick);
                Server.Send(_now, _tick);
                foreach (Player player in _players.Where(p => !p.Dropped))
                {
                    player.Session.Receive(_now);
                    player.Session.SendCommand(idle, _now);
                    player.Session.Flush(_now);
                }
            }
        }

        public void Dispose()
        {
            foreach (Player player in _players)
            {
                player.Net.Dispose();
            }

            _net.Dispose();
        }

        public sealed class Player(NetClient net, ClientSession session)
        {
            public NetClient Net { get; } = net;

            public ClientSession Session { get; } = session;

            public bool Dropped { get; private set; }

            // The line goes dead without a word: the server hears of it as a goodbye, as when a game closes.
            public void Drop()
            {
                Net.Disconnect("closed", 0);
                Dropped = true;
            }
        }
    }
}

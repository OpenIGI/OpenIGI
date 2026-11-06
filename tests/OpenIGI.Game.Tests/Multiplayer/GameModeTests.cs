using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The modes' rules on their own: who scores, who comes back, and when a match is decided.</summary>
public sealed class GameModeTests
{
    private const int TickRate = 30;

    [Fact]
    public void A_free_for_all_kill_scores_the_killer_and_the_first_to_the_limit_wins()
    {
        Host host = new(teams: [0, 0, 0]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { ScoreLimit = 3, IntermissionSeconds = 5 });
        mode.Start(host, 0);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);

        long tick = 1;
        for (int kill = 0; kill < 3; kill++, tick += TickRate * 5)
        {
            mode.Tick(host, tick, [new WorldDeath(Victim: 11, Killer: 10)]);
        }

        // Same team in the world, but nobody's team mate here: three kills, no fines.
        Assert.Equal(3, host.Match.Players[0].Kills);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        Assert.Equal(0, mode.Flow.Winner);
    }

    [Fact]
    public void The_dead_come_back_after_the_delay_and_not_before()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { RespawnSeconds = 3 });
        mode.Start(host, 0);
        host.Respawned.Clear();
        mode.Tick(host, TickRate, [new WorldDeath(Victim: 11, Killer: 10)]);
        mode.Tick(host, TickRate * 3, []);
        Assert.Empty(host.Respawned);
        mode.Tick(host, TickRate * 4, []);
        Assert.Equal([1], host.Respawned);
    }

    [Fact]
    public void A_team_scores_its_kills_of_the_other_and_is_fined_for_its_own()
    {
        Host host = new(teams: [0, 0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { ScoreLimit = 0 });
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 12, Killer: 10), new WorldDeath(Victim: 12, Killer: 11)]);
        mode.Tick(host, 2, [new WorldDeath(Victim: 11, Killer: 10)]);
        Assert.Equal(1, host.Match.ScoreOf(0));
        Assert.Equal(0, host.Match.ScoreOf(1));
    }

    [Fact]
    public void The_clock_decides_for_the_leader_and_a_level_score_is_a_draw()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { ScoreLimit = 0, TimeLimitMinutes = 1 });
        mode.Start(host, 0);
        mode.Tick(host, TickRate * 60, []);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        Assert.Equal(-1, mode.Flow.Winner);
    }

    [Fact]
    public void Warmup_scores_nothing_and_the_match_begins_from_nothing_with_everyone_put_back()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { WarmupSeconds = 10 });
        mode.Start(host, 0);
        Assert.Equal(MatchPhase.Warmup, mode.Flow.Phase);
        mode.Tick(host, 5, [new WorldDeath(Victim: 11, Killer: 10)]);
        host.Respawned.Clear();
        mode.Tick(host, TickRate * 10, []);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
        Assert.Equal(0, host.Match.Players[0].Kills);
        Assert.Equal([0, 1], host.Respawned.Order());
    }

    [Fact]
    public void After_the_intermission_the_same_map_is_played_again_without_a_rotation()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { ScoreLimit = 1, IntermissionSeconds = 5 });
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 11, Killer: 10)]);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        mode.Tick(host, 1 + (TickRate * 5), []);
        Assert.Equal(1, mode.MatchesPlayed);
        Assert.False(mode.IsMapOver);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
        Assert.Equal(0, host.Match.Players[0].Kills);
    }

    [Fact]
    public void Co_op_keeps_the_dead_down_until_the_world_says_they_were_revived()
    {
        Host host = new(teams: [0, 0]) { HoldsTheDead = true };
        IGameMode mode = GameModes.For(GameModes.FreeForAll, host.World, TickRate);
        Assert.IsType<CoopMode>(mode);
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 11, Killer: -1)]);
        mode.Tick(host, TickRate * 60, []);
        Assert.False(host.Match.Players[1].IsAlive);
        host.Revive(1);
        mode.Tick(host, (TickRate * 60) + 1, []);
        Assert.True(host.Match.Players[1].IsAlive);
        Assert.Empty(host.Respawned);
    }

    [Fact]
    public void A_newcomer_to_team_deathmatch_joins_the_smaller_team_and_free_for_all_has_none()
    {
        Host host = new(teams: [0, 0, 1]);
        Assert.Equal(1, new DeathmatchMode(TickRate, teams: true).TeamFor(host, 7));
        Assert.Equal(0, new DeathmatchMode(TickRate, teams: false).TeamFor(host, 7));
    }

    [Fact]
    public void The_match_state_reads_back_as_it_was_written()
    {
        MatchFlow flow = new(TickRate);
        flow.Enter(MatchPhase.RoundOver, 100, 7.5, winner: 1);
        OpenIGI.Net.BitWriter writer = new();
        flow.Write(writer, GameModes.TeamDeathmatch, 130);
        OpenIGI.Net.BitReader reader = new(writer.AsSpan());
        MatchState read = MatchFlow.Read(ref reader);
        Assert.Equal(new MatchState(GameModes.TeamDeathmatch, MatchPhase.RoundOver, 1, 1, 6.5), read);
    }

    // A match of players in slots 0.., each entity 10 + slot, on the teams given.
    private sealed class Host : IModeHost
    {
        private readonly FakeWorld _world = new();

        public Host(int[] teams)
        {
            Match = new Match(new MultiplayerRules());
            for (int slot = 0; slot < teams.Length; slot++)
            {
                Match.Join(slot, $"p{slot}", teams[slot]).IsAlive = true;
            }

            PlayerSlots = [.. Enumerable.Range(0, teams.Length)];
        }

        public bool HoldsTheDead { get => _world.Holds; init => _world.Holds = value; }

        public Match Match { get; }

        public IReplicatedWorld World => _world;

        public int TickRate => GameModeTests.TickRate;

        public IReadOnlyList<int> PlayerSlots { get; }

        public IReadOnlyList<int> ClientSlots => PlayerSlots;

        public List<int> Respawned { get; } = [];

        public int SlotOf(int entity) => entity >= 10 ? entity - 10 : -1;

        public void Respawn(int slot)
        {
            Respawned.Add(slot);
            Match.Players[slot].IsAlive = true;
            Match.Players[slot].DiedAt = null;
        }

        public void Revive(int slot) => _world.Revived.Add(slot);
    }

    private sealed class FakeWorld : IReplicatedWorld
    {
        public bool Holds { get; set; }

        public List<int> Revived { get; } = [];

        public bool HoldsTheDead => Holds;

        public int ChannelCount => 1;

        public long CurrentTick => 0;

        public LagCompensation? LagCompensation { get; set; }

        public IReadOnlyList<IRoundObjective> RoundObjectives => [];

        public int CurrentObjective { get; set; }

        public IReadOnlyList<int> TakeRevivals()
        {
            int[] revived = [.. Revived];
            Revived.Clear();
            return revived;
        }

        public int? AddPlayer(int slot, string name) => slot;

        public void RemovePlayer(int slot)
        {
        }

        public UserCommandBuffer? CommandsFor(int slot) => null;

        public Snapshot Capture(long tick) => new(tick, []);

        public int TeamOf(int slot) => 0;

        public IReadOnlyList<WorldDeath> TakeDeaths() => [];

        public void Respawn(int slot)
        {
        }
    }
}

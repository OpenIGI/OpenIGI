using OpenIGI.Core.Titles;
using OpenIGI.Game.Player;
using OpenIGI.Host;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// A mission played from the same inputs plays the same whether it is drawn or only simulated, as the dedicated server and the
/// replay checker run it: the level's fingerprint (<see cref="GameHost.SimulationDigest"/>) agrees tick for tick.
/// </summary>
/// <remarks>
/// A replay is nothing but the inputs, so anything the simulation reads that the inputs do not carry -- what was drawn, whether
/// a sound could be heard, the presentation's own camera -- would make a run played again on the server part from the run the
/// player played. The first tick the two fingerprints differ on is the first such read, which is where this test points. The
/// inputs are held by level tick, not by frame, because a drawn run withholds its level while the lighting settles and one
/// only simulated does not.
/// </remarks>
[Collection(ParityGroup.Name)]
public sealed class DeterminismTests
{
    // Fifteen seconds of play.
    private const int Ticks = 450;

    // What the player holds from each level tick on: walk, walk and fire, stand and fire, crouch, then walk off to one side.
    private static readonly (int From, PlayerAction Held)[] Script =
    [
        (0, default),
        (30, PlayerAction.MoveUp),
        (120, PlayerAction.MoveUp | PlayerAction.Fire),
        (180, PlayerAction.Fire),
        (240, PlayerAction.Crouch),
        (300, PlayerAction.MoveLeft | PlayerAction.MoveUp),
        (390, PlayerAction.Fire),
    ];

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void Igi1_mission_plays_the_same_drawn_or_only_simulated(int mission) =>
        Compare(GameDataSource.Igi1, GameTitle.Igi1, mission);

    [RequiresTitleDataTheory("igi2")]
    [InlineData(21)]
    public void Igi2_mission_plays_the_same_drawn_or_only_simulated(int mission) =>
        Compare(GameDataSource.Igi2, GameTitle.Igi2, mission);

    private static void Compare(GameDataSource data, GameTitle title, int mission)
    {
        ulong[] drawn = Play(data, title, mission, simulationOnly: false);
        ulong[] simulated = Play(data, title, mission, simulationOnly: true);

        Assert.True(drawn.Length >= Ticks, $"The drawn run ran {drawn.Length} ticks of {Ticks}.");
        Assert.True(simulated.Length >= Ticks, $"The simulated run ran {simulated.Length} ticks of {Ticks}.");

        // A fingerprint that never moved would agree with anything: the level must be seen to change as it is played.
        int distinct = drawn.Take(Ticks).Distinct().Count();
        Assert.True(distinct > Ticks / 2, $"The level's fingerprint took only {distinct} values in {Ticks} ticks: it does not see the level move.");
        for (int tick = 0; tick < Ticks; tick++)
        {
            Assert.True(
                drawn[tick] == simulated[tick],
                $"{title.Id} m{mission}: the level drawn and the level only simulated part on tick {tick + 1}: "
                + $"{drawn[tick]:x16} drawn, {simulated[tick]:x16} simulated.");
        }
    }

    // The level's fingerprint after each of its first ticks, the script's controls held as each tick comes.
    private static ulong[] Play(GameDataSource data, GameTitle title, int mission, bool simulationOnly)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = ParityHost.Create(data, title, mission, device, frames: 400 * Ticks, simulationOnly);
        List<ulong> digests = [];
        host.TickDigested = (tick, digest) =>
        {
            digests.Add(digest);
            host.TitleHeldForTesting = HeldAfter(tick);
        };

        host.Start();
        while (digests.Count < Ticks && host.Step())
        {
        }

        host.Stop();
        return [.. digests];
    }

    private static PlayerAction HeldAfter(int tick)
    {
        PlayerAction held = default(PlayerAction);
        foreach ((int from, PlayerAction action) in Script)
        {
            if (tick >= from)
            {
                held = action;
            }
        }

        return held;
    }
}

using System.Globalization;
using OpenIGI.Game.Player;
using OpenIGI.Host;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>Every Project I.G.I. mission can be played to its end: its own scripts say so, done as they ask.</summary>
/// <remarks>
/// <see cref="MissionSolver"/> works back from each level's LevelFlow <c>Complete</c> expression to what the player does,
/// and does it through the game's own paths. A mission passes when the host reports it completed (<c>MissionEnded</c>), as
/// it does before it loads the next one. Issue #44 was mission 11 failing this: its first objective never counted the
/// rifle.
/// <para>Each mission is a whole game. Run in one process, the fourteen reach about 9 GB, because a stopped host is not
/// freed; filter by mission (<c>DisplayName~mission: 7)</c>) or run them in small batches.</para>
/// </remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class MissionCompletionTests : IDisposable
{
    private const int MaxTicks = 30 * 60 * 20;

    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void Mission_can_be_completed(int mission)
    {
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.InfiniteHealth.Override(true);
        try
        {
            // A run is tens of thousands of frames: what a frame drew is not kept, or the lists grow without end.
            using RecordingGraphicsDevice device = new() { IsRecording = false };
            GameHost host = _support.Host(
                device,
                new HeadlessGameWindow(),
                "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "100000000");
            host.Start();
            MissionSolver solver = MissionSolver.Over(host, mission);
            solver.LogFile = Path.Combine(Path.GetTempPath(), $"openigi-solve-m{mission}.txt");
            solver.Run(MaxTicks);
            host.Stop();
            File.WriteAllText(solver.LogFile, solver.Log);
            Assert.True(solver.Outcome == true, $"Mission {mission} {(solver.Outcome == false ? "failed" : "never ended")}:\n{solver.Log}");
        }
        finally
        {
            PlayerCheatCVars.InfiniteHealth.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }
    }
}

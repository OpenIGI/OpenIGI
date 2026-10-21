using OpenIGI.Game.Physics;
using OpenIGI.Engine.Flow;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Runtime;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using static OpenIGI.Game.Tests.RetailMissionFixture;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The cutscene runtime across the whole campaign: every mission's opening chain runs to its end, and
/// the natives the cutscenes call stay within the implemented set.
/// </summary>
public sealed class RetailCutsceneRuntimeTests
{
    private readonly ITestOutputHelper _output;

    public RetailCutsceneRuntimeTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
    public void EveryRetailMissionCanRunItsOpeningCutsceneChain(int missionId)
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(missionId);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        Assert.NotEmpty(Walk(root).OfType<CutSceneTask>());
        Assert.True(runtime.IsPlaying);
        Assert.NotNull(runtime.Frame);
        for (int tick = 0; tick < 20_000 && runtime.IsPlaying; tick++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }

        Assert.False(runtime.IsPlaying);
    }

    [RequiresGameDataFact]
    public void MissionFiveIntroShowsAuthoredBinocularOverlayOnlyDuringScene3501()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(5);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);

        // LEVEL5/objects.qsc authors the holder as
        // CutScene_3500.isFinished && !CutScene_3501.isFinished && ConditionalContainer_1000.isRun.
        // The boundary ticks below are the frames produced by the retail 30 Hz completion contract.
        CutsceneFrame opening = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3500, opening.Scene.Id);
        Assert.Equal(0, opening.Scene.Ticks);
        Assert.False(opening.BinocularOverlayVisible);

        CutsceneFrame lastEstablishingFrame = opening;
        int lastEstablishingTick = opening.Scene.Ticks;
        for (int tick = 0; tick < 1_000 && runtime.ActiveScene?.Id == 3500; tick++)
        {
            lastEstablishingFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
            lastEstablishingTick = lastEstablishingFrame.Scene.Ticks;
            runtime.Tick();
        }

        Assert.NotEqual(3500, runtime.ActiveScene?.Id);
        Assert.Equal(107, lastEstablishingTick);
        Assert.False(lastEstablishingFrame.BinocularOverlayVisible);

        CutsceneFrame firstBinocularFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3501, firstBinocularFrame.Scene.Id);
        Assert.Equal(1, firstBinocularFrame.Scene.Ticks);
        Assert.True(firstBinocularFrame.BinocularOverlayVisible);

        CutsceneFrame lastBinocularFrame = firstBinocularFrame;
        int lastBinocularTick = firstBinocularFrame.Scene.Ticks;
        for (int tick = 0; tick < 1_000 && runtime.ActiveScene?.Id == 3501; tick++)
        {
            lastBinocularFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
            lastBinocularTick = lastBinocularFrame.Scene.Ticks;
            Assert.True(lastBinocularFrame.BinocularOverlayVisible);
            runtime.Tick();
        }

        Assert.NotEqual(3501, runtime.ActiveScene?.Id);
        Assert.Equal(744, lastBinocularTick);
        CutsceneFrame firstClosingFrame = Assert.IsType<CutsceneFrame>(runtime.Frame);
        Assert.Equal(3502, firstClosingFrame.Scene.Id);
        Assert.Equal(1, firstClosingFrame.Scene.Ticks);
        Assert.False(firstClosingFrame.BinocularOverlayVisible);
    }

    /// <summary>Mission 10's two parked consists stand where the original settles them, car by car.</summary>
    /// <remarks>
    /// The probe's <c>train</c> hook set reads each car's committed pose at Train::UpdateConsist's world commit
    /// (<c>0x42A66B</c> for a lead, <c>0x42AAB3</c> behind it) once every car has initialised. Its rail (870) is
    /// automatically oriented (<c>0x500EF0</c>) and climbs and turns under the second consist, so it exercises the
    /// polyline walk, the chord frames and the waypoint frames together.
    /// </remarks>
    [RequiresGameDataFact]
    public void MissionTenConsistsStandWhereTheOriginalSettlesThem()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        LevelTask root = fixture.Root;
        MagicObjectRegistry magic = fixture.LoadMagic();
        LoadedMission loaded = fixture.Load(10);
        WorldScene world = WorldScene.Build(loaded, root, magic);

        (int Lead, string Model, Vector3d Position, float[] Orientation)[] retail =
        [
            (666, "605_01_1", new(6_962_140.833, 152_980_859.470, 174_503_575.711), [0.725996f, -0.687540f, -0.014760f, 0.687398f, 0.726146f, -0.013975f, 0.020326f, 0f, 0.999793f]),
            (666, "613_01_1", new(6_915_451.728, 152_930_135.191, 174_502_796.289), [0.625440f, -0.780270f, -0.001426f, 0.780268f, 0.625442f, -0.001779f, 0.002280f, 0f, 0.999997f]),
            (666, "608_01_1", new(6_874_934.224, 152_874_249.336, 174_503_017.088), [0.547329f, -0.836903f, 0.004728f, 0.836872f, 0.547350f, 0.007230f, -0.008639f, 0f, 0.999962f]),
            (666, "606_01_1", new(6_838_411.555, 152_815_603.859, 174_503_803.981), [0.509719f, -0.860310f, 0.007196f, 0.860225f, 0.509769f, 0.012145f, -0.014117f, 0f, 0.999900f]),
            (666, "607_01_1", new(6_803_872.076, 152_755_758.847, 174_504_912.865), [0.489879f, -0.871745f, 0.008800f, 0.871605f, 0.489958f, 0.015657f, -0.017961f, 0f, 0.999838f]),
            (666, "609_01_1", new(6_769_986.940, 152_695_539.566, 174_506_216.651), [0.490724f, -0.871260f, 0.009700f, 0.871090f, 0.490820f, 0.017219f, -0.019763f, 0f, 0.999804f]),
            (300, "605_01_1", new(9_127_879.197, 152_980_582.059, 174_438_968.451), [0.057859f, 0.998295f, 0.007579f, -0.989839f, 0.058353f, -0.129666f, -0.129887f, 0f, 0.991528f]),
            (300, "606_01_1", new(9_110_593.178, 153_184_865.032, 174_469_449.411), [0.127097f, 0.991686f, 0.020109f, -0.979501f, 0.128678f, -0.154977f, -0.156276f, 0f, 0.987713f]),
            (300, "609_01_1", new(9_081_494.642, 153_316_715.159, 174_496_749.894), [0.332743f, 0.940300f, 0.071530f, -0.919299f, 0.340344f, -0.197622f, -0.210169f, 0f, 0.977664f]),
        ];

        foreach ((int lead, string model, Vector3d position, float[] orientation) in retail)
        {
            WorldPlacement car = world.Placements.Single(placement =>
                placement.Owner is DeclaredTask { Type.Name: "Train" } train
                && (train.Id == lead || train.Parent?.Id == lead)
                && string.Equals(placement.Instance.Name, model, StringComparison.OrdinalIgnoreCase));
            Vector3d expected = position / WorldScene.UnitsPerMetre;
            Assert.True((car.Position - expected).Length <= 0.002d,
                $"{lead}/{model}: retail={expected}, port={car.Position}, delta={(car.Position - expected).Length:F6}m");
            Matrix3f m = car.Orientation;
            float[] port = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
            for (int i = 0; i < 9; i++)
            {
                Assert.True(MathF.Abs(port[i] - orientation[i]) <= 2e-5f, $"{lead}/{model} M[{i}]: retail={orientation[i]}, port={port[i]}");
            }
        }
    }

    [RequiresGameDataFact]
    public void CampaignCutsceneNativeSurfaceStaysWithinTheImplementedSet()
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            RetailMissionFixture fixture = RetailMissionFixture.Open();
            LevelTask root = fixture.Root;
            _ = fixture.Load(missionId);
            foreach (GameTask task in Walk(root))
            {
                if (!IsCinematic(task))
                {
                    continue;
                }
                IEnumerable<string> texts = task switch
                {
                    CutSceneTask scene => [scene.Run, scene.Reset, scene.TimeDelta,
                        scene.StartExpression, scene.StopExpression],
                    ConditionalContainerTask container => [container.Condition, container.RunAtStart, container.RunAtStop],
                    DeclaredTask declared => declared.Type.Parameters
                        .Select((_, i) => declared.GetValue(i)).OfType<string>(),
                    _ => [],
                };
                foreach (string text in texts)
                {
                    foreach (System.Text.RegularExpressions.Match match in
                        System.Text.RegularExpressions.Regex.Matches(text, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                    {
                        names.Add(match.Groups[1].Value);
                    }
                }
            }
        }
        Assert.Equal(
            ["Game_CutsceneDelete", "Game_DisableMusic", "Game_EnableMusic", "MenuManager_SetEnabled"],
            names.Order(StringComparer.Ordinal));

        static bool IsCinematic(GameTask task)
        {
            for (GameTask? current = task; current is not null; current = current.Parent)
            {
                if (current is CutSceneTask)
                {
                    return true;
                }
                if (current is ConditionalContainerTask container
                    && (container.Label.Contains("cutscene", StringComparison.OrdinalIgnoreCase)
                        || container.Label.Contains("cut scene", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

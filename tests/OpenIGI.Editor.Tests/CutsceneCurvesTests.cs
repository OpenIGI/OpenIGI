using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The curves view reads the game's own path: every value channel passes through every key's value at its moment; a value set reads back.</summary>
public sealed class CutsceneCurvesTests
{
    private static readonly CutsceneChannel[] Values =
        [CutsceneChannel.X, CutsceneChannel.Y, CutsceneChannel.Z, CutsceneChannel.TurnX, CutsceneChannel.TurnY, CutsceneChannel.TurnZ, CutsceneChannel.Lens];

    [RequiresGameDataFact]
    public void Every_value_curve_passes_through_its_keys_and_a_set_value_reads_back()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level1/objects.qvm")));
        int checkedKeys = 0;
        foreach (MissionTask scene in CutsceneModel.Scenes(document))
        {
            IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
            CutsceneKey[] path = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
            float scale = (float)CutsceneModel.TimeScale(scene);
            for (int i = 0; i < shots.Count; i++)
            {
                // A key that lasts no time shares its moment with the next: the path is at the later one there.
                if (shots[i].Length < 1e-3)
                {
                    continue;
                }

                foreach (CutsceneChannel channel in Values)
                {
                    double tolerance = channel is CutsceneChannel.X or CutsceneChannel.Y or CutsceneChannel.Z ? 0.01 : 0.05;
                    Assert.True(Math.Abs(CutsceneCurves.At(path, scale, shots[i].Start, channel) - CutsceneCurves.Of(shots[i].Camera, channel)) < tolerance,
                        $"{scene.Label} key {i + 1} {channel}");
                }

                checkedKeys++;
            }
        }

        Assert.True(checkedKeys > 20, $"{checkedKeys} keys");

        MissionTask key = CutsceneModel.Shots(CutsceneModel.Scenes(document)[0])[0].Camera;
        foreach ((CutsceneChannel channel, double value) in ((CutsceneChannel, double)[])[(CutsceneChannel.Z, 123.5), (CutsceneChannel.TurnZ, -37.5), (CutsceneChannel.Lens, 60)])
        {
            CutsceneCurves.Set(document, key, channel, value).Apply();
            Assert.Equal(value, CutsceneCurves.Of(key, channel), 2);
        }
    }

    [Fact]
    public void Rates_are_not_draggable_values()
    {
        Assert.False(CutsceneCurves.IsValue(CutsceneChannel.Speed));
        Assert.False(CutsceneCurves.IsValue(CutsceneChannel.TurnRate));
        Assert.True(CutsceneCurves.IsValue(CutsceneChannel.Lens));
    }
}

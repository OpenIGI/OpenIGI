using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Editor.Tests;

/// <summary>
/// Rigs baked into keys: a rail runs its path at one speed; a crane rises round its subject watching it; a rig made again replaces
/// its own keys where they were; a hand edit is noticed; the sidecar reads back; the mission saves as plain knots that read back.
/// </summary>
public sealed class CutsceneRigsTests(ITestOutputHelper output)
{
    private static CutsceneRig Rail(int sceneId, params Vector3d[] points) =>
        new("Rail 1", CutsceneRigKind.Rail, sceneId, -1, points, 8, 0, 2, 0, 0, string.Empty);

    [Fact]
    public void A_rail_runs_its_path_at_one_speed_and_a_crane_rises_watching_its_subject()
    {
        CutsceneRig rail = Rail(1, new Vector3d(0, 0, 0), new Vector3d(40, 0, 0), new Vector3d(40, 30, 0));
        IReadOnlyList<CutsceneRigKey> keys = CutsceneRigs.Bake(rail, null);
        CutsceneKey[] path = [.. keys.Select(static k => new CutsceneKey(k.PositionUnits, k.Angles.X, k.Angles.Y, k.Angles.Z, 1, (float)k.Seconds, true, -1, 0, 0))];

        // Speed over the middle of the run (the ends start and stop as the game's curve does).
        List<double> speeds = [];
        for (double t = 1; t < 7; t += 0.25)
        {
            speeds.Add((CutsceneCameraMath.Evaluate(path, 1, t + 0.05, -1f).Position - CutsceneCameraMath.Evaluate(path, 1, t - 0.05, -1f).Position).Length / WorldUnits.PerMetre / 0.1);
        }

        output.WriteLine($"rail: {keys.Count} keys, speed {speeds.Min():0.0}-{speeds.Max():0.0} m/s (70 m in 8 s = {70 / 8.0:0.0})");
        Assert.All(keys, k => Assert.Equal(2 * WorldUnits.PerMetre, k.PositionUnits.Z, 3));
        Assert.True(speeds.Max() / speeds.Min() < 1.1, $"speed {speeds.Min():0.0}..{speeds.Max():0.0}");

        Vector3d subject = new(100, 100, 10);
        CutsceneRig crane = new("Crane 1", CutsceneRigKind.Crane, 1, 7, [], 6, 20, 15, 90, 0, string.Empty);
        IReadOnlyList<CutsceneRigKey> arm = CutsceneRigs.Bake(crane, subject);
        Assert.True(arm[^1].PositionUnits.Z > arm[0].PositionUnits.Z + (10 * WorldUnits.PerMetre));
        foreach (CutsceneRigKey key in arm)
        {
            Vector3f forward = CutsceneCameraMath.ToCameraOrientation(Matrix3f.FromEngineEulerAngles(key.Angles.X, key.Angles.Y, key.Angles.Z)).Row3;
            Vector3d to = subject - (key.PositionUnits / WorldUnits.PerMetre);
            double cos = ((forward.X * to.X) + (forward.Y * to.Y) + (forward.Z * to.Z)) / to.Length;
            Assert.True(cos > 0.999, $"looks {Math.Acos(Math.Min(1, cos)) * 180 / Math.PI:0.0}° off its subject");
        }

        Assert.Empty(CutsceneRigs.Bake(crane, null));
        Assert.Empty(CutsceneRigs.Bake(Rail(1, new Vector3d(0, 0, 0)), null));
    }

    [RequiresGameDataFact]
    public void A_rig_made_again_replaces_its_own_keys_notices_hand_edits_and_saves_as_plain_knots()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level1/objects.qvm")));
        MissionTask scene = CutsceneModel.Scenes(document)[0];
        int before = CutsceneModel.Shots(scene).Count;
        MissionTask first = CutsceneModel.Shots(scene)[0].Camera;

        CutsceneRig rig = Rail(scene.Id, new Vector3d(0, 0, 0), new Vector3d(60, 0, 0));
        (var add, CutsceneRig placed) = CutsceneRigs.Place(document, scene, rig, null, first)!.Value;
        add.Apply();
        int keys = CutsceneRigs.KeysOf(scene, rig.Name).Count;
        Assert.Equal(before + keys, CutsceneModel.Shots(scene).Count);
        Assert.Same(CutsceneModel.Shots(scene)[1].Camera, CutsceneRigs.KeysOf(scene, rig.Name)[0]);
        Assert.True(CutsceneRigs.IsIntact(scene, placed));

        // A shot of its own: cut to from the key before, cutting out at its end -- one move of its keys alone.
        Assert.False(CutsceneMoves.Glides(first));
        Assert.False(CutsceneMoves.Glides(CutsceneRigs.KeysOf(scene, rig.Name)[^1]));
        Assert.Contains(CutsceneMoves.Of(scene), m => m.Keys.Select(static k => k.Camera).SequenceEqual(CutsceneRigs.KeysOf(scene, rig.Name)));

        // Longer and higher: the same place in the scene, its keys made again.
        (var remake, CutsceneRig longer) = CutsceneRigs.Place(document, scene, placed with { Seconds = 12, Height = 5 }, null, null)!.Value;
        remake.Apply();
        Assert.Same(CutsceneModel.Shots(scene)[1].Camera, CutsceneRigs.KeysOf(scene, rig.Name)[0]);
        Assert.Equal(before + keys, CutsceneModel.Shots(scene).Count);
        Assert.Equal(5 * WorldUnits.PerMetre, CutsceneMoves.KeyOf(CutsceneRigs.KeysOf(scene, rig.Name)[0]).Position.Z, 1);
        Assert.True(CutsceneRigs.IsIntact(scene, longer));

        // The sidecar reads back; a hand edit is noticed.
        Assert.Equal(longer with { Points = [] }, CutsceneRigs.Read(CutsceneRigs.Write([longer]))[0] with { Points = [] });
        Assert.Equal(longer.Points, CutsceneRigs.Read(CutsceneRigs.Write([longer]))[0].Points);
        SetParameterCommand.Numbers(document, CutsceneRigs.KeysOf(scene, rig.Name)[1], "FOV", [0.5]).Apply();
        Assert.False(CutsceneRigs.IsIntact(scene, longer));

        // Saved as the game reads it: plain knots, its keys labelled.
        MissionDocument reread = MissionDocument.Read(document.ToImage());
        MissionTask again = CutsceneModel.Scenes(reread)[0];
        Assert.Equal(before + keys, CutsceneModel.Shots(again).Count);
        Assert.Equal(keys, CutsceneRigs.KeysOf(again, rig.Name).Count);
    }
}

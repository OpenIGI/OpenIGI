using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Editor.Tests;

/// <summary>
/// A scene's camera as moves and keys: every knot of every retail scene in exactly one move, the moves end to end over the scene;
/// a key retimed or taken out leaves the other keys where they were; a key added on the path leaves the camera's way all but as
/// it was, and the path passes through it.
/// </summary>
public sealed class CutsceneMovesTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void Every_retail_knot_is_a_key_of_one_move_and_the_moves_run_end_to_end()
    {
        int scenes = 0, moves = 0;
        foreach (MissionDocument document in RetailMissions())
        {
            foreach (MissionTask scene in CutsceneModel.Scenes(document))
            {
                IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
                IReadOnlyList<CutsceneMove> made = CutsceneMoves.Of(scene);
                Assert.Equal(shots.Select(static s => s.Camera), made.SelectMany(static m => m.Keys).Select(static k => k.Camera));
                for (int i = 1; i < made.Count; i++)
                {
                    Assert.True(Math.Abs(made[i].Start - made[i - 1].End) < 1e-9);
                }

                if (made.Count > 0)
                {
                    Assert.True(Math.Abs(made[^1].End - CutsceneModel.Length(scene)) < 1e-9);
                }

                scenes++;
                moves += made.Count;
            }
        }

        output.WriteLine($"{scenes} scenes, {moves} moves");
        Assert.True(scenes >= 40);
    }

    [RequiresGameDataFact]
    public void Retiming_and_taking_out_a_key_keep_the_others_where_they_were()
    {
        MissionDocument document = Mission(1);
        MissionTask scene = CutsceneModel.Scenes(document).First(static s => CutsceneModel.Shots(s).Count >= 4);
        IReadOnlyList<CutsceneShot> before = CutsceneModel.Shots(scene);
        CutsceneShot moved = before[2];
        double to = moved.Start + (moved.Length * 0.25);

        CutsceneMoves.Retime(document, scene, moved.Camera, to, ripple: false)!.Apply();
        IReadOnlyList<CutsceneShot> after = CutsceneModel.Shots(scene);
        Assert.True(Math.Abs(after[2].Start - to) < 1e-3);
        Assert.True(Math.Abs(after[3].Start - before[3].Start) < 1e-3, "the key after stays");

        CutsceneMoves.Retime(document, scene, moved.Camera, to + 1, ripple: true)!.Apply();
        IReadOnlyList<CutsceneShot> rippled = CutsceneModel.Shots(scene);
        Assert.True(Math.Abs(rippled[3].Start - (after[3].Start + 1)) < 1e-3, "rippling moves the keys after with it");

        double last = CutsceneModel.Shots(scene)[^1].Start;
        CutsceneMoves.RemoveKey(document, scene, moved.Camera).Apply();
        Assert.True(Math.Abs(CutsceneModel.Shots(scene)[^1].Start - last) < 1e-3, "the scene's timing is kept");
    }

    [RequiresGameDataFact]
    public void A_key_added_on_the_path_leaves_the_camera_as_it_went()
    {
        MissionDocument document = Mission(1);
        MissionTask scene = CutsceneModel.Scenes(document).First(static s => CutsceneModel.Shots(s).Any(static k => k.Length >= 4 && CutsceneMoves.Glides(k.Camera)));
        CutsceneShot longest = CutsceneModel.Shots(scene).Where(static k => CutsceneMoves.Glides(k.Camera)).MaxBy(static k => k.Length)!;
        double at = longest.Start + (longest.Length * 0.5);
        float scale = (float)CutsceneModel.TimeScale(scene);
        CutsceneKey[] was = [.. CutsceneModel.Shots(scene).Select(static s => CutsceneMoves.KeyOf(s.Camera))];

        (Editor.Core.Commands.IEditorCommand step, MissionTask added) = CutsceneMoves.AddKey(document, scene, at)!.Value;
        step.Apply();
        CutsceneKey[] now = [.. CutsceneModel.Shots(scene).Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        Assert.Equal(was.Length + 1, now.Length);
        at = CutsceneModel.Shots(scene).First(s => ReferenceEquals(s.Camera, added)).Start;

        // Through the new key, to the float precision the game keeps a key's place in (a millimetre this far out); elsewhere the path
        // moves a little, as a key added to any cubic does.
        Vector3d there = CutsceneCameraMath.Evaluate(was, scale, at, -1f).Position;
        double through = (CutsceneCameraMath.Evaluate(now, scale, at, -1f).Position - there).Length;
        output.WriteLine($"through the new key within {through:0.####} units");
        Assert.True(through < 0.001 * 4096, $"{through} units from the path at the new key");
        double drift = 0, travel = 0;
        for (double t = longest.Start; t <= longest.Start + longest.Length; t += 1 / 30.0)
        {
            drift = Math.Max(drift, (CutsceneCameraMath.Evaluate(now, scale, t, -1f).Position - CutsceneCameraMath.Evaluate(was, scale, t, -1f).Position).Length);
        }

        travel = (CutsceneCameraMath.Evaluate(was, scale, longest.Start + longest.Length, -1f).Position - CutsceneCameraMath.Evaluate(was, scale, longest.Start, -1f).Position).Length;
        output.WriteLine($"key added at {at:0.##} s: the path moved at most {drift / 4096:0.###} m over a move of {travel / 4096:0.#} m");
        Assert.True(drift <= Math.Max(0.05 * travel, 0.01 * 4096), $"{drift} units against {travel}");
    }

    private static MissionDocument Mission(int level) =>
        MissionDocument.Read(File.ReadAllBytes(GameData.Resolve($"missions/location0/level{level}/objects.qvm")));

    private static IEnumerable<MissionDocument> RetailMissions() => Enumerable.Range(1, 14).Select(Mission);
}

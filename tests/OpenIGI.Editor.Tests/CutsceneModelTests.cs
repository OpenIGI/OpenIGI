using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A cutscene as the sequencer reads it: its shots on the scene's clock, and what is cued off that clock.</summary>
public sealed class CutsceneModelTests
{
    private const string Level = """
        Task_DeclareParameters("CutScene", "Time scale", "Real32");
        Task_DeclareParameters("EditCamera", "Position", "ObjectPos", "Alpha", "Real32", "Beta", "Real32", "Gamma", "Real32", "FOV", "Real32", "Duration", "Real32", "Smooth to next", "bool8", "Link task ID", "Int32", "Target task ID", "Int32", "Time of day", "Real32");
        Task_DeclareParameters("StatusMessage", "Send", "VarString", "Text", "VarString");
        Task_New(-1, "Container", "Root",
            Task_New(50, "CutScene", "Intro", 2,
                Task_New(-1, "EditCamera", "a", 0, 0, 0, 0, 0, 0, 1, 1.5, TRUE, -1, -1, -1),
                Task_New(-1, "Container", "nested",
                    Task_New(-1, "EditCamera", "b", 0, 0, 0, 0, 0, 0, 1, 2, TRUE, -1, -1, -1)),
                Task_New(-1, "EditCamera", "c", 0, 0, 0, 0, 0, 0, 1, 0, TRUE, -1, -1, -1)),
            Task_New(60, "StatusMessage", "ticks", "CutScene_50.nTick > 90", "M1_CUT_01"),
            Task_New(61, "StatusMessage", "seconds", "CutScene_50.nTick >= 4*GAME_FREQUENCY && 1", "M1_CUT_02"),
            Task_New(62, "StatusMessage", "other", "CutScene_51.nTick > 10", "M1_CUT_03"));
        """;

    /// <summary>Shots run depth first as the game flattens them, each its duration times the scene's time scale, a zero one kept.</summary>
    [Fact]
    public void Shots_follow_the_scene_clock()
    {
        MissionDocument document = Document();
        MissionTask scene = Assert.Single(CutsceneModel.Scenes(document));

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        Assert.Equal(["a", "b", "c"], shots.Select(static s => s.Camera.Label));
        Assert.Equal([0.0, 3.0, 7.0], shots.Select(static s => s.Start));
        Assert.Equal(7.0, CutsceneModel.Length(scene));
    }

    /// <summary>A condition waiting for the scene's tick is a cue, in ticks or in seconds; another scene's is not.</summary>
    [Fact]
    public void Cues_are_read_and_moved_as_written()
    {
        MissionDocument document = Document();
        MissionTask scene = CutsceneModel.Scenes(document)[0];

        IReadOnlyList<CutsceneCue> cues = CutsceneModel.Cues(document, scene);
        Assert.Equal(["ticks", "seconds"], cues.Select(static c => c.Task.Label));
        Assert.Equal([3.0, 4.0], cues.Select(static c => c.Seconds));

        CutsceneModel.Retime(document, scene, cues[0], 5)!.Apply();
        CutsceneModel.Retime(document, scene, cues[1], 1.5)!.Apply();
        string script = QscPrinter.Print(document.ToScript());
        Assert.Contains("\"CutScene_50.nTick > 150\"", script, StringComparison.Ordinal);
        Assert.Contains("\"CutScene_50.nTick >= 1.5*GAME_FREQUENCY && 1\"", script, StringComparison.Ordinal);
    }

    /// <summary>A shot's length is written under the scene's time scale; a new shot goes after the one given, in its container.</summary>
    [Fact]
    public void Shots_are_lengthened_and_added()
    {
        MissionDocument document = Document();
        MissionTask scene = CutsceneModel.Scenes(document)[0];
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);

        CutsceneModel.SetLength(document, scene, shots[0].Camera, 5).Apply();
        Assert.Equal(5.0, CutsceneModel.Shots(scene)[0].Length);

        (Core.Commands.IEditorCommand step, MissionTask camera) = CutsceneModel.AddShot(document, scene, shots[1].Camera, (1, 2, 3), (0.1, 0.2, 0.3), 3)!.Value;
        step.Apply();
        Assert.Same(shots[1].Camera.Parent, camera.Parent);
        Assert.Equal(["a", "b", "Shot", "c"], CutsceneModel.Shots(scene).Select(static s => s.Camera.Label));
        Assert.Equal(3.0, CutsceneModel.Shots(scene)[2].Length);
        step.Undo();
        Assert.Equal(3, CutsceneModel.Shots(scene).Count);
    }

    /// <summary>Every retail scene reads, its shots the scene's own EditCameras, and level 1's intro has its subtitles cued off it.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_scenes_read()
    {
        int scenes = 0, cues = 0;
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            foreach (MissionTask scene in CutsceneModel.Scenes(document))
            {
                scenes++;
                Assert.Equal(scene.SelfAndSubtree().Count(static t => t.TypeName == CutsceneModel.CameraType), CutsceneModel.Shots(scene).Count);
                cues += CutsceneModel.Cues(document, scene).Count;
            }
        }

        Assert.True(scenes >= 40, $"{scenes} scenes");
        Assert.True(cues > 50, $"{cues} cues");
    }

    private static MissionDocument Document() => MissionDocument.Read(QscParser.ParseScript(Level, QvmProgram.IgiVersion));
}

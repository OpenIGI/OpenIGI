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

    /// <summary>A shot is moved to a view, set to watch or ride a task and to cut, each as one undoable step.</summary>
    [Fact]
    public void Shots_are_aimed_and_linked()
    {
        MissionDocument document = Document();
        MissionTask camera = CutsceneModel.Shots(CutsceneModel.Scenes(document)[0])[0].Camera;

        Core.Commands.IEditorCommand view = CutsceneModel.SetView(document, camera, (10, 20, 30), (0.5, 0, 1.25));
        view.Apply();
        CutsceneModel.Watch(document, camera, 60).Apply();
        CutsceneModel.Ride(document, camera, 61).Apply();
        CutsceneModel.SetSmooth(document, camera, false).Apply();
        string script = QscPrinter.Print(document.ToScript());
        Assert.Contains("\"a\", 10.0, 20.0, 30.0, 0.5, 0, 1.25, 1, 1.5, FALSE, 61, 60,", script, StringComparison.Ordinal);

        view.Undo();
        Assert.Contains("\"a\", 0, 0, 0, 0, 0, 0, 1, 1.5, FALSE, 61, 60,", QscPrinter.Print(document.ToScript()), StringComparison.Ordinal);
    }

    /// <summary>A new scene takes the mission's first scene's settings, is held until a rule plays it, and goes in a container of its own.</summary>
    [Fact]
    public void A_new_scene_is_like_the_missions_own()
    {
        MissionDocument document = Document();
        (Core.Commands.IEditorCommand step, MissionTask scene) = CutsceneModel.AddScene(document, "Cutscene 2", 70)!.Value;
        step.Apply();

        Assert.Equal(2, CutsceneModel.Scenes(document).Count);
        Assert.Equal(2.0, CutsceneModel.TimeScale(scene));
        Assert.Equal("Cutscenes made in the editor", scene.Parent!.Label);
        Assert.Empty(CutsceneModel.Shots(scene));

        (Core.Commands.IEditorCommand second, MissionTask another) = CutsceneModel.AddScene(document, "Cutscene 3", 71)!.Value;
        second.Apply();
        Assert.Same(scene.Parent, another.Parent);
        step.Undo();
    }

    /// <summary>Every shot of every move looks at its subject, and the move lasts as long as asked, its last shot holding none.</summary>
    [Fact]
    public void Camera_moves_look_at_their_subject()
    {
        OpenIGI.Core.Numerics.Vector3d subject = new(100, 200, 30);
        foreach (Panels.CameraMove move in Enum.GetValues<Panels.CameraMove>().Where(static m => m != Panels.CameraMove.Pan))
        {
            IReadOnlyList<Panels.MoveShot> shots = Panels.CameraMoves.Shots(move, subject, subject + new OpenIGI.Core.Numerics.Vector3d(40, 0, 5), 6);
            Assert.Equal(6.0, shots.Sum(static s => s.Seconds), 6);
            Assert.Equal(0.0, shots[^1].Seconds);
            foreach (Panels.MoveShot shot in shots)
            {
                OpenIGI.Core.Numerics.Matrix3f turn = OpenIGI.Core.Numerics.Matrix3f.FromEngineEulerAngles(shot.Angles.X, shot.Angles.Y, shot.Angles.Z);
                OpenIGI.Core.Numerics.Vector3f forward = OpenIGI.Game.World.CutsceneCameraMath.ToCameraOrientation(turn).Row3;
                OpenIGI.Core.Numerics.Vector3d towards = (subject * OpenIGI.Core.Numerics.WorldUnits.PerMetre) - shot.PositionUnits;
                double cos = ((forward.X * towards.X) + (forward.Y * towards.Y) + (forward.Z * towards.Z)) / towards.Length;
                Assert.True(cos > 0.999, $"{move}: a shot looks {Math.Acos(Math.Min(1, cos)) * 180 / Math.PI:0.#} degrees off its subject");
            }
        }
    }

    /// <summary>A move laid over ground higher than its shots keeps every shot above it, still looking at its subject.</summary>
    [Fact]
    public void Camera_moves_keep_above_the_ground()
    {
        OpenIGI.Core.Numerics.Vector3d subject = new(100, 200, 30);
        foreach (Panels.CameraMove move in Enum.GetValues<Panels.CameraMove>().Where(static m => m != Panels.CameraMove.Pan))
        {
            foreach (Panels.MoveShot shot in Panels.CameraMoves.Shots(move, subject, subject + new OpenIGI.Core.Numerics.Vector3d(40, 0, 5), 6, static (_, _) => 80))
            {
                Assert.True(shot.PositionUnits.Z / OpenIGI.Core.Numerics.WorldUnits.PerMetre >= 80 + Panels.CameraMoves.Clearance - 1e-6, $"{move} puts a shot under the ground");
                OpenIGI.Core.Numerics.Vector3f forward = OpenIGI.Game.World.CutsceneCameraMath.ToCameraOrientation(OpenIGI.Core.Numerics.Matrix3f.FromEngineEulerAngles(shot.Angles.X, shot.Angles.Y, shot.Angles.Z)).Row3;
                OpenIGI.Core.Numerics.Vector3d towards = (subject * OpenIGI.Core.Numerics.WorldUnits.PerMetre) - shot.PositionUnits;
                Assert.True(((forward.X * towards.X) + (forward.Y * towards.Y) + (forward.Z * towards.Z)) / towards.Length > 0.999, $"{move}: a raised shot no longer faces its subject");
            }
        }
    }

    /// <summary>
    /// The viewport looking through a move's key, as the sequencer's preview turns it (the engine's path, then the view's
    /// yaw and pitch), faces the subject.
    /// </summary>
    [Fact]
    public void The_preview_faces_the_subject()
    {
        OpenIGI.Core.Numerics.Vector3d subject = new(6020, -13663, 42590);
        IReadOnlyList<Panels.MoveShot> shots = Panels.CameraMoves.Shots(Panels.CameraMove.Orbit, subject, new OpenIGI.Core.Numerics.Vector3d(5989, -13820, 42607), 6);
        OpenIGI.Game.World.CutsceneKey[] keys = [.. shots.Select(static s => new OpenIGI.Game.World.CutsceneKey(s.PositionUnits, s.Angles.X, s.Angles.Y, s.Angles.Z, 1, (float)s.Seconds, true, -1, 0, 0))];
        double start = 0;
        foreach (Panels.MoveShot shot in shots)
        {
            OpenIGI.Game.World.CutscenePathPoint point = OpenIGI.Game.World.CutsceneCameraMath.Evaluate(keys, 1, start, -1);
            OpenIGI.Core.Numerics.Vector3f forward = OpenIGI.Game.World.CutsceneCameraMath.ToCameraOrientation(point.Orientation).Row3;
            float yaw = MathF.Atan2(forward.X, forward.Y), pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
            OpenIGI.Core.Numerics.Vector3f seen = OpenIGI.Core.Graphics.Camera3D.LookRotation(yaw, pitch).Row3;
            OpenIGI.Core.Numerics.Vector3d towards = subject - (point.Position / OpenIGI.Core.Numerics.WorldUnits.PerMetre);
            double cos = ((seen.X * towards.X) + (seen.Y * towards.Y) + (seen.Z * towards.Z)) / towards.Length;
            Assert.True(cos > 0.999, $"at {start}s the view looks {Math.Acos(Math.Min(1, cos)) * 180 / Math.PI:0.#} degrees off the subject");
            start += shot.Seconds;
        }
    }

    /// <summary>
    /// What the objectives are about, in their order, once each, only tasks with a place; a scene made the intro runs from the
    /// start until it is finished, as retail intros do.
    /// </summary>
    [Fact]
    public void An_intro_goes_to_what_the_objectives_are_about()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_DeclareParameters("CutScene", "Run", "VarString", "Initial run", "bool8", "Time scale", "Real32");
            Task_DeclareParameters("Terminal", "Position", "ObjectPos");
            Task_DeclareParameters("HumanSoldier", "Position", "ObjectPos");
            Task_DeclareParameters("EditVariable", "Add", "VarString");
            Task_DeclareParameters("DefineComputerObjective", "Objective 1 Complete Expression", "VarString", "Objective 2 Complete Expression", "VarString");
            Task_New(-1, "Container", "Root",
                Task_New(50, "CutScene", "Intro", "", FALSE, 1),
                Task_New(10, "Terminal", "t", 0, 0, 0),
                Task_New(20, "HumanSoldier", "boss", 0, 0, 0),
                Task_New(30, "EditVariable", "count", ""),
                Task_New(1, "DefineComputerObjective", "objectives", "HumanSoldier_20.isDead && EditVariable_30.nValue", "Terminal_10.isHacked || HumanSoldier_20.isDead"));
            """, QvmProgram.IgiVersion));

        Assert.Equal([20, 10], CutsceneModel.ObjectiveTargets(document).Select(static t => t.Id));

        MissionTask scene = CutsceneModel.Scenes(document)[0];
        CutsceneModel.PlayAtStart(document, scene).Apply();
        string script = QscPrinter.Print(document.ToScript());
        Assert.Contains("\"!CutScene_50.isFinished\", TRUE", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every retail scene reads, its shots the scene's own EditCameras, and level 1's intro has its subtitles cued off it; every
    /// scene that waits on something runs only until it is finished.
    /// </summary>
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
                string runText = scene.FindParameter("Run")?.Arguments is [QscStringLiteral { Value: { } run }] ? run : "";
                // A scene that waits on something runs only until it is finished (Igi1TaskActions: "Play the cutscene").
                if (runText.Trim() is not ("" or "1"))
                {
                    Assert.Contains(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"!CutScene_{scene.Id}.isFinished"), runText, StringComparison.Ordinal);
                }

                Assert.Equal(scene.SelfAndSubtree().Count(static t => t.TypeName == CutsceneModel.CameraType), CutsceneModel.Shots(scene).Count);
                cues += CutsceneModel.Cues(document, scene).Count;
            }
        }

        Assert.True(scenes >= 40, $"{scenes} scenes");

        // Level 1's insertion scene flies the helicopter (1502) on keys that run for seconds.
        string level1 = GameDataSource.Igi1.EnumerateFiles("objects.qvm").First(static p => p.Replace('\\', '/').Contains("location0/level1/", StringComparison.OrdinalIgnoreCase));
        MissionDocument first = MissionDocument.Read(File.ReadAllBytes(level1));
        MissionTask insertion = CutsceneModel.Scenes(first).First(static s => s.Id == 1200);
        CutsceneAnimation heli = Assert.Single(CutsceneModel.Animations(first, insertion), static a => a.TargetId == 1502);
        Assert.True(heli.Length > 5, $"the helicopter's keys run {heli.Length} s");
        Assert.True(cues > 50, $"{cues} cues");
    }

    /// <summary>
    /// Mission 2's intro as the sequencer shows it: its five lines of dialogue (the first begun with the scene, its condition only
    /// isRun) as dialogue, named by the speech they play; the container shown until 7.8 s cued from the start. The game has no
    /// subtitles here -- nothing puts a message up during the scene -- so the dialogue is the sounds.
    /// </summary>
    [RequiresGameDataFact]
    public void Mission_2s_intro_shows_its_dialogue_and_what_starts_with_it()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level2/objects.qvm")));
        MissionTask scene = document.EnumerateTasks().First(static t => t.Id == 666);
        IReadOnlyList<CutsceneCue> cues = CutsceneModel.Cues(document, scene);

        CutsceneCue[] dialogue = [.. cues.Where(static c => CutsceneModel.IsDialogue(c.Task))];
        Assert.Equal(["_cut02_01", "_cut02_02", "_cut02_03", "_cut02_04", "_cut02_05"], dialogue.Select(static c => CutsceneModel.SoundOf(c.Task)));
        Assert.Equal([0, 3.9, 7.3, 25, 26.5], dialogue.Select(static c => Math.Round(c.Seconds, 2)));
        Assert.Contains(cues, static c => c.Task.Id == 4003 && c.Seconds == 0);
        Assert.DoesNotContain(cues, static c => c.Task.TypeName == "StatusMessage");
        Assert.False(CutsceneModel.IsDialogue(cues.First(static c => c.Task.Id == 1006).Task), "anya typing is a sound, not dialogue");
    }

    private static MissionDocument Document() => MissionDocument.Read(QscParser.ParseScript(Level, QvmProgram.IgiVersion));
}

using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host;
using OpenIGI.Host.Editor;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// An editor's changes made in the level while a scene plays live are the changes a saved and loaded mission has: mission 1's
/// intro, changed at 10 s, gone back to 5 s and played to 16 s -- and the level then is, field for field, the one the same mission
/// saved with those changes and loaded afresh reaches at 16 s.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class CutsceneLiveEditTests(ITestOutputHelper output) : IDisposable
{
    private const int Scene = 1200;

    private readonly AiScenarioSupport _support = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "openigi-live-edit-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        _support.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>A shot's camera moved (the view only) and a cue moved later (what happens).</summary>
    [RequiresGameDataFact]
    public void Changes_made_live_are_the_changes_a_saved_mission_loads_with() => Compare((host, document, scene) =>
    {
        MissionTask camera = CutsceneModel.Shots(scene)[1].Camera;
        CutsceneCue cue = CutsceneModel.Cues(document, scene).First(static c => c.Seconds is > 6 and < 18);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"camera: {camera.Label}; cue: {cue.Task.TypeName} {cue.Task.Id} '{cue.Parameter}' at {cue.Seconds:0.##} s"));

        double[] position = Numbers(camera, "Position");
        SetParameterCommand.Numbers(document, camera, "Position", [position[0] + 2000, position[1], position[2] + 400]).Apply();
        Assert.Null(host.ApplyLiveEdit(document.LevelPath(camera), document.CompileTaskValues(camera), double.PositiveInfinity));
        double was = cue.Seconds;
        CutsceneModel.Retime(document, scene, cue, was + 1.5)!.Apply();
        Assert.Null(host.ApplyLiveEdit(document.LevelPath(cue.Task), document.CompileTaskValues(cue.Task), was));
    });

    /// <summary>A shot added after the first and another taken out: the scene's shots, and so its length, changed.</summary>
    [RequiresGameDataFact]
    public void Shots_added_and_taken_out_live_are_those_a_saved_mission_loads_with() => Compare((host, document, scene) =>
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        MissionTask first = shots[0].Camera;
        double[] at = Numbers(first, "Position");
        (Editor.Core.Commands.IEditorCommand add, MissionTask added) = CutsceneModel.AddShot(document, scene, first, (at[0] + 1500, at[1] - 800, at[2] + 600), (0.1, 0.2, 1.3), 2.5)!.Value;
        add.Apply();
        new RemoveTaskCommand(document, shots[2].Camera).Apply();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"added '{added.Label}' after '{first.Label}'; took out '{shots[2].Camera.Label}' (from {shots[1].Start:0.##} s)"));

        List<(string, ReadOnlyMemory<byte>?)> children = [.. scene.Children.Select(child =>
            (child.TypeName, child.TypeName == CutsceneModel.CameraType ? (ReadOnlyMemory<byte>?)document.CompileTaskValues(child) : null))];
        Assert.Null(host.ApplyLiveChildren(document.LevelPath(scene), children, shots[1].Start));
    });

    /// <summary>A key eased out live (helper keys baked in after it) plays as the mission saved with the ease loads.</summary>
    [RequiresGameDataFact]
    public void An_ease_baked_live_is_the_ease_a_saved_mission_loads_with() => Compare((host, document, scene) =>
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        CutsceneShot eased = shots.Take(shots.Count - 1).First(static s => s.Length >= 1 && s.Start + s.Length > 5 && s.Start < 16 && CutsceneMoves.Glides(s.Camera));
        CutsceneEasing.Set(document, scene, eased.Camera, CutsceneEase.InOut)!.Apply();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"eased '{eased.Camera.Label}' {eased.Start:0.##}-{eased.Start + eased.Length:0.##} s: {CutsceneModel.Shots(scene).Count - shots.Count} helper keys"));

        List<(string, ReadOnlyMemory<byte>?)> children = [.. scene.Children.Select(child =>
            (child.TypeName, child.TypeName == CutsceneModel.CameraType ? (ReadOnlyMemory<byte>?)document.CompileTaskValues(child) : null))];
        Assert.Null(host.ApplyLiveChildren(document.LevelPath(scene), children, eased.Start));
    });

    // The scene begun live, changed at 10 s by the edit given, taken back to 5 s and played to 16 s; then the mission saved with the
    // edit, loaded afresh and played to 16 s: the two levels and their cameras the same.
    private void Compare(Action<GameHost, MissionDocument, MissionTask> edit)
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-lockstep", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 100_000 && host.Mission().Cutscenes is null; i++)
        {
            Assert.True(host.Step());
        }

        string path = host.ObjectsPathForTesting!;
        MissionDocument document = MissionDocument.Read(host.ReadGameFile(path)!.Value);
        MissionTask scene = document.EnumerateTasks().First(static t => t.Id == Scene);

        IEditorCutscenePreview preview = host.CutscenePreviewForTesting!;
        Assert.True(preview.Begin(Scene));
        Settle(host, preview);
        preview.Seek(10);
        edit(host, document, scene);
        preview.Seek(5);
        preview.Seek(16);
        Assert.InRange(preview.Seconds, 15.96, 16.04);
        Vector3d liveCamera = host.Mission().CutsceneFrameShown!.Camera.Position;
        LevelCheckpoint live = host.CaptureLevel(480)!;

        string file = Path.Combine(_folder, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, document.ToImage());
        preview.Finish();
        host.MountForTesting("live-edit-test", _folder);
        host.ReloadLevelForTesting();
        Assert.True(preview.Begin(Scene));
        Settle(host, preview);
        preview.Seek(16);
        Assert.InRange(preview.Seconds, 15.96, 16.04);
        Vector3d loadedCamera = host.Mission().CutsceneFrameShown!.Camera.Position;
        LevelCheckpoint loaded = host.CaptureLevel(480)!;

        IReadOnlyList<string> differences = live.State.Differences(loaded.State, limit: 30);
        foreach (string difference in differences)
        {
            output.WriteLine(difference);
        }

        Assert.Equal(loadedCamera, liveCamera);
        Assert.Empty(differences);
    }

    private static double[] Numbers(MissionTask task, string parameter)
    {
        Assert.True(task.FindParameter(parameter)!.TryGetNumbers(out double[] values));
        return values;
    }

    private static void Settle(GameHost host, IEditorCutscenePreview preview)
    {
        for (int i = 0; i < 100_000 && preview.IsPreparing; i++)
        {
            Assert.True(host.Step());
        }

        Assert.False(preview.IsPreparing);
    }
}

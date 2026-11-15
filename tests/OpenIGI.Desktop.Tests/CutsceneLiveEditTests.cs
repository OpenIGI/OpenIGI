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
/// intro, a shot's camera moved and a cue moved later, made at 10 s, gone back to 5 s and played to 16 s -- and the level then
/// is, field for field, the one the same mission saved with those changes and loaded afresh reaches at 16 s.
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

    [RequiresGameDataFact]
    public void Changes_made_live_are_the_changes_a_saved_mission_loads_with()
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
        MissionTask camera = CutsceneModel.Shots(scene)[1].Camera;
        CutsceneCue cue = CutsceneModel.Cues(document, scene).First(static c => c.Seconds is > 6 and < 18);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"camera: {camera.Label}; cue: {cue.Task.TypeName} {cue.Task.Id} '{cue.Parameter}' at {cue.Seconds:0.##} s"));

        IEditorCutscenePreview preview = host.CutscenePreviewForTesting!;
        Assert.True(preview.Begin(Scene));
        Settle(host, preview);
        preview.Seek(10);

        // The edits, made in the document as the editor makes them and given to the running level.
        double[] position = Numbers(camera, "Position");
        SetParameterCommand.Numbers(document, camera, "Position", [position[0] + 2000, position[1], position[2] + 400]).Apply();
        Assert.Null(host.ApplyLiveEdit(document.LevelPath(camera), document.CompileTaskValues(camera), double.PositiveInfinity));
        double was = cue.Seconds;
        CutsceneModel.Retime(document, scene, cue, was + 1.5)!.Apply();
        Assert.Null(host.ApplyLiveEdit(document.LevelPath(cue.Task), document.CompileTaskValues(cue.Task), was));

        preview.Seek(5);
        preview.Seek(16);
        Assert.InRange(preview.Seconds, 15.96, 16.04);
        Vector3d liveCamera = host.Mission().CutsceneFrameShown!.Camera.Position;
        LevelCheckpoint live = host.CaptureLevel(480)!;

        // The same mission saved with the changes and loaded afresh, the scene played to the same moment.
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

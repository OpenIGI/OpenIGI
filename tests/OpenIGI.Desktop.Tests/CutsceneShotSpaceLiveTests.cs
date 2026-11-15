using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.Host.Editor;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A riding shot played live, its ride well under way: the camera the game shows, written back through the space the preview
/// reports, is the scene's own path as written -- so what the editor writes from its viewport is seen where the viewport was.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class CutsceneShotSpaceLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataFact]
    public void The_shown_camera_written_back_is_the_path_as_written()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-lockstep", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 100_000 && host.Mission().Cutscenes is null; i++)
        {
            Assert.True(host.Step());
        }

        MissionDocument document = MissionDocument.Read(host.ReadGameFile(host.ObjectsPathForTesting!)!.Value);

        // A riding shot that neither watches a target nor shakes, half a second into it.
        (MissionTask scene, int index, CutsceneShot shot) = CutsceneModel.Scenes(document)
            .Where(static s => s.Id >= 0)
            .SelectMany(static s => CutsceneModel.Shots(s).Select((shot, i) => (s, i, shot)))
            .First(static c => Number(c.shot.Camera, "Link task ID", -1) >= 0 && Number(c.shot.Camera, "Target task ID", -1) < 0
                && Number(c.shot.Camera, "Camera shake", 0) == 0 && c.shot.Length >= 1.0);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"scene {scene.Id}, key {index} '{shot.Camera.Label}' riding #{Number(shot.Camera, "Link task ID", -1)} at {shot.Start:0.##} s"));

        IEditorCutscenePreview preview = host.CutscenePreviewForTesting!;
        Assert.True(preview.Begin(scene.Id));
        for (int i = 0; i < 100_000 && preview.IsPreparing; i++)
        {
            Assert.True(host.Step());
        }

        preview.Seek(shot.Start + 0.5);
        (int key, CutsceneShotSpace space) = preview.ShotSpace!.Value;
        Assert.Equal(index, key);
        Assert.NotEqual(CutsceneShotSpace.World, space);

        Camera3D shown = host.Mission().CutsceneFrameShown!.Camera;
        (Vector3d written, Matrix3f turn) = space.ToAuthored(shown.Position * WorldScene.UnitsPerMetre, CutsceneCameraMath.FromCameraOrientation(shown.Orientation));

        // The path as written, at the moments the frame may have been built at: the shot's start (a ride taken once is held from
        // there) or about the playhead.
        CutsceneKey[] keys = [.. CutsceneModel.Shots(scene).Select(static s => KeyOf(s.Camera))];
        double scale = CutsceneModel.TimeScale(scene);
        double nearest = double.MaxValue;
        foreach (double t in (double[])[shot.Start, preview.Seconds, preview.Seconds - (1 / 30.0), preview.Seconds - (2 / 30.0)])
        {
            CutscenePathPoint point = CutsceneCameraMath.Evaluate(keys, (float)scale, t, -1f);
            nearest = Math.Min(nearest, (point.Position - written).Length);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"written back {nearest:0.###} units from the path"));
        Assert.True(nearest < 1.0, $"{nearest} units from the path as written");
        Assert.True(Math.Abs(turn.Row3.Length - 1f) < 1e-3f);
    }

    private static CutsceneKey KeyOf(MissionTask camera)
    {
        double[] at = Numbers(camera, "Position");
        return new CutsceneKey(
            new Vector3d(at[0], at[1], at[2]),
            (float)Number(camera, "Alpha", 0), (float)Number(camera, "Beta", 0), (float)Number(camera, "Gamma", 0),
            (float)Number(camera, "FOV", 1), (float)Number(camera, "Duration", 0),
            camera.FindParameter("Smooth to next") is { Arguments: [var smooth] } && ScriptValues.TryGetBool(smooth, out bool glides) && glides,
            -1f, 0f, 0f);
    }

    private static double[] Numbers(MissionTask task, string parameter) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) ? values : [0, 0, 0];

    private static double Number(MissionTask task, string parameter, double fallback) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;
}

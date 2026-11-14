using OpenIGI.Core.Numerics;
using OpenIGI.Game.Hosting;
using OpenIGI.Host;
using OpenIGI.Host.Editor;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The editor's cutscene preview on a real level: a scene played by the game's own tick, any moment of it the one play reaches
/// whether it is played up to, jumped to or gone back to, and the level loaded afresh once the preview ends.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class CutscenePreviewTests : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// Mission 1's insertion scene, begun on the level before its first tick as the editor holds it: its camera at any moment is
    /// the same however the moment is reached; it plays to its end; the preview leaves a level loaded afresh. The
    /// outro, which only the mission's end starts, is made to play.
    /// </summary>
    [RequiresGameDataFact]
    public void A_scene_plays_on_the_level_the_same_however_its_moments_are_reached()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-lockstep", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 100_000 && host.Mission().Cutscenes is null; i++)
        {
            Assert.True(host.Step());
        }

        Assert.Equal(0, host.Mission().Cutscenes!.SimulationTick);
        IEditorCutscenePreview preview = host.CutscenePreviewForTesting!;
        IMissionCutscenePreview game = host.Gameplay().CutscenePreview!;

        Assert.True(preview.Begin(1200));
        Assert.False(preview.WasForced);
        List<Vector3d> played = [];
        for (int tick = 1; tick <= 150; tick++)
        {
            preview.Seek(tick / 30.0);
            played.Add(Camera(host));
        }

        Assert.InRange(preview.Seconds, 4.96, 5.04);

        // Back: the level loaded afresh and played again up to the moment.
        preview.Seek(2);
        Assert.True(preview.IsPreparing);
        Settle(host, preview);
        Assert.InRange(preview.Seconds, 1.96, 2.04);
        Assert.Equal(played[59], Camera(host));
        preview.Seek(5);
        Assert.Equal(played[149], Camera(host));

        preview.Seek(60);
        Assert.True(preview.IsFinished);
        OpenIGI.Engine.Tasks.LevelTask ran = host.Mission().LevelRoot!;
        preview.Finish();
        Settle(host, preview);
        Assert.Null(preview.Scene);

        // Loaded afresh: not the level the scene ran in. (Out of the editor nothing holds it still, so it is not compared as it
        // loads: this host plays it on at once.)
        Assert.NotSame(ran, host.Mission().LevelRoot);

        Assert.True(preview.Begin(1204));
        Settle(host, preview);
        Assert.True(preview.WasForced);
        preview.Seek(3);
        Assert.True(game.IsPlaying(1204));
        Assert.InRange(preview.Seconds, 2.96, 3.04);
        ran = host.Mission().LevelRoot!;
        preview.Finish();
        Settle(host, preview);
        Assert.NotSame(ran, host.Mission().LevelRoot);
    }

    private static Vector3d Camera(GameHost host) => host.Mission().CutsceneFrameShown!.Camera.Position;

    // Frames run until the level loaded afresh is taken up.
    private static void Settle(GameHost host, IEditorCutscenePreview preview)
    {
        for (int i = 0; i < 100_000 && preview.IsPreparing; i++)
        {
            Assert.True(host.Step());
        }

        Assert.False(preview.IsPreparing);
    }
}

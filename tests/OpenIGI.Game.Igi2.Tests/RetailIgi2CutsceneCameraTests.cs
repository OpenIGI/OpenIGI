using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A cutscene's camera (<c>CutScene_SetCamera</c>, <c>igi2 0x439F20</c>; <c>CutScene_InterpolateCamera</c>, <c>0x438F10</c>): its
/// interpolation, its look at a target, its shake, and mission 1's intro seen through it.
/// </summary>
public sealed class RetailIgi2CutsceneCameraTests
{
    /// <summary>The intro's first cutscene, <c>CutScene_3049</c>, one camera watching the plane fly in.</summary>
    private const int Intro = 3049;

    /// <summary>Its camera, <c>EditCamera_3011</c>, which targets the plane.</summary>
    private const int IntroCamera = 3011;

    /// <summary>The plane, <c>EditRigidObj_3061</c>.</summary>
    private const int Plane = 3061;

    /// <summary>
    /// A value runs from its key's exactly, and reaches the next one's to the chopping; with no time on a side, that side's
    /// slope is nought.
    /// </summary>
    [Fact]
    public void A_value_runs_from_key_to_key()
    {
        Assert.Equal(10.0, Igi2CutsceneCameras.InterpolateScalar(0.0, 10.0, 20.0, 30.0, 0.0, 1.0, 2.0, 3.0, 1.0).Value);
        Assert.Equal(20.0, Igi2CutsceneCameras.InterpolateScalar(0.0, 10.0, 20.0, 30.0, 0.0, 1.0, 2.0, 3.0, 2.0).Value, 4);
        Assert.Equal(15.0, Igi2CutsceneCameras.InterpolateScalar(0.0, 10.0, 20.0, 30.0, 0.0, 1.0, 2.0, 3.0, 1.5).Value, 4);

        // One key alone stands still.
        Assert.Equal(7.0, Igi2CutsceneCameras.InterpolateScalar(7.0, 7.0, 7.0, 7.0, 0.0, 0.0, 0.0, 0.0, 5.0).Value);
    }

    /// <summary>The shake's stream starts where <c>CutScene_Open</c> seeds it.</summary>
    [Fact]
    public void The_shake_stream_is_seeded_as_the_game_starts()
    {
        Igi2CutsceneShake shake = new();
        Assert.Equal([55222, 21679, 29621, 5587, 49920, 10392], Enumerable.Range(0, 6).Select(_ => shake.Next()));
    }

    /// <summary>A camera that follows a target looks straight at it, its right level with the ground.</summary>
    [Fact]
    public void Following_looks_at_the_target()
    {
        Vector3d from = new(1000.0, 2000.0, 500.0), target = new(4000.0, -2000.0, 1500.0);
        float[] m = Igi2CutsceneCameras.FollowTarget(from, target);
        double dx = target.X - from.X, dy = target.Y - from.Y, dz = target.Z - from.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.Equal(dx / length, m[2], 5);
        Assert.Equal(dy / length, m[5], 5);
        Assert.Equal(dz / length, m[8], 5);
        Assert.Equal(0f, m[6]);

        // Down is the forward across right: the frame is a rotation.
        Assert.Equal(1.0, Determinant(m), 4);
    }

    /// <summary>
    /// Mission 1's intro: the first cutscene to run places its one camera where it was authored, as it has no shake, looking at
    /// the plane, with the cutscene's letterbox and the camera's time of day; and the title player looks through it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_intro_is_seen_through_its_camera()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        m.Run.CutsceneShake = new Igi2CutsceneShake();
        Igi2EditCameraTask camera = m.Tree.OfType<Igi2EditCameraTask>().Single(static c => c.Id == IntroCamera);
        Igi2CutSceneTask intro = m.Tree.OfType<Igi2CutSceneTask>().Single(static c => c.Id == Intro);
        Assert.Equal((Plane, 0f, 0.7f), (camera.TargetTaskId, camera.CameraShake, intro.ViewportHeightFactor));

        Igi2CutsceneView? view = null;
        for (int tick = 0; tick < 30 && view is null; tick++)
        {
            m.Run.Tick();
            view = m.Run.CutsceneView;
        }

        Assert.NotNull(view);
        Assert.Equal(camera.Position, view.Position);
        Assert.Equal((1f, 1003f, 0.7f, true, default(Vector3f)), (view.FieldOfView, view.LightTime, view.ViewportHeight, view.IsCameraJustChanged, view.Speed));

        Vector3d plane = m.Tree.OfType<Igi2EditRigidObjTask>().Single(static p => p.Id == Plane).CurrentPosition;
        double dx = plane.X - view.Position.X, dy = plane.Y - view.Position.Y, dz = plane.Z - view.Position.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.Equal(dx / length, view.Orientation[2], 4);
        Assert.Equal(dy / length, view.Orientation[5], 4);

        // The next tick it is the same camera, still on the plane.
        m.Run.Tick();
        Assert.False(m.Run.CutsceneView!.IsCameraJustChanged);

        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(RetailIgi2Mission1.Load(collision: true).Collision!, m.Level, null, camera.Position, 0f);
        player.Level = m.Run;
        Igi2CutsceneView now = m.Run.CutsceneView!;
        Assert.Equal(now.Position, player.EyePosition);
        Assert.Equal(new Vector3f(now.Orientation[2], now.Orientation[5], now.Orientation[8]), player.ViewOrientation.Row3);
        Assert.Equal(now.FieldOfView, player.FieldOfViewScale);
        Assert.Null(player.FirstPerson);
    }

    private static double Determinant(float[] m) =>
        (m[0] * ((m[4] * (double)m[8]) - (m[5] * (double)m[7])))
        - (m[1] * ((m[3] * (double)m[8]) - (m[5] * (double)m[6])))
        + (m[2] * ((m[3] * (double)m[7]) - (m[4] * (double)m[6])));
}

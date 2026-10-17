using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The shot that opens and closes the map computer, held to the things that would be visible if they were
/// wrong: it leaves from the player's eye, it arrives on the map's own vantage, the field of view is part
/// of the move rather than a cut, and the fall lands exactly where the player is about to be looking from.
/// </summary>
public sealed class MapComputerCameraTests
{
    private const float Frame = 1f / 60f;
    private const float GameplayFieldOfView = 1.05f;
    private const float MapFieldOfView = 0.30f;

    private static readonly CameraPose Eye =
        new(new Vector3d(120.0, 340.0, 12.5), 2.1f, -0.12f);

    private static readonly CameraPose Vantage =
        new(new Vector3d(120.0, 340.0, 912.5), 0f, MapComputerCamera.MapPitch);

    [Fact]
    public void ItLeavesFromWhereThePlayerIsLooking()
    {
        MapComputerCamera camera = Opened();

        camera.Update(Frame, Eye, Vantage, MapFieldOfView);

        // A frame in, not a frame's worth of travel in: the ease is cubic at the start, so one sixtieth of
        // a second into a nine-hundred-metre climb is centimetres. Asserted as a distance rather than to a
        // decimal place, which at this magnitude is a statement about rounding rather than about the shot.
        Assert.Equal(MapComputerPhase.Ascend, camera.Phase);
        Assert.True(
            Math.Abs(camera.Pose.Position.Z - Eye.Position.Z) < 0.1,
            "the first frame of the climb was not at the eye");
        Assert.Equal(GameplayFieldOfView, camera.FieldOfView, 1);
    }

    [Fact]
    public void ItArrivesOnTheMapsOwnVantage()
    {
        MapComputerCamera camera = Opened();

        RunPhase(camera, MapComputerCamera.AscendSeconds);

        // Past the climb the map owns the pose outright, so the hand-over is exact rather than nearly so:
        // any difference here would be a visible jump between the last frame of the shot and the first
        // frame of the map.
        Assert.Equal(MapComputerPhase.Boot, camera.Phase);
        Assert.Equal(Vantage.Position.Z, camera.Pose.Position.Z, 6);
        Assert.Equal(MapComputerCamera.MapPitch, camera.Pose.Pitch, 6);
        Assert.Equal(MapFieldOfView, camera.FieldOfView, 6);
    }

    [Fact]
    public void TheClimbNarrowsTheFieldOfViewAllTheWayUp()
    {
        // The dolly is the move. A climb of nine hundred metres at a gameplay field of view is a fall away
        // rather than a survey, so the field of view has to be closing monotonically the whole time — a
        // single frame where it widens would read as the camera stumbling.
        MapComputerCamera camera = Opened();
        float previous = float.MaxValue;

        for (float t = 0f; t < MapComputerCamera.AscendSeconds; t += Frame)
        {
            camera.Update(Frame, Eye, Vantage, MapFieldOfView);
            Assert.True(camera.FieldOfView <= previous + 1e-5f, $"the field of view widened at {t:0.000}s");
            previous = camera.FieldOfView;
        }
    }

    [Fact]
    public void TheClimbNeverDropsBelowWhereItStarted()
    {
        // A Bezier or an overshooting ease would dip the camera through the floor on its way up, which at
        // eye height means a frame or two inside the terrain.
        MapComputerCamera camera = Opened();

        for (float t = 0f; t < MapComputerCamera.AscendSeconds; t += Frame)
        {
            camera.Update(Frame, Eye, Vantage, MapFieldOfView);
            Assert.True(camera.Pose.Position.Z >= Eye.Position.Z - 1e-6, "the climb went below the eye");
        }
    }

    [Fact]
    public void ItLandsExactlyInThePlayersOwnEyes()
    {
        // The no-pop guarantee at the other end: the last frame of the fall and the first frame of play
        // have to be the same camera. Compared exactly rather than to a tolerance.
        MapComputerCamera camera = Opened();
        RunPhase(camera, MapComputerCamera.AscendSeconds);
        RunPhase(camera, MapComputerCamera.BootSeconds);
        camera.BeginClose(Vantage, MapFieldOfView, Eye, GameplayFieldOfView);
        RunPhase(camera, MapComputerCamera.ShutdownSeconds);
        RunPhase(camera, MapComputerCamera.DescendSeconds);

        Assert.Equal(MapComputerPhase.Done, camera.Phase);
        Assert.Equal(Eye.Position.X, camera.Pose.Position.X);
        Assert.Equal(Eye.Position.Y, camera.Pose.Position.Y);
        Assert.Equal(Eye.Position.Z, camera.Pose.Position.Z);
        Assert.Equal(Eye.Yaw, camera.Pose.Yaw);
        Assert.Equal(Eye.Pitch, camera.Pose.Pitch);
        Assert.Equal(GameplayFieldOfView, camera.FieldOfView);
    }

    [Fact]
    public void TheFallLeavesFromWhereverTheMapWasPannedTo()
    {
        // Closing the computer after panning across the level is a flight back, not a cut. The first frame
        // of the fall must therefore be at the panned vantage rather than over the player.
        var panned = new CameraPose(new Vector3d(1400.0, 2200.0, 912.5), 0f, MapComputerCamera.MapPitch);
        MapComputerCamera camera = Opened();
        RunPhase(camera, MapComputerCamera.AscendSeconds);
        RunPhase(camera, MapComputerCamera.BootSeconds);

        camera.BeginClose(panned, MapFieldOfView, Eye, GameplayFieldOfView);
        RunPhase(camera, MapComputerCamera.ShutdownSeconds, panned);
        camera.Update(Frame, Eye, panned, MapFieldOfView);

        Assert.Equal(MapComputerPhase.Descend, camera.Phase);
        Assert.True(
            Math.Abs(camera.Pose.Position.X - panned.Position.X) < 60.0,
            "the fall did not leave from the panned vantage");
    }

    [Fact]
    public void ThePlayerKeepsTheirInputUntilTheDisplayHasSettled()
    {
        // Panning during the climb would move a map the player cannot read yet, and a page change made
        // mid-boot would arrive on a screen that is still filling.
        MapComputerCamera camera = Opened();

        Assert.False(camera.IsInteractive);
        RunPhase(camera, MapComputerCamera.AscendSeconds);
        Assert.False(camera.IsInteractive);
        RunPhase(camera, MapComputerCamera.BootSeconds);
        Assert.True(camera.IsInteractive);
    }

    [Fact]
    public void TheDisplayIsOffAtBothEndsAndFullInTheMiddle()
    {
        // The grade is the transition. Anything left of it on the first or last frame is a green tint over
        // gameplay, which is the one failure of this effect a player would actually report.
        MapComputerCamera camera = Opened();

        camera.Update(Frame, Eye, Vantage, MapFieldOfView);
        Assert.Equal(0f, camera.DisplayAmount, 3);

        RunPhase(camera, MapComputerCamera.AscendSeconds);
        Assert.Equal(1f, camera.DisplayAmount, 3);

        camera.BeginClose(Vantage, MapFieldOfView, Eye, GameplayFieldOfView);
        RunPhase(camera, MapComputerCamera.ShutdownSeconds);
        RunPhase(camera, MapComputerCamera.DescendSeconds);
        Assert.Equal(0f, camera.DisplayAmount, 3);
    }

    [Fact]
    public void TheShutterIsExactlyZeroWhereTheViewIsHandedOver()
    {
        // A smear still running on the first frame the player controls is the shot overstaying.
        MapComputerCamera camera = Opened();
        RunPhase(camera, MapComputerCamera.AscendSeconds);

        Assert.Equal(0f, camera.MotionBlur);

        RunPhase(camera, MapComputerCamera.BootSeconds);
        camera.BeginClose(Vantage, MapFieldOfView, Eye, GameplayFieldOfView);
        RunPhase(camera, MapComputerCamera.ShutdownSeconds);
        RunPhase(camera, MapComputerCamera.DescendSeconds);

        Assert.Equal(0f, camera.MotionBlur);
    }

    [Fact]
    public void ClosingIsRefusedUntilThereIsSomethingToClose()
    {
        MapComputerCamera camera = new();

        Assert.False(camera.CanClose);
        camera.BeginOpen(Eye, GameplayFieldOfView, Vantage, MapFieldOfView);
        camera.Update(Frame, Eye, Vantage, MapFieldOfView);
        Assert.False(camera.CanClose);

        RunPhase(camera, MapComputerCamera.AscendSeconds);
        Assert.True(camera.CanClose);
    }

    private static MapComputerCamera Opened()
    {
        MapComputerCamera camera = new();
        camera.BeginOpen(Eye, GameplayFieldOfView, Vantage, MapFieldOfView);
        return camera;
    }

    /// <summary>Runs a phase to just past its end, so the state machine has stepped out of it.</summary>
    private static void RunPhase(MapComputerCamera camera, float seconds, CameraPose? vantage = null)
    {
        CameraPose from = vantage ?? Vantage;
        int steps = (int)MathF.Ceiling(seconds / Frame) + 2;
        for (int i = 0; i < steps; i++)
        {
            camera.Update(Frame, Eye, from, MapFieldOfView);
        }
    }
}

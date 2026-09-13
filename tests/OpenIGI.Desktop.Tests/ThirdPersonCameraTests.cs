using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>What the third-person view has to keep true, whatever it is tuned to.</summary>
public sealed class ThirdPersonCameraTests
{
    private static Camera3D FirstPerson(float yaw = 0f, float pitch = 0f) =>
        new(new Vector3d(10.0, 20.0, 1.6), Camera3D.LookRotation(yaw, pitch));

    private static ThirdPersonCamera Camera(double distance = 2.5, double shoulder = 0.5, double height = 0.25) =>
        new()
        {
            DistanceInMetres = distance,
            ShoulderInMetres = shoulder,
            HeightInMetres = height,
            BlendSeconds = 0.4,
        };

    /// <summary>Off, the feature is not merely close to invisible — it is the same camera.</summary>
    [Fact]
    public void AtRestItHandsBackTheCameraItWasGiven()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson(0.7f, -0.2f);

        Camera3D drawn = camera.Update(0.016f, wanted: false, eye);

        Assert.Equal(eye.Position.X, drawn.Position.X);
        Assert.Equal(eye.Position.Y, drawn.Position.Y);
        Assert.Equal(eye.Position.Z, drawn.Position.Z);
        Assert.Equal(eye.Orientation, drawn.Orientation);
        Assert.False(camera.IsEngaged);
        Assert.Equal(0.0, camera.OffsetInMetres);
    }

    /// <summary>The move takes the blend it was asked for, and arrives at the offsets it was asked for.</summary>
    [Fact]
    public void ItReachesTheShoulderOverTheBlendAndNoSooner()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson();

        // Yaw zero looks along +Y, so behind is -Y, right is +X and up is +Z.
        Camera3D half = default;
        for (int frame = 0; frame < 4; frame++)
        {
            half = camera.Update(0.05f, wanted: true, eye);
        }

        Assert.True(camera.Blend > 0.4f && camera.Blend < 0.6f, $"half way through, not {camera.Blend}.");
        Assert.True(
            half.Position.Y > eye.Position.Y - 2.5,
            "half way through the move the camera is not yet at the full distance.");

        for (int frame = 0; frame < 10; frame++)
        {
            camera.Update(0.05f, wanted: true, eye);
        }

        Camera3D arrived = camera.Camera;
        Assert.Equal(1f, camera.Blend);
        Assert.Equal(eye.Position.X + 0.5, arrived.Position.X, 6);
        Assert.Equal(eye.Position.Y - 2.5, arrived.Position.Y, 6);
        Assert.Equal(eye.Position.Z + 0.25, arrived.Position.Z, 6);
    }

    /// <summary>The orientation never moves, which is what stops the move being a swing.</summary>
    [Fact]
    public void TheViewDirectionIsThePlayersThroughout()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson(1.1f, 0.3f);

        for (int frame = 0; frame < 12; frame++)
        {
            Camera3D drawn = camera.Update(0.05f, wanted: true, eye);
            Assert.Equal(eye.Orientation, drawn.Orientation);
        }
    }

    /// <summary>
    /// The easing leaves and arrives at rest, which is the difference between a move and a cut.
    /// </summary>
    /// <remarks>
    /// Stated as the property that matters rather than as the polynomial: the first step out of the eye
    /// and the last step into the shoulder both have to be smaller than a step taken in the middle. A
    /// linear blend fails this, and so does anything that snaps.
    /// </remarks>
    [Fact]
    public void TheMoveLeavesAndArrivesAtRest()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson();

        List<double> offsets = [];
        for (int frame = 0; frame < 8; frame++)
        {
            camera.Update(0.05f, wanted: true, eye);
            offsets.Add(camera.OffsetInMetres);
        }

        double first = offsets[0];
        double middle = offsets[4] - offsets[3];
        double last = offsets[^1] - offsets[^2];

        Assert.True(first < middle, $"the move started abruptly: {first} against {middle}.");
        Assert.True(last < middle, $"the move stopped abruptly: {last} against {middle}.");
    }

    /// <summary>Reversing mid-move turns around where it is rather than restarting.</summary>
    /// <remarks>
    /// The property, stated so it cannot be satisfied by accident: from the same point in the move, the
    /// frame that goes back has to cover the same ground as the frame that would have gone on. That is
    /// what it means for the blend to be a POSITION being driven toward a target rather than an
    /// animation being played — an animation restarted backwards would take its first, eased-in step
    /// instead, and the camera would visibly hesitate every time the key was tapped twice.
    /// </remarks>
    [Fact]
    public void ReversingMidMoveCoversTheSameGroundAsCarryingOn()
    {
        Camera3D eye = FirstPerson();
        ThirdPersonCamera onward = Camera();
        ThirdPersonCamera back = Camera();

        for (int frame = 0; frame < 4; frame++)
        {
            onward.Update(0.05f, wanted: true, eye);
            back.Update(0.05f, wanted: true, eye);
        }

        double midway = onward.OffsetInMetres;
        onward.Update(0.05f, wanted: true, eye);
        back.Update(0.05f, wanted: false, eye);

        Assert.True(back.OffsetInMetres < midway, "asking for first person did not start the way back.");
        Assert.Equal(onward.OffsetInMetres - midway, midway - back.OffsetInMetres, 6);
    }

    /// <summary>A wall behind the player takes the distance away on the frame it arrives.</summary>
    [Fact]
    public void AWallPullsTheEyeInAtOnce()
    {
        ThirdPersonCamera camera = Camera(distance: 3.0, shoulder: 0.0, height: 0.0);
        Camera3D eye = FirstPerson();

        // A wall one metre behind the head, in units, as every world query here is.
        Vector3d wall = new(
            eye.Position.X * ThirdPersonCamera.UnitsPerMetre,
            (eye.Position.Y - 1.0) * ThirdPersonCamera.UnitsPerMetre,
            eye.Position.Z * ThirdPersonCamera.UnitsPerMetre);

        bool Blocked(Vector3d from, Vector3d to, out Vector3d hit)
        {
            hit = wall;
            return to.Y < from.Y;
        }

        for (int frame = 0; frame < 20; frame++)
        {
            camera.Update(0.05f, wanted: true, eye, Blocked);
        }

        Assert.Equal(1f, camera.Blend);
        Assert.Equal(1.0 - ThirdPersonCamera.CollisionMarginInMetres, camera.OffsetInMetres, 6);
    }

    /// <summary>Once the way is clear again the eye takes its time going back out.</summary>
    /// <remarks>
    /// The asymmetry the class exists to get right: the frame a wall arrives, the camera has to be in
    /// front of it or the near plane cuts through it; the frame a wall leaves, releasing the distance at
    /// once fires the camera backwards out of every doorway.
    /// </remarks>
    [Fact]
    public void LeavingAWallReleasesTheDistanceGradually()
    {
        ThirdPersonCamera camera = Camera(distance: 3.0, shoulder: 0.0, height: 0.0);
        Camera3D eye = FirstPerson();
        bool blocking = true;

        bool Blocked(Vector3d from, Vector3d to, out Vector3d hit)
        {
            hit = new Vector3d(
                eye.Position.X * ThirdPersonCamera.UnitsPerMetre,
                (eye.Position.Y - 1.0) * ThirdPersonCamera.UnitsPerMetre,
                eye.Position.Z * ThirdPersonCamera.UnitsPerMetre);
            return blocking;
        }

        for (int frame = 0; frame < 20; frame++)
        {
            camera.Update(0.05f, wanted: true, eye, Blocked);
        }

        double pinned = camera.OffsetInMetres;
        blocking = false;
        camera.Update(0.05f, wanted: true, eye, Blocked);

        double released = camera.OffsetInMetres - pinned;
        Assert.True(released > 0.0, "the camera did not start moving back out.");
        Assert.True(
            released <= (ThirdPersonCamera.RecoveryMetresPerSecond * 0.05) + 1e-6,
            $"the camera sprang back out at {released / 0.05:0.##} m/s.");
    }

    /// <summary>The arms go away, then the character appears, and never both nor neither for long.</summary>
    [Fact]
    public void TheArmsLeaveBeforeTheCharacterArrives()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson();

        Assert.True(camera.ShowsViewModel);
        Assert.False(camera.ShowsBody);

        int armsAway = -1;
        int bodyShown = -1;
        for (int frame = 0; frame < 20; frame++)
        {
            camera.Update(0.02f, wanted: true, eye);
            if (armsAway < 0 && !camera.ShowsViewModel)
            {
                armsAway = frame;
            }

            if (bodyShown < 0 && camera.ShowsBody)
            {
                bodyShown = frame;
            }

            Assert.False(camera.ShowsBody && camera.ShowsViewModel, "the arms and the body were drawn at once.");
        }

        Assert.True(armsAway >= 0 && bodyShown > armsAway, "the character arrived before the arms left.");
    }

    /// <summary>
    /// Backed into a corner the view falls back to the first-person one rather than to the inside of a head.
    /// </summary>
    [Fact]
    public void APinnedCameraHandsTheViewBackToTheArms()
    {
        ThirdPersonCamera camera = Camera(distance: 2.6, shoulder: 0.0, height: 0.0);
        Camera3D eye = FirstPerson();

        // A wall closer to the head than the collision margin, so the camera has nowhere to go at all.
        bool Blocked(Vector3d from, Vector3d to, out Vector3d hit)
        {
            hit = new Vector3d(
                eye.Position.X * ThirdPersonCamera.UnitsPerMetre,
                (eye.Position.Y - (ThirdPersonCamera.CollisionMarginInMetres * 0.5)) * ThirdPersonCamera.UnitsPerMetre,
                eye.Position.Z * ThirdPersonCamera.UnitsPerMetre);
            return to.Y < from.Y;
        }

        for (int frame = 0; frame < 30; frame++)
        {
            camera.Update(0.02f, wanted: true, eye, Blocked);
        }

        Assert.Equal(1f, camera.Blend);
        Assert.Equal(0.0, camera.OffsetInMetres, 6);
        Assert.True(camera.ShowsViewModel, "with nowhere to go the arms should be back.");
        Assert.False(camera.ShowsBody, "a head at the eye must not be drawn.");
    }

    /// <summary>A cut is a cut: the camera is at the eye on the very next frame.</summary>
    [Fact]
    public void ResetPutsTheCameraBackAtTheEye()
    {
        ThirdPersonCamera camera = Camera();
        Camera3D eye = FirstPerson();

        for (int frame = 0; frame < 20; frame++)
        {
            camera.Update(0.05f, wanted: true, eye);
        }

        camera.Reset();
        Camera3D drawn = camera.Update(0.05f, wanted: false, eye);

        Assert.False(camera.IsEngaged);
        Assert.Equal(eye.Position.Y, drawn.Position.Y);
    }

    /// <summary>With no shoulder offset the camera stays on the eye's own sight line.</summary>
    /// <remarks>
    /// Which is the whole reason the offset is a setting: on the sight line, a centred crosshair is
    /// truthful at every range, because the point the round reaches projects to the centre of the screen
    /// whatever its distance. The HUD's aim projection is what buys the offset back.
    /// </remarks>
    [Fact]
    public void WithoutAShoulderOffsetTheEyeStaysOnItsOwnSightLine()
    {
        ThirdPersonCamera camera = Camera(distance: 2.5, shoulder: 0.0, height: 0.0);
        Camera3D eye = FirstPerson(0.9f, 0.15f);

        for (int frame = 0; frame < 20; frame++)
        {
            camera.Update(0.05f, wanted: true, eye);
        }

        Vector3f forward = eye.Orientation.Row3;
        Vector3d drawn = camera.Camera.Position;

        // The camera is exactly the distance behind the eye along the view axis, and nowhere else.
        Assert.Equal(eye.Position.X - (forward.X * 2.5), drawn.X, 6);
        Assert.Equal(eye.Position.Y - (forward.Y * 2.5), drawn.Y, 6);
        Assert.Equal(eye.Position.Z - (forward.Z * 2.5), drawn.Z, 6);
    }
}

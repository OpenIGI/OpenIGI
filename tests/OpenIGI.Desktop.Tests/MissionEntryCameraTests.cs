using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The menu-to-mission shot, held to the things that would be visible if they were wrong: it starts where
/// the menu was, it ends exactly in the player's own eyes, it never jumps in between, and it never stops
/// moving even when it is waiting.
/// </summary>
public sealed class MissionEntryCameraTests
{
    private const double UnitsPerMetre = 4096.0;
    private const float Frame = 1f / 60f;

    [Fact]
    public void ItStartsFromWhereTheMenuActuallyIs()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out _);

        entry.Update(Frame, showcase, null);

        Assert.Equal(MissionEntryPhase.Reveal, entry.Phase);
        Assert.Equal(showcase.Position.X, entry.Pose.Position.X, 6);
        Assert.Equal(showcase.Position.Y, entry.Pose.Position.Y, 6);
        Assert.Equal(showcase.Position.Z, entry.Pose.Position.Z, 6);
    }

    [Fact]
    public void ItEndsExactlyInThePlayersOwnEyes()
    {
        // The no-pop guarantee, and the reason CameraPose.Lerp returns its target verbatim at 1: the last
        // cinematic frame and the first gameplay frame have to be the same camera, not two cameras that
        // are very nearly the same. Compared exactly, not to a tolerance.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        RunToEnd(entry, showcase, landing);

        Assert.Equal(landing.Position.X, entry.Pose.Position.X);
        Assert.Equal(landing.Position.Y, entry.Pose.Position.Y);
        Assert.Equal(landing.Position.Z, entry.Pose.Position.Z);
        Assert.Equal(landing.Yaw, entry.Pose.Yaw);
        Assert.Equal(landing.Pitch, entry.Pose.Pitch);

        // And with the wander already at exactly zero, the CAMERA matches too -- which is the thing the
        // player actually sees.
        Camera3D expected = landing.ToCamera();
        Assert.Equal(expected.Orientation.Row1, entry.Camera.Orientation.Row1);
        Assert.Equal(expected.Orientation.Row2, entry.Camera.Orientation.Row2);
        Assert.Equal(expected.Orientation.Row3, entry.Camera.Orientation.Row3);
    }

    [Fact]
    public void TheCameraNeverJumps()
    {
        // Every seam between phases is a place a shot can tear. Nothing proves smoothness in general, but
        // a frame-to-frame step far larger than its neighbours is exactly what a seam looks like, and
        // that is checkable.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        var steps = new List<double>();
        Vector3d previous = showcase.Position;

        entry.Update(Frame, showcase, landing);
        previous = entry.Pose.Position;

        while (entry.Phase is not (MissionEntryPhase.Chrome or MissionEntryPhase.Done))
        {
            if (entry.WantsPromotion)
            {
                entry.NotifyPromoted();
            }

            if (entry.Phase == MissionEntryPhase.Hold)
            {
                entry.NotifyReady();
            }

            entry.Update(Frame, showcase, landing);
            steps.Add(Distance(previous, entry.Pose.Position));
            previous = entry.Pose.Position;
        }

        double mean = steps.Average();
        double worst = steps.Max();

        // The shot deliberately changes speed -- that is where the weight comes from -- so this is a
        // generous bound. It is still far tighter than a discontinuity, which teleports the camera by
        // something on the order of the whole journey in a single frame.
        Assert.True(worst < mean * 8.0, $"worst step {worst:F3} m against a mean of {mean:F3} m");
    }

    [Fact]
    public void TheShotClimbsAboveBothEnds()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        double highest = double.MinValue;
        RunToEnd(entry, showcase, landing, pose => highest = Math.Max(highest, pose.Position.Z));

        Assert.True(highest > showcase.Position.Z + MissionEntryCamera.MinimumApexRise * 0.9);
        Assert.True(highest > landing.Position.Z + MissionEntryCamera.MinimumApexRise * 0.9);
    }

    [Fact]
    public void HowHighItGoesFollowsHowFarItHasToTravel()
    {
        // A fixed altitude reads wrong at both ends. A short hop should stay low; a move across a whole
        // level should climb -- and both should stop at the declared bounds.
        double near = ApexFor(60.0);
        double far = ApexFor(900.0);
        double absurd = ApexFor(20000.0);

        Assert.True(far > near);
        Assert.Equal(MissionEntryCamera.MinimumApexRise, near, 1);
        Assert.Equal(MissionEntryCamera.MaximumApexRise, absurd, 1);
    }

    [Fact]
    public void TheHoldWaitsForTheMissionButKeepsMoving()
    {
        // The whole point of the beat: while it is waiting for the level to be brought up, the picture
        // must not be frozen. A wait should read as a longer shot, never as the game having stopped.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        // The mission is brought up as the travel begins, where a dropped frame is least visible...
        bool promotionWanted = false;
        while (entry.Phase != MissionEntryPhase.Hold)
        {
            entry.Update(Frame, showcase, landing);
            if (entry.WantsPromotion)
            {
                Assert.Equal(MissionEntryPhase.Travel, entry.Phase);
                promotionWanted = true;
                entry.NotifyPromoted();
            }
        }

        Assert.True(promotionWanted);
        Assert.False(entry.WantsPromotion);

        Vector3d previous = entry.Pose.Position;
        int frames = 0;
        for (int i = 0; i < 120; i++)
        {
            entry.Update(Frame, showcase, landing);
            Assert.Equal(MissionEntryPhase.Hold, entry.Phase);
            Assert.NotEqual(previous, entry.Pose.Position);
            previous = entry.Pose.Position;
            frames++;
        }

        Assert.Equal(120, frames);

        // ...and the hold waits for the picture to be final, not merely for the level to be up. Once it
        // is, the shot goes on to the descent rather than sitting there.
        entry.NotifyReady();
        entry.Update(Frame, showcase, landing);
        entry.Update(Frame, showcase, landing);
        Assert.Equal(MissionEntryPhase.Descend, entry.Phase);
    }

    [Fact]
    public void AHoldThatIsNeverPromotedGivesUpRatherThanHangingForever()
    {
        // An honest hitch is better than a camera that never lands. If the mission somehow never comes
        // up, the shot still finishes.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        for (int i = 0; i < 60 * 40; i++)
        {
            entry.Update(Frame, showcase, landing);
        }

        Assert.Equal(MissionEntryPhase.Done, entry.Phase);
    }

    [Fact]
    public void TheInterfaceIsGoneBeforeTheCameraCommits()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        float atStart = entry.ShellOpacity;
        while (entry.Phase == MissionEntryPhase.Reveal)
        {
            entry.Update(Frame, showcase, landing);
        }

        Assert.Equal(1f, atStart);
        Assert.Equal(0f, entry.ShellOpacity);

        // And it stays gone for the rest of the shot.
        RunToEnd(entry, showcase, landing, _ => Assert.Equal(0f, entry.ShellOpacity));
    }

    [Fact]
    public void TheFramingComesInAndGoesBackOut()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        float widest = 0f;
        RunToEnd(entry, showcase, landing, _ => widest = MathF.Max(widest, entry.LetterboxAmount));

        Assert.Equal(1f, widest, 3);
        Assert.Equal(0f, entry.LetterboxAmount, 3);
    }

    [Fact]
    public void TheStandingBodyIsShownThroughTheDiveAndGoneByTheEnd()
    {
        // The beat the shot is built around: the character is there to be dived at, and by the time the
        // view is handed over it is the player rather than something in front of them.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        bool shownDuringDescent = false;
        bool shownOutsideDescent = false;

        RunToEnd(entry, showcase, landing, _ =>
        {
            if (!entry.ShowPlayerBody)
            {
                return;
            }

            if (entry.Phase == MissionEntryPhase.Descend)
            {
                shownDuringDescent = true;
            }
            else
            {
                shownOutsideDescent = true;
            }
        });

        Assert.True(shownDuringDescent);
        Assert.False(shownOutsideDescent);
        Assert.False(entry.ShowPlayerBody);
    }

    [Fact]
    public void TheBodyIsNeverDrawnFromInsideItself()
    {
        // The descent ends AT the eye, and the eye is inside the character's head. The soldier renderer
        // does not cull back faces, so a frame drawn from within the model fills the whole screen with
        // the inside of it -- which is what "the player flashes over the whole screen" was. The invariant
        // that prevents it is simply that the body is never shown once the camera is within arm's reach.
        //
        // The landing wobbles here because it is re-read every frame from a live body settling onto the
        // ground, and the rule has to hold against that rather than only against a still target.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        bool everShown = false;
        double closestWhileShown = double.MaxValue;
        int wobble = 0;

        for (int i = 0; i < 60 * 20 && entry.Phase != MissionEntryPhase.Done; i++)
        {
            if (entry.WantsPromotion)
            {
                entry.NotifyPromoted();
            }

            if (entry.Phase == MissionEntryPhase.Hold)
            {
                entry.NotifyReady();
            }

            double jitter = ((wobble++ % 2) == 0 ? 0.35 : -0.35);
            var unsettled = landing with
            {
                Position = new Vector3d(
                    landing.Position.X,
                    landing.Position.Y,
                    landing.Position.Z + jitter),
            };

            entry.Update(Frame, showcase, unsettled);

            if (!entry.ShowPlayerBody)
            {
                continue;
            }

            everShown = true;
            closestWhileShown = Math.Min(closestWhileShown, Distance(entry.Pose.Position, unsettled.Position));
        }

        Assert.True(everShown, "the character was never shown at all");

        // Not merely outside the model — outside the distance at which it would be rushing up at the
        // lens. The descent's last stretch is fast, so a body still drawn at close range sweeps from
        // waist-height to full-screen and vanishes inside a few frames, which reads as a flash.
        Assert.True(
            closestWhileShown > 4.0,
            $"the body was still being drawn {closestWhileShown:F2} m from the eye, close enough to flash");
    }

    [Fact]
    public void TheSmearIsGoneBeforeThePlayerHasControl()
    {
        // Motion blur is the shot's own, not the player's. A smear still running on the first frame they
        // control is the cinematic overstaying -- and because the value is pushed into a cvar the player
        // also owns, "nearly zero" would leave their setting quietly altered.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        float peak = 0f;
        RunToEnd(entry, showcase, landing, _ => peak = MathF.Max(peak, entry.MotionBlur));

        Assert.True(peak > 0.2f, $"the shot barely smeared at all (peak {peak:F2})");
        Assert.Equal(0f, entry.MotionBlur);
    }

    [Fact]
    public void TheShotFocusesOnWhatItIsLookingAt()
    {
        // The menu focuses at a fixed forty metres, which once the camera has climbed is far short of the
        // ground and blurs the whole world evenly. While the shot is still letting go of that framing its
        // focus has to follow the distance to its actual subject instead.
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        float whileClimbing = 0f;
        float onceClear = -1f;
        RunToEnd(entry, showcase, landing, _ =>
        {
            if (entry.Phase == MissionEntryPhase.Ascend)
            {
                whileClimbing = MathF.Max(whileClimbing, entry.FocalDistanceMetres);
            }
            else if (entry.Phase == MissionEntryPhase.Travel)
            {
                onceClear = MathF.Max(onceClear, entry.FocalDistanceMetres);
            }
        });

        // Climbing away from the menu's forty metres, the plane follows the subject out.
        Assert.True(whileClimbing > 80f, $"focused at only {whileClimbing:F0} m during the climb");

        // And once the menu's framing has fully eased off, no focal plane at all — the defocus gather is
        // turned off outright rather than left running against a plane nothing is behind.
        Assert.Equal(0f, onceClear);
        Assert.Equal(0f, entry.FocalDistanceMetres);
    }

    [Fact]
    public void TheMenusDepthOfFieldIsFullyOffBeforeTheDescentStarts()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);

        while (entry.Phase != MissionEntryPhase.Descend)
        {
            if (entry.WantsPromotion)
            {
                entry.NotifyPromoted();
            }

            if (entry.Phase == MissionEntryPhase.Hold)
            {
                entry.NotifyReady();
            }

            entry.Update(Frame, showcase, landing);
        }

        Assert.Equal(0f, entry.MenuDepthOfField);
    }

    [Fact]
    public void AShotThatOpensAtAltitudeHoldsOverTheStartAndLandsInTheEyes()
    {
        // A cold start or a restart has no orbit to rise out of: the shot begins at its hold, with the
        // interface already gone and the bars already in, and lands exactly where every other route does.
        var entry = new MissionEntryCamera();
        var landing = new CameraPose(new Vector3d(120.0, -40.0, 12.5), 0.7f, -0.05f);

        entry.BeginAtHold(landing, groundHeightMetres: 10.0);

        Assert.Equal(MissionEntryPhase.Hold, entry.Phase);
        Assert.Equal(0f, entry.ShellOpacity, 3);
        Assert.Equal(1f, entry.LetterboxAmount, 3);
        Assert.True(entry.Pose.Position.Z > landing.Position.Z + MissionEntryCamera.MinimumApexRise - 1.0);
        Assert.Equal(landing.Position.X, entry.TitleAnchor.X, 6);
        Assert.Equal(landing.Yaw, entry.TitleYaw, 6);

        // It waits for the picture to be final, moving all the while...
        Vector3d previous = entry.Pose.Position;
        for (int i = 0; i < 90; i++)
        {
            entry.Update(Frame, entry.Pose, landing);
            Assert.Equal(MissionEntryPhase.Hold, entry.Phase);
            Assert.NotEqual(previous, entry.Pose.Position);
            previous = entry.Pose.Position;
        }

        // ...and then descends into the player's own eyes.
        entry.NotifyReady();
        RunToEnd(entry, entry.Pose, landing);
        Assert.Equal(MissionEntryPhase.Done, entry.Phase);
        Assert.Equal(landing.Position.X, entry.Pose.Position.X);
        Assert.Equal(landing.Position.Z, entry.Pose.Position.Z);
    }

    [Fact]
    public void AMissionWithItsOwnIntroEndsTheShotOverTheStart()
    {
        var entry = new MissionEntryCamera();
        var landing = new CameraPose(new Vector3d(0.0, 0.0, 5.0), 0f, 0f);

        entry.BeginAtHold(landing, groundHeightMetres: 0.0, descend: false);
        entry.NotifyReady();
        RunToEnd(entry, entry.Pose, landing);

        // Finished, and still at altitude: the authored camera takes the frame from here.
        Assert.Equal(MissionEntryPhase.Done, entry.Phase);
        Assert.False(entry.Descends);
        Assert.True(entry.Pose.Position.Z > landing.Position.Z + 50.0);
    }

    [Fact]
    public void ResettingPutsItBackToIdle()
    {
        MissionEntryCamera entry = Begin(out CameraPose showcase, out CameraPose landing);
        entry.Update(Frame, showcase, landing);

        entry.Reset();

        Assert.Equal(MissionEntryPhase.Idle, entry.Phase);
        Assert.False(entry.IsCameraActive);
        Assert.False(entry.IsRunning);
    }

    /// <summary>The vantage's own height above the ground, sampled the moment the travel begins.</summary>
    /// <remarks>
    /// Read at the start of the travel rather than as the highest point of the whole shot, because the
    /// travel deliberately arches over its middle and the hold deliberately drifts upward — so the peak
    /// is the vantage plus both of those, and it is the vantage that the clamps are about.
    /// </remarks>
    private static double ApexFor(double metresApart)
    {
        var entry = new MissionEntryCamera();
        var showcase = new CameraPose(new Vector3d(0.0, 0.0, 20.0), 0.4f, -0.2f);
        var spawn = new Vector3d(metresApart * UnitsPerMetre, 0.0, 0.0);

        entry.Begin(showcase, spawn, groundHeightMetres: 0.0);

        var landing = new CameraPose(new Vector3d(metresApart, 0.0, 1.8), 0.9f, 0f);

        while (entry.Phase != MissionEntryPhase.Travel)
        {
            entry.Update(Frame, showcase, landing);
        }

        // Measured above the higher of the two ends, which is what the rise is added to.
        return entry.Pose.Position.Z - Math.Max(showcase.Position.Z, 0.0);
    }

    private static MissionEntryCamera Begin(out CameraPose showcase, out CameraPose landing)
    {
        var entry = new MissionEntryCamera();

        showcase = new CameraPose(new Vector3d(120.0, -80.0, 55.0), 0.6f, -0.35f);
        var spawn = new Vector3d(300.0 * UnitsPerMetre, 210.0 * UnitsPerMetre, 12.0 * UnitsPerMetre);
        landing = new CameraPose(new Vector3d(300.0, 210.0, 13.8), -2.1f, 0f);

        entry.Begin(showcase, spawn, groundHeightMetres: 10.0);
        return entry;
    }

    private static void RunToEnd(
        MissionEntryCamera entry,
        in CameraPose showcase,
        in CameraPose landing,
        Action<CameraPose>? each = null)
    {
        CameraPose orbit = showcase;
        CameraPose target = landing;

        for (int i = 0; i < 60 * 20 && entry.Phase != MissionEntryPhase.Done; i++)
        {
            if (entry.WantsPromotion)
            {
                entry.NotifyPromoted();
            }

            if (entry.Phase == MissionEntryPhase.Hold)
            {
                entry.NotifyReady();
            }

            entry.Update(Frame, orbit, target);
            each?.Invoke(entry.Pose);
        }
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The interval the original's clock has just covered, which a port clip, reading one step behind, covers next.
/// </summary>
public sealed class BoneClipUpcomingEventTests
{
    private const int Footstep = 1010;

    [Fact]
    public void AFreshClipReportsTheEventItsFirstStepWillCross()
    {
        // Mission 4's walk: the original's clock read 160 after the tick a patrol set off, with the step dispatched
        // that tick; this clip reads 0 then, so the event at 160 is the next step's.
        BoneClip clip = new(Walk(eventAt: BoneAnimation.FrameStep), bone: 0, loops: true);

        Assert.True(clip.UpcomingEvent(Footstep));
        clip.Tick();
        Assert.False(clip.UpcomingEvent(Footstep));
    }

    [Fact]
    public void AnEventIsReportedOnceAsTheClockApproachesIt()
    {
        BoneClip clip = new(Walk(eventAt: 5 * BoneAnimation.FrameStep), bone: 0, loops: true);
        int reports = 0;
        int reportedAt = -1;
        for (int tick = 0; tick < 10; tick++)
        {
            if (clip.UpcomingEvent(Footstep))
            {
                reports++;
                reportedAt = tick;
            }

            clip.Tick();
        }

        Assert.Equal(1, reports);
        Assert.Equal(4, reportedAt);
    }

    [Fact]
    public void ALoopingClipReportsAnEventAcrossItsSeam()
    {
        // Duration 8 steps; an event at step 0 of the next cycle is upcoming from the cycle's last step.
        BoneClip clip = new(Walk(eventAt: 0f, steps: 8), bone: 0, loops: true);
        for (int tick = 0; tick < 7; tick++)
        {
            clip.Tick();
        }

        Assert.Equal(7 * BoneAnimation.FrameStep, clip.Time);
        Assert.True(clip.UpcomingEvent(Footstep));
    }

    private static BoneAnimation Walk(float eventAt, int steps = 32) =>
        BoneAnimation.Create(
            id: 57,
            duration: steps * BoneAnimation.FrameStep,
            rotations: [[]],
            events: [new BoneAnimationEvent(Footstep, 0, eventAt, default(Vector3f))]);
}

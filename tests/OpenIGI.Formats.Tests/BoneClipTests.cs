using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Checks the clip clock against <c>0x4D4B60</c>.</summary>
public sealed class BoneClipTests
{
    [Fact]
    public void AClipAdvancesOneFrameStepPerTick()
    {
        BoneClip clip = new(Animation(duration: 480f), bone: 0);

        clip.Tick();
        clip.Tick();

        Assert.Equal(2f * BoneAnimation.FrameStep, clip.Time, 3);
        Assert.False(clip.Ended);
    }

    [Fact]
    public void AOneShotClipStopsAtItsDuration()
    {
        BoneClip clip = new(Animation(duration: 320f), bone: 0);

        clip.Tick();
        clip.Tick();
        clip.Tick();

        Assert.Equal(320f, clip.Time, 3);
        Assert.True(clip.Ended);
    }

    [Fact]
    public void ALoopingClipSubtractsItsDurationRatherThanTakingARemainder()
    {
        BoneClip clip = new(Animation(duration: 240f), bone: 0, loops: true);

        clip.Tick();
        Assert.False(clip.EndedThisTick);

        clip.Tick();

        // 320 past the start, duration 240, so the second tick lands at 80 -- and the original really
        // does subtract once rather than modulo, which is visible whenever a rate exceeds a duration.
        Assert.Equal(80f, clip.Time, 3);
        Assert.True(clip.Ended);
        Assert.True(clip.EndedThisTick);
    }

    [Fact]
    public void ABackwardsClipStartsAtTheEndAndStopsAtZero()
    {
        BoneClip clip = new(Animation(duration: 320f), bone: 0, backwards: true);

        Assert.Equal(320f, clip.Time, 3);

        clip.Tick();
        clip.Tick();
        clip.Tick();

        Assert.Equal(0f, clip.Time, 3);
        Assert.True(clip.Ended);
    }

    [Fact]
    public void ALoopingClipReportsAuthoredEventsInBothDirections()
    {
        BoneAnimation animation = BoneAnimation.Create(
            2,
            320f,
            [[Key(0f, 0f), Key(320f, 0f)]],
            events:
            [
                new BoneAnimationEvent(8, BoneAnimationEvent.NoBone, 0f, default),
                new BoneAnimationEvent(8, BoneAnimationEvent.NoBone, 160f, default),
            ]);
        BoneClip clip = new(animation, bone: 0, loops: true);

        clip.Rate = 0f;
        clip.Tick();
        Assert.False(clip.CrossedEvent(8));

        clip.Rate = BoneAnimation.FrameStep;
        clip.Tick();
        Assert.True(clip.CrossedEvent(8));

        clip.Tick();
        Assert.True(clip.CrossedEvent(8)); // wrapped through the event at time zero

        clip.Rate = -BoneAnimation.FrameStep;
        clip.Tick();
        Assert.True(clip.CrossedEvent(8)); // reverse-wrapped through time zero
    }

    [Fact]
    public void AnArmedClipReportsTheEventAtItsStartOnItsFirstTickOnly()
    {
        // 0x4D47D0 gives every new clip node flag 0x8000; 0x4D4B60's first tick then also asks for the
        // events lying exactly at the old clock, which a forward interval (open at the start) never would.
        BoneAnimation animation = BoneAnimation.Create(
            2,
            320f,
            [[Key(0f, 0f), Key(320f, 0f)]],
            events:
            [
                new BoneAnimationEvent(8, BoneAnimationEvent.NoBone, 0f, default),
                new BoneAnimationEvent(8, BoneAnimationEvent.NoBone, 160f, default),
            ]);

        BoneClip unarmed = new(animation, bone: 0, loops: true) { Rate = 0f };
        unarmed.Tick();
        Assert.False(unarmed.CrossedEvent(8));

        BoneClip held = new(animation, bone: 0, loops: true) { Rate = 0f };
        held.ArmStartEvents();
        held.Tick();
        Assert.True(held.TryGetCrossedEvent(8, out float mountTime));
        Assert.Equal(0f, mountTime);
        held.Tick();
        Assert.False(held.CrossedEvent(8)); // the flag is spent; a held clip is silent again

        BoneClip moving = new(animation, bone: 0, loops: true);
        moving.ArmStartEvents();
        moving.Tick();
        Assert.True(moving.TryGetCrossedEvent(8, out float first));
        Assert.Equal(0f, first); // the start point is dispatched before the interval's own 160
    }

    [Fact]
    public void EveryBoneSamplesItsOwnTrack()
    {
        // One clip drives a whole rig because the sampler indexes by the ASKING bone, not by the bone
        // the clip was started on. Track 1 turns and track 0 does not, so the two differ.
        BoneRotationKey[][] tracks =
        [
            [Key(0f, 0f), Key(320f, 0f)],
            [Key(0f, 0f), Key(320f, MathF.PI / 2f)],
        ];

        BoneClip clip = new(BoneAnimation.Create(2, 320f, tracks), bone: 0);
        clip.Tick();
        clip.Tick();

        Assert.Equal(0f, clip.SampleRotation(0).Z, 4);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), clip.SampleRotation(1).Z, 4);
    }

    private static BoneAnimation Animation(float duration) =>
        BoneAnimation.Create(2, duration, [[Key(0f, 0f), Key(duration, 0f)]]);

    private static BoneRotationKey Key(float time, float angle)
    {
        Quaternion32 q = new(0f, 0f, MathF.Sin(angle / 2f), MathF.Cos(angle / 2f));
        return new BoneRotationKey(q, time, q, q);
    }
}

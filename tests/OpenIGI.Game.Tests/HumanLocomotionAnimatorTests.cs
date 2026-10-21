using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The animator that replaces a walking-speed constant.
/// </summary>
public sealed class HumanLocomotionAnimatorTests
{
    private const double UnitsPerMetre = 4096.0;

    [RequiresGameDataFact]
    public void AWalkCoversExactlyItsAnimationsOwnTravel()
    {
        // The whole point: how far a step goes is a property of the clip, not of a constant. Playing a
        // looping state for exactly its duration must cover exactly what the track covers.
        BoneAnimationSet set = PlayerAnimations();
        HumanLocomotionAnimator animator = new(set);

        animator.Enter(1);
        Assert.True(set.TryGet(animator.AnimationId, out BoneAnimation clip));

        Vector3f start = BoneTrackSampler.Sample(clip.Translation, 0f);
        Vector3f end = BoneTrackSampler.Sample(clip.Translation, clip.Duration);

        int ticks = (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick);
        Vector3f total = default;
        for (int i = 0; i < ticks; i++)
        {
            Vector3f step = animator.Advance();
            total = new Vector3f(total.X + step.X, total.Y + step.Y, total.Z + step.Z);
        }

        Assert.Equal(end.X - start.X, total.X, 1f);
        Assert.Equal(end.Y - start.Y, total.Y, 1f);
    }

    [RequiresGameDataFact]
    public void TheFourDirectionsTravelTheWayTheirStatesSay()
    {
        // This is what named the motion flags: state 1 goes forward, 4 back, 7 right, 8 left. If the
        // state table or the bit naming were wrong, these would disagree.
        BoneAnimationSet set = PlayerAnimations();

        Assert.True(Travel(set, 1).Y > 0.5 * UnitsPerMetre, "state 1 should walk forward");
        Assert.True(Travel(set, 4).Y < -0.5 * UnitsPerMetre, "state 4 should walk backward");
        Assert.True(Travel(set, 7).X > 0.5 * UnitsPerMetre, "state 7 should strafe right");
        Assert.True(Travel(set, 8).X < -0.5 * UnitsPerMetre, "state 8 should strafe left");

        // The crouched four go the same ways, more slowly.
        Assert.True(Travel(set, 9).Y > 0f && Travel(set, 9).Y < Travel(set, 1).Y);
        Assert.True(Travel(set, 12).Y < 0f);
        Assert.True(Travel(set, 15).X > 0f && Travel(set, 15).X < Travel(set, 7).X);
        Assert.True(Travel(set, 16).X < 0f);
    }

    [RequiresGameDataFact]
    public void TheResultingSpeedsAreOnesAPersonWouldRecognise()
    {
        // A sanity check on the whole chain rather than on any one number: with the 1.75 scale applied,
        // forward movement should land in the range a running soldier occupies, and crouching should be
        // markedly slower.
        BoneAnimationSet set = PlayerAnimations();

        double forward = SpeedMetresPerSecond(set, 1);
        double crouched = SpeedMetresPerSecond(set, 9);

        Assert.InRange(forward, 3.0, 9.0);
        Assert.True(crouched < forward * 0.8, $"crouched {crouched:0.##} m/s against standing {forward:0.##}");
    }

    [RequiresGameDataFact]
    public void ALoopKeepsCoveringGroundAcrossItsSeam()
    {
        // The extra key past the duration exists so a cycle does not lose the last frame's travel or
        // jump backwards when it repeats. Three cycles must cover three cycles' worth.
        BoneAnimationSet set = PlayerAnimations();
        HumanLocomotionAnimator animator = new(set);
        animator.Enter(1);

        Assert.True(set.TryGet(animator.AnimationId, out BoneAnimation clip));
        Assert.True(animator.Loops);

        int perCycle = (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick);
        double one = 0.0;
        for (int i = 0; i < perCycle; i++)
        {
            one += animator.Advance().Y;
        }

        double rest = 0.0;
        for (int i = 0; i < perCycle * 2; i++)
        {
            rest += animator.Advance().Y;
        }

        // Two more cycles cover twice what one did, within a frame's worth of slack.
        Assert.Equal(2.0 * one, rest, one / perCycle);
    }

    [RequiresGameDataFact]
    public void ReEnteringTheSameStateDoesNotRestartTheCycle()
    {
        BoneAnimationSet set = PlayerAnimations();
        HumanLocomotionAnimator animator = new(set);

        animator.Enter(1);
        animator.Advance();
        animator.Advance();
        float time = animator.Time;

        animator.Enter(1);
        Assert.Equal(time, animator.Time);

        // A different state does restart it.
        animator.Enter(4);
        Assert.Equal(0f, animator.Time);
    }

    [RequiresGameDataFact]
    public void AStateWithNoAnimationCoversNoGround()
    {
        // Several states drive themselves rather than naming a clip; they must not move the body.
        BoneAnimationSet set = PlayerAnimations();
        HumanLocomotionAnimator animator = new(set);

        animator.Enter(30);
        Assert.Equal(HumanLocomotionStates.NoAnimation, animator.AnimationId);
        Assert.Equal(default, animator.Advance());
    }

    private static Vector3f Travel(BoneAnimationSet set, int state)
    {
        HumanLocomotionAnimator animator = new(set);
        animator.Enter(state);

        if (!set.TryGet(animator.AnimationId, out BoneAnimation clip))
        {
            return default;
        }

        int ticks = Math.Max(1, (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick));
        Vector3f total = default;
        for (int i = 0; i < ticks; i++)
        {
            Vector3f step = animator.Advance();
            total = new Vector3f(total.X + step.X, total.Y + step.Y, total.Z + step.Z);
        }

        return total;
    }

    private static double SpeedMetresPerSecond(BoneAnimationSet set, int state)
    {
        HumanLocomotionAnimator animator = new(set);
        animator.Enter(state);
        set.TryGet(animator.AnimationId, out BoneAnimation clip);

        int ticks = Math.Max(1, (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick));
        Vector3f travel = Travel(set, state);
        double distance = Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y));

        return distance * HumanMotion.DefaultDeltaTranslationScale / ticks * 30.0 / UnitsPerMetre;
    }

    private static BoneAnimationSet PlayerAnimations() =>
        BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));
}

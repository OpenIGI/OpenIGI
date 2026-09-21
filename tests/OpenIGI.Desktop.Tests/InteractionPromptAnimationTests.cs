using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks down the interaction marker's fade, which has to survive the frames with no offer.</summary>
public sealed class InteractionPromptAnimationTests
{
    [Fact]
    public void NothingIsDrawnBeforeAnythingIsOffered()
    {
        InteractionPromptAnimation animation = new();

        animation.Update(target: null, elapsedSeconds: 1f);

        Assert.Null(animation.Visible);
        Assert.Equal(0f, animation.Opacity);
    }

    [Fact]
    public void AnOfferFadesUpOverItsOwnWallClock()
    {
        InteractionPromptAnimation animation = new();

        Advance(animation, Target(id: 7), InteractionPromptAnimation.FadeInSeconds * 0.5f);

        Assert.NotNull(animation.Visible);
        Assert.Equal(0.5f, animation.Opacity, 3);

        Advance(animation, Target(id: 7), InteractionPromptAnimation.FadeInSeconds);

        Assert.Equal(1f, animation.Opacity);
    }

    [Fact]
    public void TheLastOfferIsHeldOnScreenWhileItFadesAway()
    {
        InteractionPromptAnimation animation = new();
        Advance(animation, Target(id: 7), 1f);

        Advance(animation, target: null, InteractionPromptAnimation.FadeOutSeconds * 0.5f);

        // Still drawn, and still the door it was pinned to -- otherwise the marker would vanish the
        // instant the player's aim left it rather than falling away from it.
        Assert.NotNull(animation.Visible);
        Assert.Equal(7, animation.Visible!.Value.Id);
        Assert.Equal(0.5f, animation.Opacity, 3);

        Advance(animation, target: null, InteractionPromptAnimation.FadeOutSeconds);

        Assert.Null(animation.Visible);
        Assert.Equal(0f, animation.Opacity);
    }

    [Fact]
    public void AimingAtADifferentTaskRestartsTheEntryPop()
    {
        InteractionPromptAnimation animation = new();
        Advance(animation, Target(id: 7), 1f);
        Assert.Equal(1f, animation.Pop, 3);

        animation.Update(Target(id: 9), 0f);

        Assert.Equal(0f, animation.Pop, 3);
        Assert.Equal(9, animation.Visible!.Value.Id);

        // The fade does not restart with it: the marker moves to the new object at full opacity rather
        // than blinking through nothing on the way.
        Assert.Equal(1f, animation.Opacity);
    }

    [Fact]
    public void TheSameTaskKeepsItsPopWhileItsProgressAdvances()
    {
        InteractionPromptAnimation animation = new();
        Advance(animation, Target(id: 7), 1f);

        animation.Update(Target(id: 7, progress: 0.4f), 0.016f);

        Assert.Equal(1f, animation.Pop, 3);
        Assert.Equal(0.4f, animation.Visible!.Value.Progress);
    }

    [Fact]
    public void AHitchCannotSkipTheAnimation()
    {
        InteractionPromptAnimation animation = new();

        // A two-second frame advances the fade by at most one clamped step, exactly as the optics clock
        // does: a stall must not make the marker appear to have been there all along.
        animation.Update(Target(id: 7), 2f);

        Assert.True(animation.Opacity < 1f);
    }

    [Fact]
    public void ResetDropsTheMarkerOutright()
    {
        InteractionPromptAnimation animation = new();
        Advance(animation, Target(id: 7), 1f);

        animation.Reset();

        Assert.Null(animation.Visible);
        Assert.Equal(0f, animation.Opacity);
        Assert.Equal(0f, animation.Pop, 3);
    }

    /// <summary>Runs the clock forward in ordinary frame-sized steps, as a real session would.</summary>
    private static void Advance(
        InteractionPromptAnimation animation,
        InteractionPromptTarget? target,
        float seconds,
        int steps = 64)
    {
        for (int i = 0; i < steps; i++)
        {
            animation.Update(target, seconds / steps);
        }
    }

    private static InteractionPromptTarget Target(int id, float? progress = null) => new(
        id,
        InteractionActivateType.Door,
        "a_door.spr",
        new Vector3d(4096.0, 4096.0, 0.0),
        DurationTicks: 0,
        Locked: false,
        DoorOpen: false,
        progress);
}

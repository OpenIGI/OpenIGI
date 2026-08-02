using OpenIGI.UI.Animation;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Covers the tween engine, springs, timelines and the widget state machine — all pure logic.</summary>
public sealed class AnimationTests
{
    [Theory]
    [InlineData(Ease.Linear)]
    [InlineData(Ease.InQuad)]
    [InlineData(Ease.OutQuad)]
    [InlineData(Ease.InOutCubic)]
    [InlineData(Ease.OutQuint)]
    public void Easing_curves_pin_their_endpoints(Ease ease)
    {
        Assert.Equal(0f, Easing.Evaluate(ease, 0f), 5);
        Assert.Equal(1f, Easing.Evaluate(ease, 1f), 5);
    }

    [Fact]
    public void Easing_clamps_out_of_range_progress()
    {
        Assert.Equal(0f, Easing.Evaluate(Ease.OutCubic, -2f), 5);
        Assert.Equal(1f, Easing.Evaluate(Ease.OutCubic, 5f), 5);
    }

    [Fact]
    public void Linear_easing_is_the_identity()
    {
        Assert.Equal(0.25f, Easing.Evaluate(Ease.Linear, 0.25f), 5);
        Assert.Equal(0.5f, Easing.Evaluate(Ease.Linear, 0.5f), 5);
    }

    [Fact]
    public void Tween_starts_settled_at_its_value()
    {
        var tween = new Tween(3f);
        Assert.Equal(3f, tween.Value, 5);
        Assert.True(tween.IsComplete);
    }

    [Fact]
    public void Tween_reaches_its_target_after_its_duration()
    {
        var tween = new Tween(0f, duration: 1f, Ease.Linear);
        tween.Retarget(10f);

        tween.Update(0.5f);
        Assert.Equal(5f, tween.Value, 3);

        tween.Update(0.5f);
        Assert.Equal(10f, tween.Value, 3);
        Assert.True(tween.IsComplete);
    }

    [Fact]
    public void Tween_delay_holds_the_start_value()
    {
        var tween = new Tween(0f, duration: 1f, Ease.Linear);
        tween.Retarget(10f, delay: 0.5f);

        tween.Update(0.5f);
        Assert.Equal(0f, tween.Value, 3);

        tween.Update(0.5f);
        Assert.Equal(5f, tween.Value, 3);
    }

    [Fact]
    public void Tween_retarget_eases_on_from_the_current_value()
    {
        var tween = new Tween(0f, duration: 1f, Ease.Linear);
        tween.Retarget(10f);
        tween.Update(0.5f); // now at 5

        tween.Retarget(0f, duration: 1f);
        Assert.Equal(5f, tween.From, 3);
        tween.Update(0.5f);
        Assert.Equal(2.5f, tween.Value, 3);
    }

    [Fact]
    public void Spring_converges_on_its_target_and_never_overshoots()
    {
        var spring = new Spring(0f, smoothTime: 0.1f) { Target = 1f };
        float previous = 0f;
        for (int i = 0; i < 100; i++)
        {
            float value = spring.Update(1f / 60f);
            Assert.True(value <= 1.0001f, $"spring overshot to {value}");
            Assert.True(value >= previous - 0.0001f, "spring moved backwards");
            previous = value;
        }

        Assert.Equal(1f, spring.Value, 2);
    }

    [Fact]
    public void Spring_snap_clears_velocity()
    {
        var spring = new Spring(0f, smoothTime: 0.2f) { Target = 5f };
        spring.Update(0.1f);
        spring.SnapTo(2f);
        Assert.Equal(2f, spring.Value, 5);
        Assert.Equal(0f, spring.Velocity, 5);
    }

    [Fact]
    public void Timeline_stagger_delays_grow_per_item()
    {
        Assert.Equal(0f, Timeline.StaggerDelay(0, 0.05f), 5);
        Assert.Equal(0.05f, Timeline.StaggerDelay(1, 0.05f), 5);
        Assert.Equal(0.1f, Timeline.StaggerDelay(2, 0.05f), 5);
        Assert.Equal(0.35f, Timeline.StaggerDelay(3, 0.05f, baseDelay: 0.2f), 5);
    }

    [Fact]
    public void Timeline_completes_only_once_every_tween_has()
    {
        var timeline = new Timeline();
        float captured = -1f;
        var early = new Tween(0f, duration: 0.5f, Ease.Linear);
        var late = new Tween(0f, duration: 0.5f, Ease.Linear);
        early.Retarget(1f, delay: 0f);
        late.Retarget(1f, delay: 0.5f);
        timeline.Add(early);
        timeline.Add(late, v => captured = v);

        timeline.Update(0.5f);
        Assert.False(timeline.IsComplete); // late has not started
        Assert.Equal(0f, captured, 3);

        timeline.Update(0.5f);
        timeline.Update(0.001f);
        Assert.True(timeline.IsComplete);
        Assert.Equal(1f, captured, 2);
    }

    [Fact]
    public void Widget_state_machine_appears_then_focuses()
    {
        var machine = new WidgetVisualStateMachine();
        machine.Enter();
        Assert.Equal(WidgetVisualState.Enter, machine.State);

        Advance(machine, 1f);
        Assert.Equal(1f, machine.Appear, 2);
        Assert.Equal(WidgetVisualState.Idle, machine.State);

        machine.SetFocused(true);
        Advance(machine, 1f);
        Assert.Equal(1f, machine.Focus, 2);
        Assert.Equal(WidgetVisualState.Focused, machine.State);

        machine.SetPressed(true);
        Advance(machine, 1f);
        Assert.Equal(WidgetVisualState.Pressed, machine.State);
    }

    [Fact]
    public void Widget_state_machine_exit_eventually_reports_gone()
    {
        var machine = new WidgetVisualStateMachine();
        machine.AppearInstantly();
        machine.Exit();
        Advance(machine, 2f);
        Assert.True(machine.IsGone);
        Assert.Equal(0f, machine.Appear, 2);
    }

    private static void Advance(WidgetVisualStateMachine machine, float seconds)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            machine.Update(1f / 60f);
        }
    }
}

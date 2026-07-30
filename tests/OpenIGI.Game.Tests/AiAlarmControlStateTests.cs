using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the alarm's two-tick shadow and the events it derives.</summary>
public sealed class AiAlarmControlStateTests
{
    /// <summary>
    /// <c>AIEVENT_ALARMON</c> is level-triggered — posted on every tick the alarm is on, not just the first.
    /// </summary>
    [Fact]
    public void AlarmOnIsPostedEveryTickItIsOn()
    {
        var alarm = new AiAlarmControlState();

        for (int tick = 1; tick <= 3; tick++)
        {
            alarm.Update(tick, disabled: false, enabled: true, triggerCondition: true, onCondition: true);
            Assert.True(alarm.IsOn);
            Assert.Equal(AiEvent.AlarmOn, alarm.PendingEvent);
            Assert.True(alarm.ForcesAlert);
        }
    }

    /// <summary>
    /// <c>AIEVENT_ALARMOFF</c> is a falling edge — exactly one tick, and it does <b>not</b> latch alert. An AI
    /// that misses that tick never hears the all-clear.
    /// </summary>
    [Fact]
    public void AlarmOffIsASingleFallingEdge()
    {
        var alarm = new AiAlarmControlState();
        alarm.Update(1, false, true, true, true);
        Assert.Equal(AiEvent.AlarmOn, alarm.PendingEvent);

        // Goes quiet: one tick of AlarmOff.
        alarm.Update(2, false, true, false, false);
        Assert.False(alarm.IsOn);
        Assert.True(alarm.WasOn);
        Assert.Equal(AiEvent.AlarmOff, alarm.PendingEvent);
        Assert.False(alarm.ForcesAlert);

        // And then nothing at all.
        alarm.Update(3, false, true, false, false);
        Assert.False(alarm.WasOn);
        Assert.Null(alarm.PendingEvent);

        alarm.Update(4, false, true, false, false);
        Assert.Null(alarm.PendingEvent);
    }

    /// <summary>Nothing is posted while the alarm has never sounded.</summary>
    [Fact]
    public void NothingIsPostedWhileQuiet()
    {
        var alarm = new AiAlarmControlState();
        alarm.Update(1, false, true, false, false);
        Assert.Null(alarm.PendingEvent);
    }

    /// <summary>Either gate forces both flags false without evaluating a condition.</summary>
    [Theory]
    [InlineData(true, true)]    // disabled
    [InlineData(false, false)]  // not enabled
    public void EitherGateForcesTheAlarmOff(bool disabled, bool enabled)
    {
        var alarm = new AiAlarmControlState();
        alarm.Update(1, false, true, true, true);
        Assert.True(alarm.IsOn);

        alarm.Update(2, disabled, enabled, triggerCondition: true, onCondition: true);
        Assert.False(alarm.IsOn);
        Assert.False(alarm.IsTriggered);

        // The shadow was still taken, so the gate produces a proper falling edge.
        Assert.True(alarm.WasOn);
        Assert.Equal(AiEvent.AlarmOff, alarm.PendingEvent);
    }

    /// <summary>
    /// An absent condition leaves its flag alone rather than clearing it — the original only assigns when the
    /// expression exists.
    /// </summary>
    [Fact]
    public void AnAbsentConditionKeepsThePreviousValue()
    {
        var alarm = new AiAlarmControlState();
        alarm.Update(1, false, true, true, true);
        Assert.True(alarm.IsOn);
        Assert.True(alarm.IsTriggered);

        alarm.Update(2, false, true, triggerCondition: null, onCondition: null);
        Assert.True(alarm.IsOn);
        Assert.True(alarm.IsTriggered);
    }

    /// <summary>The trigger stamps the tick, and only while it holds.</summary>
    [Fact]
    public void TheTriggerStampsItsTick()
    {
        var alarm = new AiAlarmControlState();

        alarm.Update(7, false, true, true, false);
        Assert.Equal(7, alarm.LastTriggeredTick);

        alarm.Update(8, false, true, false, false);
        Assert.Equal(7, alarm.LastTriggeredTick);

        alarm.Update(9, false, true, true, false);
        Assert.Equal(9, alarm.LastTriggeredTick);
    }

    /// <summary>The trigger is independent of the alarm sounding — they are two separate expressions.</summary>
    [Fact]
    public void TheTriggerAndTheAlarmAreIndependent()
    {
        var alarm = new AiAlarmControlState();

        alarm.Update(1, false, true, triggerCondition: true, onCondition: false);
        Assert.True(alarm.IsTriggered);
        Assert.False(alarm.IsOn);

        alarm.Update(2, false, true, triggerCondition: false, onCondition: true);
        Assert.False(alarm.IsTriggered);
        Assert.True(alarm.IsOn);
    }
}

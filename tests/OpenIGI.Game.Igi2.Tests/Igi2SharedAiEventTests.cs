using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Another game's AI event read in this game's terms (<see cref="Igi2AiEvent.TryFromShared"/>).</summary>
public sealed class Igi2SharedAiEventTests
{
    [Theory]
    [InlineData(AiEvent.Create, 0)]
    [InlineData(AiEvent.AlertResponse, 6)]
    [InlineData(AiEvent.AlarmOn, 7)]
    [InlineData(AiEvent.Walk, 9)]
    [InlineData(AiEvent.Gunshot, 15)]
    [InlineData(AiEvent.Explosion, 20)]
    [InlineData(AiEvent.FriendlyDetection, 22)]
    public void The_engines_events_are_numbered_one_lower_past_combat(AiEvent shared, int expected)
    {
        Assert.True(Igi2AiEvent.TryFromShared(Event((int)shared, 0, null), out Igi2AiEvent own));
        Assert.Equal(expected, own.Event);
    }

    [Fact]
    public void Combat_a_near_miss_and_a_private_event_have_no_counterpart()
    {
        Assert.False(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Combat, 0, null), out _));
        Assert.False(Igi2AiEvent.TryFromShared(Event((int)AiEvent.GunshotMiss, 0, null), out _));
        Assert.False(Igi2AiEvent.TryFromShared(Event(-1, 0, null), out _));
    }

    [Fact]
    public void The_flags_pass_whole_for_the_source_to_answer()
    {
        // A footstep (0x463920): in range, of a living human, from the other side. The walker, any game's human, is the
        // originator this game's admission tests ask whether it lives.
        HumanPlayerTask walker = new() { Team = 0 };
        Assert.True(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Walk, 0x46, walker), out Igi2AiEvent step));
        Assert.Equal(0x46, step.Flags);
        Assert.Same(walker, step.Originator);
        IAiHuman human = Assert.IsAssignableFrom<IAiHuman>(step.Originator);
        Assert.False(human.IsDead);
        Assert.Equal(0, step.Team);
    }

    private static HumanAiEvent Event(int type, int flags, HumanTask? human) =>
        new(type, human?.Team ?? -1, flags, 20480f, new Vector3d(1, 2, 3), human, human, new object());
}

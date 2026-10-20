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
    public void A_human_of_the_other_game_is_tested_here_and_its_bits_cleared()
    {
        // A footstep (0x463920): in range, of a living human, from the other side.
        HumanPlayerTask walker = new() { Team = 0 };
        Assert.True(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Walk, 0x46, walker), out Igi2AiEvent step));
        Assert.Equal(0x42, step.Flags);
        Assert.Same(walker, step.Originator);
        Assert.Equal(0, step.Team);

        Assert.False(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Walk, 0x46, null), out _));
        walker.IsDead = true;
        Assert.False(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Walk, 0x0E, walker), out _));
        Assert.True(Igi2AiEvent.TryFromShared(Event((int)AiEvent.Walk, 0x16, walker), out Igi2AiEvent dead));
        Assert.Equal(0x02, dead.Flags);
    }

    private static HumanAiEvent Event(int type, int flags, HumanTask? human) =>
        new(type, human?.Team ?? -1, flags, 20480f, new Vector3d(1, 2, 3), human, human, new object());
}

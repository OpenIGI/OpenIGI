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

    [Fact]
    public void Another_games_gunshot_is_the_shot_this_games_guns_raise_along_its_line_of_fire()
    {
        HumanPlayerTask shooter = new() { Team = 0 };
        Vector3f line = new(0f, 409600f, 0f);
        HumanAiEvent loud = Event((int)AiEvent.Gunshot, 0x46, shooter) with { Radius = 122880f, Direction = line };

        IReadOnlyList<Igi2AiEvent> heard = Igi2AiEvent.FromShared(loud);

        Assert.Equal([39, 15, 23], heard.Select(static e => e.Event));
        Assert.Equal([0x2C2, 0x442, 0x522], heard.Select(static e => e.Flags));
        Assert.Equal([614400f, 122880f, 122880f], heard.Select(static e => e.Radius));
        Assert.All(heard, e =>
        {
            Assert.Equal(line, e.Direction);
            Assert.Same(shooter, e.Originator);
            Assert.Equal(0, e.Team);
        });

        // A silenced gun's is not loud: no one sees it from afar.
        Assert.Equal([15, 23], Igi2AiEvent.FromShared(loud with { Radius = 4096f }).Select(static e => e.Event));
    }

    [Fact]
    public void A_round_striking_the_ground_is_this_games_bullet_strike_and_a_landing_its_ground_impact()
    {
        HumanPlayerTask shooter = new() { Team = 0 };
        HumanAiEvent round = Event((int)AiEvent.GroundImpact, 0x46, shooter) with { Direction = new Vector3f(0f, 4096f, -512f) };

        Igi2AiEvent strike = Assert.Single(Igi2AiEvent.FromShared(round));
        Assert.Equal(42, strike.Event);
        Assert.Equal(0x402, strike.Flags);
        Assert.Equal(40960f, strike.Radius);
        Assert.Equal(new Vector3f(0f, -4096f, 512f), strike.Direction);

        Igi2AiEvent landing = Assert.Single(Igi2AiEvent.FromShared(round with { Direction = default }));
        Assert.Equal(10, landing.Event);
    }

    private static HumanAiEvent Event(int type, int flags, HumanTask? human) =>
        new(type, human?.Team ?? -1, flags, 20480f, new Vector3d(1, 2, 3), human, human, new object());
}

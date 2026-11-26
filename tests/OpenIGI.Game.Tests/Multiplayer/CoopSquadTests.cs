using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The co-op squad's fallen: who can be revived, and by whom.</summary>
public sealed class CoopSquadTests
{
    private static readonly Vector3d Here = new(0, 0, 0);

    [Fact]
    public void A_player_alone_and_down_is_not_told_to_wait_for_a_teammate()
    {
        CoopSquad squad = new();
        squad.Tick(0, [new SquadMember(1, 0, Here, 0f, Down: true)]);

        Assert.False(squad.AnyoneStanding);
        Assert.Null(squad.ReviveProgressOf(1));
    }

    [Fact]
    public void A_fallen_player_waits_while_a_teammate_stands()
    {
        CoopSquad squad = new();
        squad.Tick(0, [new SquadMember(1, 0, Here, 0f, Down: true), new SquadMember(2, 1, new Vector3d(100 * 4096, 0, 0), 0f, Down: false)]);

        Assert.True(squad.AnyoneStanding);
        Assert.Equal(0f, squad.ReviveProgressOf(1));
    }

    [Fact]
    public void The_whole_squad_down_waits_for_nobody_until_it_stands_together_again()
    {
        CoopSquad squad = new();
        squad.Tick(0, [new SquadMember(1, 0, Here, 0f, Down: true), new SquadMember(2, 1, Here, 0f, Down: true)]);
        Assert.Null(squad.ReviveProgressOf(2));

        squad.StoodUpTogether();
        Assert.True(squad.AnyoneStanding);
    }
}

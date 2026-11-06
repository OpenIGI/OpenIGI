using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The players' votes: one at a time, carried by more than half, the one a kick names without a say.</summary>
public sealed class MatchVoteTests
{
    [Fact]
    public void A_vote_passes_once_more_than_half_say_yes_and_the_caller_counts()
    {
        MatchVote votes = new();
        Assert.Null(votes.Call(1, new VoteResult(VoteKind.Restart), "start over", now: 0));
        Assert.Null(votes.Settle([1, 2, 3, 4], 1, out _));

        Assert.True(votes.Say(2, yes: true));
        Assert.Null(votes.Settle([1, 2, 3, 4], 2, out _));
        Assert.True(votes.Say(3, yes: true));

        Assert.Equal(VoteKind.Restart, votes.Settle([1, 2, 3, 4], 3, out bool passed)?.Kind);
        Assert.True(passed);
        Assert.Null(votes.Running);
    }

    [Fact]
    public void A_vote_fails_as_soon_as_it_can_no_longer_be_carried_or_when_its_time_is_up()
    {
        MatchVote votes = new();
        votes.Call(1, new VoteResult(VoteKind.Scramble), "scramble", now: 0);
        votes.Say(2, yes: false);
        votes.Say(3, yes: false);
        Assert.NotNull(votes.Settle([1, 2, 3], 1, out bool lost));
        Assert.False(lost);

        votes.Call(2, new VoteResult(VoteKind.Restart), "restart", now: 2);
        Assert.Null(votes.Settle([1, 2, 3], 10, out _));
        Assert.NotNull(votes.Settle([1, 2, 3], 2 + MatchVote.Seconds, out bool late));
        Assert.False(late);
    }

    [Fact]
    public void The_player_a_kick_names_has_no_say_and_no_one_votes_themselves_off()
    {
        MatchVote votes = new();
        Assert.NotNull(votes.Call(1, new VoteResult(VoteKind.Kick, Slot: 1), "kick me", now: 0));

        Assert.Null(votes.Call(1, new VoteResult(VoteKind.Kick, Slot: 3), "kick three", now: 0));
        Assert.False(votes.Say(3, yes: false));
        Assert.True(votes.Say(2, yes: true));

        // Of the two with a say, both said yes.
        Assert.Equal(3, votes.Settle([1, 2, 3], 1, out bool passed)?.Slot);
        Assert.True(passed);
    }

    [Fact]
    public void One_vote_runs_at_a_time_and_a_caller_waits_before_calling_again()
    {
        MatchVote votes = new();
        votes.Call(1, new VoteResult(VoteKind.Restart), "restart", now: 0);
        Assert.NotNull(votes.Call(2, new VoteResult(VoteKind.Scramble), "scramble", now: 1));

        votes.Settle([1], 1, out _);
        Assert.NotNull(votes.Call(1, new VoteResult(VoteKind.Scramble), "scramble", now: 5));
        Assert.Null(votes.Call(1, new VoteResult(VoteKind.Scramble), "scramble", now: MatchVote.CallerCooldownSeconds + 1));
    }
}

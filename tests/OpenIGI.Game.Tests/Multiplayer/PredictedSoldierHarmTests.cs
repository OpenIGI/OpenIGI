using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A co-op client's foresight of its rounds on the level's soldiers, against the server's word that follows.</summary>
public sealed class PredictedSoldierHarmTests
{
    private const int Guard = 7;

    [Fact]
    public void A_round_short_of_what_is_left_is_a_hit_and_one_past_it_a_kill()
    {
        PredictedSoldierHarm harm = Advanced(0);
        Assert.Equal(ForeseenHarmVerdict.Follow, harm.TakeServer(Guard, 0.5f, alive: true));
        Assert.Equal(ForeseenHarm.Hit, harm.NoteHit(Guard, 0.3f));

        // The first round's damage is still waiting on the server: the second, with it, takes what was left.
        Assert.Equal(ForeseenHarm.Kill, harm.NoteHit(Guard, 0.25f));
        Assert.True(harm.IsFallen(Guard));
    }

    [Fact]
    public void A_soldier_foreseen_dead_stays_down_until_the_server_says_so()
    {
        PredictedSoldierHarm harm = Advanced(0);
        harm.TakeServer(Guard, 0.2f, alive: true);
        Assert.Equal(ForeseenHarm.Kill, harm.NoteHit(Guard, 0.5f));

        // The server's word a round trip behind still has him alive: he stays down.
        harm.Advance(0.2, 0.1, 0.066);
        Assert.Equal(ForeseenHarmVerdict.StayDown, harm.TakeServer(Guard, 0.2f, alive: true));

        // And then dead: nothing to undo.
        Assert.Equal(ForeseenHarmVerdict.Follow, harm.TakeServer(Guard, 0f, alive: false));
        Assert.False(harm.IsFallen(Guard));
        Assert.True(harm.ClaimKill());
        Assert.False(harm.ClaimKill());
    }

    [Fact]
    public void A_soldier_the_server_kept_alive_past_its_word_gets_up_and_its_kill_is_unclaimed()
    {
        PredictedSoldierHarm harm = Advanced(0);
        harm.TakeServer(Guard, 0.2f, alive: true);
        harm.NoteHit(Guard, 0.5f);

        harm.Advance(harm.ConfirmSeconds + 0.01, 0.1, 0.066);
        Assert.Equal(ForeseenHarmVerdict.GetUp, harm.TakeServer(Guard, 0.2f, alive: true));
        Assert.False(harm.IsFallen(Guard));
        Assert.False(harm.ClaimKill());
    }

    [Fact]
    public void What_the_server_took_off_is_no_longer_waited_on()
    {
        PredictedSoldierHarm harm = Advanced(0);
        harm.TakeServer(Guard, 1f, alive: true);
        harm.NoteHit(Guard, 0.4f);

        // The server counted the round: 0.6 left, nothing waiting. A round of 0.5 is then a hit, not a kill.
        harm.TakeServer(Guard, 0.6f, alive: true);
        Assert.Equal(ForeseenHarm.Hit, harm.NoteHit(Guard, 0.5f));
    }

    [Fact]
    public void A_round_the_server_never_counted_is_let_go_once_its_word_was_due()
    {
        PredictedSoldierHarm harm = Advanced(0);
        harm.TakeServer(Guard, 1f, alive: true);
        harm.NoteHit(Guard, 0.6f);
        harm.Advance(2, 0.1, 0.066);
        Assert.Equal(ForeseenHarm.Hit, harm.NoteHit(Guard, 0.6f));
    }

    [Fact]
    public void A_soldier_whose_death_is_not_to_be_foreseen_is_only_ever_hit()
    {
        PredictedSoldierHarm harm = Advanced(0);
        harm.TakeServer(Guard, null, alive: true);
        Assert.Equal(ForeseenHarm.Hit, harm.NoteHit(Guard, 100f));
    }

    [Fact]
    public void The_wait_is_the_round_trip_and_the_interpolation_within_its_bounds()
    {
        PredictedSoldierHarm harm = new();
        harm.Advance(0, 0.1, 0.066);
        Assert.Equal(0.1 + 0.066 + PredictedSoldierHarm.ConfirmSlackSeconds, harm.ConfirmSeconds, 6);
        harm.Advance(0, 5, 0.5);
        Assert.Equal(PredictedSoldierHarm.MostConfirmSeconds, harm.ConfirmSeconds);
        harm.Advance(0, 0, 0);
        Assert.Equal(PredictedSoldierHarm.LeastConfirmSeconds, harm.ConfirmSeconds);
    }

    [Fact]
    public void Vitality_carries_the_share_of_a_soldiers_own_health_whatever_scaled_it()
    {
        // A soldier of 0.8 made one and a half times as tough for a squad, half its scaled health gone: 0.6 left, three quarters
        // of its own.
        byte sent = SoldierVitality.Encode(0.6f, 0.8f, foreseeable: true);
        Assert.Equal(48, sent);
        Assert.Equal(0.6f, SoldierVitality.Decode(sent, 0.8f)!.Value, 3);
        Assert.Null(SoldierVitality.Decode(SoldierVitality.Encode(0.6f, 0.8f, foreseeable: false), 0.8f));
        Assert.Null(SoldierVitality.Decode(SoldierVitality.Encode(100f, 0.8f, foreseeable: true), 0.8f));
    }

    private static PredictedSoldierHarm Advanced(double now)
    {
        PredictedSoldierHarm harm = new();
        harm.Advance(now, 0.1, 0.066);
        return harm;
    }
}

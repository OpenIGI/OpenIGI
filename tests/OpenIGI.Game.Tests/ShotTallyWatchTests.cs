using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The one watch a session's worlds and the single player's kill feed read a player's tally through.</summary>
public sealed class ShotTallyWatchTests
{
    [Fact]
    public void WhatWasCountedBeforeTheWatchIsNeverTold()
    {
        ShotTally tally = new();
        tally.Killed(head: false);
        ShotTallyWatch watch = new();
        Assert.Null(watch.Take(tally));
    }

    [Fact]
    public void EachNewKillIsToldOnceWithItsHead()
    {
        ShotTally tally = new();
        ShotTallyWatch watch = new();
        Assert.Null(watch.Take(tally));

        tally.Hit(head: true);
        tally.Killed(head: true);
        tally.Hit(head: false);
        tally.Killed(head: false);
        ShotTallyNews news = Assert.IsType<ShotTallyNews>(watch.Take(tally));
        Assert.Equal(2, news.Kills);
        Assert.Equal(2, news.Hits);
        Assert.False(news.Head);
        Assert.Null(watch.Take(tally));

        tally.Hit(head: true);
        news = Assert.IsType<ShotTallyNews>(watch.Take(tally));
        Assert.False(news.Killed);
        Assert.True(news.Head);
    }

    [Fact]
    public void AnotherPlayersKillsAreNotTheSoldiers()
    {
        ShotTally tally = new();
        ShotTallyWatch watch = new();
        watch.Take(tally);
        tally.KilledPlayer(head: true);
        Assert.Null(watch.Take(tally));
    }
}

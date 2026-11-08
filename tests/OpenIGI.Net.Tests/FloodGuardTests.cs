using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>The rate a server answers strangers by: per machine, whatever port it sends from.</summary>
public sealed class FloodGuardTests
{
    private static readonly NetAddress Attacker = NetAddress.IPv4(203, 0, 113, 7, 40000);

    [Fact]
    public void A_sender_gets_its_burst_then_only_its_rate()
    {
        FloodGuard guard = new(perSecond: 8, burst: 16);

        int answered = Enumerable.Range(0, 1000).Count(_ => guard.TrySpend(Attacker, 0));
        Assert.Equal(16, answered);

        Assert.True(guard.TrySpend(Attacker, 1));
        Assert.Equal(1000 - 16, guard.Refused);
    }

    [Fact]
    public void Another_port_on_the_same_machine_is_the_same_sender()
    {
        FloodGuard guard = new(perSecond: 1, burst: 1);
        Assert.True(guard.TrySpend(Attacker, 0));

        Assert.False(guard.TrySpend(Attacker with { Port = 40001 }, 0));
        Assert.True(guard.TrySpend(NetAddress.IPv4(198, 51, 100, 1, 40000), 0));
    }

    [Fact]
    public void Room_peers_are_told_apart()
    {
        FloodGuard guard = new(perSecond: 1, burst: 1);
        Assert.True(guard.TrySpend(NetAddress.Peer(1), 0));
        Assert.True(guard.TrySpend(NetAddress.Peer(2), 0));
    }

    [Fact]
    public void A_refusal_charge_slows_the_next_tries()
    {
        FloodGuard guard = new(perSecond: 8, burst: 16);
        Assert.True(guard.TrySpend(Attacker, 0));

        guard.Charge(Attacker, 0, 16);

        Assert.False(guard.TrySpend(Attacker, 0));
    }

    [Fact]
    public void When_every_place_is_held_by_a_sender_just_heard_a_new_one_is_refused_and_quiet_ones_make_room()
    {
        FloodGuard guard = new(perSecond: 1, burst: 1, maxTracked: 2);
        Assert.True(guard.TrySpend(NetAddress.IPv4(10, 0, 0, 1, 1), 0));
        Assert.True(guard.TrySpend(NetAddress.IPv4(10, 0, 0, 2, 1), 0));

        Assert.False(guard.TrySpend(NetAddress.IPv4(10, 0, 0, 3, 1), 1));
        Assert.True(guard.TrySpend(NetAddress.IPv4(10, 0, 0, 3, 1), 30));
    }
}

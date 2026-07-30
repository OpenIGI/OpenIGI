using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the detection sweep's phase gate and its two nested rate limits.</summary>
public sealed class AiDetectionScheduleTests
{
    /// <summary>Each AI sweeps on one tick in four, so four AIs with different phases never collide.</summary>
    [Fact]
    public void EachAiSweepsOnOneTickInFour()
    {
        AiDetectionSchedule[] ais =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
        ];

        for (int tick = 0; tick < 12; tick++)
        {
            // Exactly one of the four sweeps on any given tick.
            Assert.Equal(1, ais.Count(a => a.SweepsOn(tick)));
        }

        // And each one sweeps every fourth tick.
        Assert.Equal([0, 4, 8], Enumerable.Range(0, 12).Where(ais[0].SweepsOn));
        Assert.Equal([2, 6, 10], Enumerable.Range(0, 12).Where(ais[2].SweepsOn));
    }

    /// <summary>A phase outside the four is refused.</summary>
    [Fact]
    public void APhaseOutsideTheFourIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDetectionSchedule(4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiDetectionSchedule(-1, 0, 0));
    }

    /// <summary>An interval of zero tests on every sweep — the cooldown is set to one and spent immediately.</summary>
    [Fact]
    public void AZeroIntervalTestsEverySweep()
    {
        var schedule = new AiDetectionSchedule(0, 0, 0);
        var slot = new AiDetectionSlot();
        slot.Register(0);
        int cones = 0;
        int rays = 0;

        for (int i = 0; i < 5; i++)
        {
            schedule.Sweep(slot, () => { cones++; return true; }, () => { rays++; return true; }, 100);
        }

        Assert.Equal(5, cones);
        Assert.Equal(5, rays);
        Assert.True(slot.IsVisible);
    }

    /// <summary>
    /// An interval of <c>n</c> tests once every <c>n + 1</c> sweeps — the cooldown is set to <c>n + 1</c>, spent
    /// down to <c>n</c> in the same call, and then has to reach zero. Off by one from the obvious reading, which
    /// is why it is pinned.
    /// </summary>
    [Theory]
    [InlineData(1, 12, 6)]
    [InlineData(2, 12, 4)]
    [InlineData(3, 12, 3)]
    [InlineData(5, 12, 2)]
    public void AnIntervalTestsOncePerThatManySweepsPlusOne(int interval, int sweeps, int expected)
    {
        var schedule = new AiDetectionSchedule(0, interval, 0);
        var slot = new AiDetectionSlot();
        slot.Register(0);
        int cones = 0;

        for (int i = 0; i < sweeps; i++)
        {
            schedule.Sweep(slot, () => { cones++; return true; }, () => true, 100);
        }

        Assert.Equal(expected, cones);
    }

    /// <summary>
    /// <b>The ray is never paid for someone outside the cone.</b> That is the whole point of the cascade — the
    /// cheap test gates the expensive one.
    /// </summary>
    [Fact]
    public void TheRayOnlyRunsWhileInTheCone()
    {
        var schedule = new AiDetectionSchedule(0, 0, 0);
        var slot = new AiDetectionSlot();
        slot.Register(0);
        int rays = 0;

        for (int i = 0; i < 10; i++)
        {
            schedule.Sweep(slot, () => false, () => { rays++; return true; }, 100);
        }

        Assert.Equal(0, rays);
        Assert.False(slot.IsInCone);
        Assert.False(slot.IsVisible);
    }

    /// <summary>
    /// <b>Losing the cone clears visibility at once; regaining it waits for the ray.</b> An AI loses sight of you
    /// instantly and re-acquires you no sooner than the ray interval allows.
    /// </summary>
    [Fact]
    public void LosingTheConeIsInstantButRegainingItWaitsForTheRay()
    {
        // Cone tested every sweep, ray only every third.
        var schedule = new AiDetectionSchedule(0, 0, 3);
        var slot = new AiDetectionSlot();
        slot.Register(0);
        bool inCone = true;
        int rays = 0;

        // Acquire.
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.True(slot.IsVisible);
        Assert.Equal(1, rays);

        // Step out of the cone: visibility goes immediately, with no ray.
        inCone = false;
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.False(slot.IsVisible);
        Assert.Equal(1, rays);

        // Step back in: in-cone returns at once but visible does not, because the ray is still cooling down.
        inCone = true;
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.True(slot.IsInCone);
        Assert.False(slot.IsVisible);
        Assert.Equal(1, rays);

        // Three more sweeps and the ray comes due. Note the tick spent outside the cone did NOT count
        // towards it: the ray cooldown is only decremented while in a cone.
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.False(slot.IsVisible);
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.False(slot.IsVisible);
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.True(slot.IsVisible);
        Assert.Equal(2, rays);
    }

    /// <summary>
    /// Time spent outside the cone does not count towards the next ray, because the ray's cooldown is only
    /// decremented while in one. Stepping out of view and back does not shorten the wait.
    /// </summary>
    [Fact]
    public void TimeOutOfTheConeDoesNotCountTowardsTheNextRay()
    {
        var schedule = new AiDetectionSchedule(0, 0, 2);
        var slot = new AiDetectionSlot();
        slot.Register(0);
        bool inCone = true;
        int rays = 0;

        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.Equal(1, rays);

        // Twenty sweeps out of the cone advance nothing.
        inCone = false;
        for (int i = 0; i < 20; i++)
        {
            schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        }

        Assert.Equal(1, rays);

        // Back in, and it still takes the full period.
        inCone = true;
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.Equal(1, rays);
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.Equal(1, rays);
        schedule.Sweep(slot, () => inCone, () => { rays++; return true; }, 100);
        Assert.Equal(2, rays);
    }

    /// <summary>A blocked line leaves the target in the cone but not visible.</summary>
    [Fact]
    public void ABlockedLineLeavesTheTargetInConeButUnseen()
    {
        var schedule = new AiDetectionSchedule(0, 0, 0);
        var slot = new AiDetectionSlot();
        slot.Register(0);

        schedule.Sweep(slot, () => true, () => false, 100);

        Assert.True(slot.IsInCone);
        Assert.False(slot.IsVisible);
    }

    /// <summary>The distance is refreshed on every sweep, whatever the cooldowns say.</summary>
    [Fact]
    public void TheDistanceIsRefreshedEverySweep()
    {
        var schedule = new AiDetectionSchedule(0, 10, 10);
        var slot = new AiDetectionSlot();
        slot.Register(0);

        schedule.Sweep(slot, () => true, () => true, 1234.0);
        Assert.Equal(1234.0, slot.Distance);

        // Both cooldowns are now well short of due, but the distance still updates.
        schedule.Sweep(slot, () => throw new InvalidOperationException("cone retested too soon"), () => true, 4321.0);
        Assert.Equal(4321.0, slot.Distance);
    }

    /// <summary>Registering resets the flags and cooldowns; unregistering touches only the registered bit.</summary>
    [Fact]
    public void RegisteringResetsAndUnregisteringDoesNot()
    {
        var slot = new AiDetectionSlot { IsInCone = true, IsVisible = true, ConeCooldown = 7, RayCooldown = 9 };

        slot.Register(tick: 42);
        Assert.True(slot.IsRegistered);
        Assert.False(slot.IsInCone);
        Assert.False(slot.IsVisible);
        Assert.Equal(0, slot.ConeCooldown);
        Assert.Equal(0, slot.RayCooldown);
        Assert.Equal(42, slot.FirstSeenTick);

        slot.IsInCone = true;
        slot.IsVisible = true;
        slot.Unregister();

        // 0x4894E0 clears only +42; the stale flags are left behind and nothing reads them.
        Assert.False(slot.IsRegistered);
        Assert.True(slot.IsInCone);
        Assert.True(slot.IsVisible);
    }
}

using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>How far behind the server a client draws the others: its <c>cl_interp</c>, and what its line asks on top.</summary>
public sealed class TickClockSyncTests
{
    private const int TickRate = 30;

    [Fact]
    public void A_steady_line_sending_every_tick_draws_at_cl_interp()
    {
        TickClockSync clock = Feed(spacing: 1, jitterSeconds: 0, seconds: 10);
        Assert.Equal(0.1, clock.AppliedInterpolationDelay, 3);
    }

    [Fact]
    public void Sparse_snapshots_are_drawn_two_spacings_behind_so_one_lost_is_bridged()
    {
        // A snapshot every 4 ticks (7.5 a second): two spacings are 0.27 s.
        TickClockSync clock = Feed(spacing: 4, jitterSeconds: 0, seconds: 20);
        Assert.InRange(clock.AppliedInterpolationDelay, 0.25, 0.28);
    }

    [Fact]
    public void A_jittery_line_draws_further_back_but_never_past_the_bound()
    {
        TickClockSync calm = Feed(spacing: 1, jitterSeconds: 0, seconds: 20);
        TickClockSync jittery = Feed(spacing: 1, jitterSeconds: 0.06, seconds: 20);
        TickClockSync wild = Feed(spacing: 1, jitterSeconds: 2, seconds: 20);
        Assert.True(jittery.AppliedInterpolationDelay > calm.AppliedInterpolationDelay + 0.02, $"{jittery.AppliedInterpolationDelay}");
        Assert.True(wild.AppliedInterpolationDelay <= wild.MaxInterpolationDelay + 1e-9);
    }

    [Fact]
    public void The_delay_eases_instead_of_jumping()
    {
        TickClockSync clock = Feed(spacing: 1, jitterSeconds: 0, seconds: 5);
        double before = clock.AppliedInterpolationDelay;
        clock.InterpolationDelay = 0.4;
        clock.AddSample(10_000, 10_000.0 / TickRate, 0);
        double after = clock.AppliedInterpolationDelay;
        Assert.True(after > before && after < 0.4, $"{before} -> {after}");
    }

    // Snapshots every `spacing` ticks for a while, arriving off their time by up to `jitterSeconds` either way.
    private static TickClockSync Feed(int spacing, double jitterSeconds, double seconds)
    {
        TickClockSync clock = new(TickRate);
        Random random = new(7);
        for (long tick = 0; tick < seconds * TickRate; tick += spacing)
        {
            double arrival = (tick / (double)TickRate) + 0.05 + (((random.NextDouble() * 2) - 1) * jitterSeconds);
            clock.AddSample(tick, arrival, 0.1);
        }

        return clock;
    }
}

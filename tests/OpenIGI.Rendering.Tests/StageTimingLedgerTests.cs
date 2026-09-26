using OpenIGI.Rendering.Diagnostics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the stage timings say what they claim to say.
/// </summary>
/// <remarks>
/// This arithmetic used to live inside a Vulkan class, where checking it needed a GPU and so nothing
/// did. It is where the warm-up is discarded, where a rolling window keeps a running sum that has to
/// stay in step with what is in it, and where a peak has to survive the window scrolling past it —
/// three ways to report a number that is merely plausible.
/// </remarks>
public sealed class StageTimingLedgerTests
{
    [Fact]
    public void A_stage_that_never_ran_reports_nothing_rather_than_zero_samples_of_something()
    {
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.CompleteFrame();

        RenderStageTiming lighting = Find(ledger, RenderStage.Lighting);

        Assert.Equal(0, lighting.SampleCount);
        Assert.False(lighting.Active);
        Assert.Equal(0.0, lighting.Milliseconds);
    }

    [Fact]
    public void The_warm_up_frames_are_discarded()
    {
        // The first frames of a run are pipeline compiles and cold caches; reported as the steady state
        // they make every budget look blown.
        StageTimingLedger ledger = new(window: 8, warmUpFrames: 2);

        for (int i = 0; i < 2; i++)
        {
            ledger.Observe(RenderStage.Shadows, 100.0);
            ledger.CompleteFrame();
        }

        Assert.Equal(0, Find(ledger, RenderStage.Shadows).SampleCount);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.CompleteFrame();

        RenderStageTiming shadows = Find(ledger, RenderStage.Shadows);

        Assert.Equal(1, shadows.SampleCount);
        Assert.Equal(1.0, shadows.Milliseconds);
        Assert.Equal(1.0, shadows.PeakMilliseconds);
    }

    [Fact]
    public void A_stage_that_runs_twice_in_a_frame_is_summed_rather_than_replaced()
    {
        // One shadow query per cascade is still one stage: the budget is for the stage, not for one
        // visit to it.
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 0.5);
        ledger.Observe(RenderStage.Shadows, 0.25);
        ledger.CompleteFrame();

        Assert.Equal(0.75, Find(ledger, RenderStage.Shadows).Milliseconds, 6);
    }

    [Fact]
    public void The_average_follows_the_window_rather_than_every_frame_ever_seen()
    {
        StageTimingLedger ledger = new(window: 2, warmUpFrames: 0);

        foreach (double milliseconds in (ReadOnlySpan<double>)[10.0, 2.0, 2.0])
        {
            ledger.Observe(RenderStage.Lighting, milliseconds);
            ledger.CompleteFrame();
        }

        RenderStageTiming lighting = Find(ledger, RenderStage.Lighting);

        // The ten has scrolled out of a window of two, so the running sum must have lost it too.
        Assert.Equal(2, lighting.SampleCount);
        Assert.Equal(2.0, lighting.Milliseconds, 6);
    }

    [Fact]
    public void The_peak_survives_the_window_scrolling_past_it()
    {
        // The budget is what a stage costs; the peak is what it costs when the camera turns to face the
        // whole level, which is the frame that actually drops.
        StageTimingLedger ledger = new(window: 2, warmUpFrames: 0);

        foreach (double milliseconds in (ReadOnlySpan<double>)[9.0, 1.0, 1.0, 1.0])
        {
            ledger.Observe(RenderStage.Lighting, milliseconds);
            ledger.CompleteFrame();
        }

        Assert.Equal(9.0, Find(ledger, RenderStage.Lighting).PeakMilliseconds);
    }

    [Fact]
    public void Active_says_what_ran_in_the_last_frame_and_not_what_ever_ran()
    {
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.CompleteFrame();

        Assert.True(Find(ledger, RenderStage.Shadows).Active);

        ledger.Observe(RenderStage.Lighting, 1.0);
        ledger.CompleteFrame();

        Assert.False(Find(ledger, RenderStage.Shadows).Active);
        Assert.True(Find(ledger, RenderStage.Lighting).Active);

        // Still measured, though, which is the difference between "not running now" and "never ran".
        Assert.Equal(1, Find(ledger, RenderStage.Shadows).SampleCount);
    }

    [Fact]
    public void The_total_is_the_distribution_of_the_per_frame_sum()
    {
        // Not a sum of per-stage percentiles, which would be a frame that never happened.
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.Observe(RenderStage.Lighting, 2.0);
        ledger.CompleteFrame();

        GpuFrameTiming total = ledger.TotalTiming();

        Assert.Equal(3.0, total.Milliseconds, 6);
        Assert.Equal(1, total.SampleCount);
    }

    [Fact]
    public void The_measured_frame_is_what_the_gpu_took_and_not_what_the_stages_add_up_to()
    {
        // The gap between the two is work no stage claims, which is the one thing a sum cannot see.
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.CompleteFrame(measuredMilliseconds: 4.0);

        Assert.Equal(1.0, ledger.TotalTiming().Milliseconds, 6);
        Assert.Equal(4.0, ledger.MeasuredTiming().Milliseconds, 6);
    }

    [Fact]
    public void A_frame_that_was_not_measured_end_to_end_contributes_no_measured_sample()
    {
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Shadows, 1.0);
        ledger.CompleteFrame();

        Assert.Equal(0, ledger.MeasuredTiming().SampleCount);
    }

    [Fact]
    public void A_frame_in_which_nothing_ran_is_counted_but_recorded_nowhere()
    {
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.CompleteFrame(measuredMilliseconds: 4.0);

        Assert.Equal(1, ledger.CompletedFrames);
        Assert.Equal(0, ledger.TotalTiming().SampleCount);
        Assert.Equal(0, ledger.MeasuredTiming().SampleCount);
    }

    [Fact]
    public void A_reset_forgets_the_peaks_as_well_as_the_window()
    {
        StageTimingLedger ledger = new(window: 4, warmUpFrames: 0);

        ledger.Observe(RenderStage.Lighting, 9.0);
        ledger.CompleteFrame(measuredMilliseconds: 9.0);

        ledger.Reset();

        RenderStageTiming lighting = Find(ledger, RenderStage.Lighting);

        Assert.Equal(0.0, lighting.PeakMilliseconds);
        Assert.Equal(0, lighting.SampleCount);
        Assert.Equal(0, ledger.CompletedFrames);
        Assert.Equal(0, ledger.MeasuredTiming().SampleCount);
    }

    [Fact]
    public void Every_stage_appears_in_the_report_once()
    {
        StageTimingLedger ledger = new();

        IReadOnlyList<RenderStageTiming> timings = ledger.Timings();

        Assert.Equal(Enum.GetValues<RenderStage>().Length, timings.Count);
        Assert.Equal(timings.Count, timings.Select(t => t.Stage).Distinct().Count());
    }

    private static RenderStageTiming Find(StageTimingLedger ledger, RenderStage stage) =>
        ledger.Timings().Single(timing => timing.Stage == stage);
}

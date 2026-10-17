using System.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The handover of a probe bake from its worker to the render thread.
/// </summary>
/// <remarks>
/// The bake moved off the main thread because running it there took the GPU context away — three
/// seconds of a live window answering nothing is long enough for Windows' watchdog. What that buys has
/// to survive the handover, so what is pinned here is the handle's contract rather than the trace's
/// arithmetic, which <see cref="IrradianceProbeBakerTests"/> already covers.
/// </remarks>
public sealed class IrradianceProbeBakeTests
{
    /// <summary>Coarse on purpose: this is a test about the handover, not about probe density.</summary>
    private const float TestSpacingMetres = 32f;

    private static readonly Vector3f GroundAlbedo = new(0.42f, 0.40f, 0.37f);

    [RequiresGameDataFact]
    public void TheVolumeArrivesOnTheThreadThatCollectsIt()
    {
        IrradianceProbeBake bake = StartOnLevelOne();

        Assert.True(SpinUntilComplete(bake), "the bake did not finish inside its budget");
        Assert.True(bake.TryCollect(out IrradianceVolume? volume), "a finished bake refused to hand its volume over");
        Assert.NotNull(volume);
        Assert.True(volume.ProbeCount > 0, "the collected volume held no probes");
    }

    [RequiresGameDataFact]
    public void ItIsCollectedExactlyOnce()
    {
        // The render thread polls this every frame and uploads whatever it gets. Handing the volume
        // over twice would re-upload megabytes of transfer for the rest of the level.
        IrradianceProbeBake bake = StartOnLevelOne();
        Assert.True(SpinUntilComplete(bake));

        Assert.True(bake.TryCollect(out IrradianceVolume? first));
        Assert.NotNull(first);

        Assert.False(bake.TryCollect(out IrradianceVolume? second), "the volume was handed over a second time");
        Assert.Null(second);
    }

    [RequiresGameDataFact]
    public void NothingIsCollectedWhileItIsStillTracing()
    {
        // The frame that polls before the worker is done must get its answer immediately and carry on
        // drawing. A handle that blocked here would reproduce the fault it was written to remove.
        IrradianceProbeBake bake = StartOnLevelOne();

        long startedAt = Stopwatch.GetTimestamp();
        bool collected = bake.TryCollect(out IrradianceVolume? volume);
        double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        Assert.True(elapsedMilliseconds < 100.0, $"polling an unfinished bake blocked for {elapsedMilliseconds:0} ms");

        // It may legitimately have finished already on a fast machine; what must never happen is a
        // claim to have collected something and nothing in hand.
        if (collected)
        {
            Assert.NotNull(volume);
        }
        else
        {
            Assert.Null(volume);
        }

        SpinUntilComplete(bake);
    }

    [RequiresGameDataFact]
    public void WaitingForItGivesTheSameFieldAsPollingForIt()
    {
        // The lockstep capture path takes this one, and a reference image has to contain the same field
        // an interactive load would eventually show.
        IrradianceVolume? waited = StartOnLevelOne().WaitForResult();
        Assert.NotNull(waited);

        IrradianceProbeBake polled = StartOnLevelOne();
        Assert.True(SpinUntilComplete(polled));
        Assert.True(polled.TryCollect(out IrradianceVolume? collected));
        Assert.NotNull(collected);

        Assert.Equal(waited.ProbeCount, collected.ProbeCount);
        Assert.Equal(waited.Coefficients.ToArray(), collected.Coefficients.ToArray());
    }

    [RequiresGameDataFact]
    public void SteppedInlineItGivesTheFieldTheWorkersGive()
    {
        // A single-threaded host traces the probes a row at a time, a slice of a frame per poll. The rows
        // are independent, so the field must be the same to the last coefficient as the pool's.
        IrradianceVolume? pooled = StartOnLevelOne().WaitForResult();
        Assert.NotNull(pooled);

        IrradianceProbeBake stepped = StartOnLevelOne(InlineWorkScheduler.Instance);

        int polls = 0;
        while (!stepped.Poll())
        {
            polls++;
        }

        // Many slices, not one: the trace really was spread over the polls.
        Assert.True(polls > 10, $"{polls} poll(s)");
        Assert.True(stepped.TryCollect(out IrradianceVolume? collected));
        Assert.NotNull(collected);

        Assert.Equal(pooled.ProbeCount, collected.ProbeCount);
        Assert.Equal(pooled.Coefficients.ToArray(), collected.Coefficients.ToArray());
    }

    [RequiresGameDataFact]
    public void CancellingItFinishesRatherThanHanging()
    {
        // Quitting during a bake. The worker is a thread-pool thread, so it cannot hold the process
        // open -- what this pins is that cancelling neither deadlocks the collector nor escapes as an
        // exception into the frame that polled.
        IrradianceProbeBake bake = StartOnLevelOne();
        bake.Cancel();

        Assert.True(SpinUntilComplete(bake), "a cancelled bake never completed");
        Assert.True(bake.TryCollect(out IrradianceVolume? volume));

        // Either it was stopped, or it beat the cancellation. Both are correct; a throw is not.
        if (volume is not null)
        {
            Assert.True(volume.ProbeCount > 0);
        }
    }

    private static IrradianceProbeBake StartOnLevelOne(IWorkScheduler? work = null)
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        return IrradianceProbeBake.Start(
            scene,
            solids.CasterWithDoorsShut(),
            new Vector3f(0.3f, 0.2f, 0.93f),
            TestSpacingMetres,
            GroundAlbedo,
            work ?? ThreadPoolWorkScheduler.Instance);
    }

    private static bool SpinUntilComplete(IrradianceProbeBake bake)
    {
        // Generous: a Debug trace of a real level is slow, and the machine may be running the rest of
        // the suite alongside it.
        long startedAt = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromMinutes(2))
        {
            if (bake.IsComplete)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }
}

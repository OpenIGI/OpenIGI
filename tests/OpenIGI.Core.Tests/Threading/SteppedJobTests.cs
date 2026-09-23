using OpenIGI.Core.Threading;
using Xunit;

namespace OpenIGI.Core.Tests.Threading;

/// <summary>
/// Stepped work, run a slice at a time where nothing runs beside the caller and to the end on a worker
/// where something does -- and the same answer either way.
/// </summary>
public sealed class SteppedJobTests
{
    [Fact]
    public void A_step_runs_at_least_one_slice_and_the_job_finishes_with_its_answer()
    {
        int slicesRun = 0;
        SteppedJob<int> job = new(done => Count(3, () => slicesRun++, done));

        Assert.False(job.Step(TimeSpan.Zero));
        Assert.Equal(1, slicesRun);
        Assert.Equal(0, job.Result);

        Assert.True(job.Step(TimeSpan.FromMinutes(1)));
        Assert.Equal(3, slicesRun);
        Assert.Equal(3, job.Result);
    }

    [Fact]
    public void Cancellation_finishes_the_job_with_no_answer()
    {
        SteppedJob<string?> job = new(done => Cancelled(done));

        Assert.Null(job.RunToEnd());
        Assert.True(job.IsFinished);
    }

    [Fact]
    public void Inline_a_poll_is_a_slice_of_the_work()
    {
        int slicesRun = 0;
        ScheduledJob<int> scheduled = new(new SteppedJob<int>(done => Count(4, () => slicesRun++, done)), InlineWorkScheduler.Instance);

        Assert.False(scheduled.IsConcurrent);
        Assert.Equal(0, slicesRun);

        int polls = 0;
        while (!scheduled.Poll())
        {
            polls++;
            Assert.True(polls < 100, "The job never finished.");
        }

        Assert.Equal(4, scheduled.Result);
        Assert.Equal(4, slicesRun);
    }

    [Fact]
    public void On_the_pool_the_job_runs_by_itself_and_waiting_returns_its_answer()
    {
        ScheduledJob<int> scheduled = new(new SteppedJob<int>(done => Count(5, () => { }, done)), ThreadPoolWorkScheduler.Instance);

        Assert.True(scheduled.IsConcurrent);
        Assert.Equal(5, scheduled.Wait());
        Assert.True(scheduled.Poll());
        Assert.Equal(5, scheduled.Result);
    }

    [Theory]
    [MemberData(nameof(WorkSchedulerTests.Schedulers), MemberType = typeof(WorkSchedulerTests))]
    public void A_failure_finishes_the_job_with_no_answer_and_waiting_rethrows_it(IWorkScheduler work)
    {
        ScheduledJob<int> scheduled = new(new SteppedJob<int>(done => Failing(done)), work);

        Assert.Throws<InvalidDataException>(() => scheduled.Wait());

        Assert.True(scheduled.Poll());
        Assert.Equal(0, scheduled.Result);
        Assert.IsType<InvalidDataException>(scheduled.Failure);
    }

    [Fact]
    public void A_range_is_one_slice_on_the_pool_and_one_slice_an_index_inline()
    {
        int[] seen = new int[8];

        Assert.Single(Stepping.Range(ThreadPoolWorkScheduler.Instance, seen.Length, 0, i => Interlocked.Increment(ref seen[i])));
        Assert.Equal(8, Stepping.Range(InlineWorkScheduler.Instance, seen.Length, 0, i => Interlocked.Increment(ref seen[i])).Count());
        Assert.All(seen, count => Assert.Equal(2, count));
    }

    private static IEnumerable<bool> Count(int slices, Action slice, Action<int> done)
    {
        for (int i = 0; i < slices; i++)
        {
            slice();
            yield return true;
        }

        done(slices);
    }

    private static IEnumerable<bool> Cancelled(Action<string?> done)
    {
        yield return true;
        throw new OperationCanceledException();
    }

    private static IEnumerable<bool> Failing(Action<int> done)
    {
        yield return true;
        throw new InvalidDataException("the bake met something");
    }
}

using OpenIGI.Core.Threading;
using Xunit;

namespace OpenIGI.Core.Tests.Threading;

/// <summary>
/// The two schedulers answer the same calls the same way, apart from where the work runs: the inline one
/// on the caller's thread before the call returns, the pool's beside it.
/// </summary>
public sealed class WorkSchedulerTests
{
    public static TheoryData<IWorkScheduler> Schedulers => [InlineWorkScheduler.Instance, ThreadPoolWorkScheduler.Instance];

    [Fact]
    public void Inline_work_has_run_on_the_callers_thread_by_the_time_the_call_returns()
    {
        int caller = Environment.CurrentManagedThreadId;
        int ranOn = -1;

        Task task = InlineWorkScheduler.Instance.Run(() => ranOn = Environment.CurrentManagedThreadId);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(caller, ranOn);
        Assert.False(InlineWorkScheduler.Instance.IsConcurrent);
        Assert.True(ThreadPoolWorkScheduler.Instance.IsConcurrent);
    }

    [Theory]
    [MemberData(nameof(Schedulers))]
    public async Task A_value_comes_back_through_the_task(IWorkScheduler scheduler)
    {
        Assert.Equal(42, await scheduler.Run(() => 42));
    }

    [Theory]
    [MemberData(nameof(Schedulers))]
    public async Task A_failure_is_carried_by_the_task_rather_than_thrown_at_the_call(IWorkScheduler scheduler)
    {
        Task<int> task = scheduler.Run<int>(() => throw new InvalidDataException("bad"));

        InvalidDataException thrown = await Assert.ThrowsAsync<InvalidDataException>(() => task);
        Assert.Equal("bad", thrown.Message);
    }

    [Theory]
    [MemberData(nameof(Schedulers))]
    public async Task Work_cancelled_before_it_starts_does_not_run(IWorkScheduler scheduler)
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        bool ran = false;

        Task task = scheduler.Run(() => ran = true, cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(ran);
    }

    [Theory]
    [MemberData(nameof(Schedulers))]
    public void A_range_runs_every_index_once(IWorkScheduler scheduler)
    {
        int[] seen = new int[100];

        scheduler.ForRange(0, seen.Length, 4, i => Interlocked.Increment(ref seen[i]));

        Assert.All(seen, count => Assert.Equal(1, count));
    }

    [Theory]
    [MemberData(nameof(Schedulers))]
    public void Every_item_runs_once_and_a_failing_body_surfaces_as_the_pools_loop_surfaces_it(IWorkScheduler scheduler)
    {
        int total = 0;
        scheduler.ForEach(Enumerable.Range(1, 10), 0, item => Interlocked.Add(ref total, item));
        Assert.Equal(55, total);

        AggregateException failed = Assert.Throws<AggregateException>(() =>
            scheduler.ForEach([1, 2, 3], 0, item =>
            {
                if (item == 2)
                {
                    throw new InvalidDataException("two");
                }
            }));

        Assert.Contains(failed.InnerExceptions, error => error is InvalidDataException);
    }
}

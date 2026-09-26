using System.Runtime.ExceptionServices;

namespace OpenIGI.Core.Threading;

/// <summary>
/// A stepped job, run to the end on a worker where the scheduler has one, and a slice per poll where it
/// does not.
/// </summary>
/// <typeparam name="T">What the job produces.</typeparam>
/// <remarks>
/// <para>
/// The shape every lighting bake has: started once, polled by the frame loop until it is done, and waited
/// for outright by a lockstep capture. On the desktop the poll only asks whether the worker has finished.
/// On a single-threaded host the poll IS the work: each advances the job by <see cref="SliceBudget"/>, so
/// the bake proceeds behind the loading screen at a cost the frame can carry, and the caller does not
/// change.
/// </para>
/// <para>
/// A job that fails is finished with no answer, as a faulted worker has always been to a poll; waiting
/// for it rethrows what it met.
/// </para>
/// </remarks>
public sealed class ScheduledJob<T>
{
    /// <summary>How long one poll advances a job run inline.</summary>
    public static readonly TimeSpan SliceBudget = TimeSpan.FromMilliseconds(8);

    private readonly SteppedJob<T> _job;
    private readonly Task<T?>? _worker;
    private ExceptionDispatchInfo? _failure;

    /// <summary>Initialises a new instance of the <see cref="ScheduledJob{T}"/> class, and starts it where it runs concurrently.</summary>
    /// <param name="job">The work.</param>
    /// <param name="work">Where it runs.</param>
    public ScheduledJob(SteppedJob<T> job, IWorkScheduler work)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(work);

        _job = job;
        _worker = work.IsConcurrent ? work.Run(job.RunToEnd) : null;
    }

    /// <summary>Gets whether the job runs on a worker rather than a slice per poll.</summary>
    public bool IsConcurrent => _worker is not null;

    /// <summary>Gets whether the job has finished, however it finished, without advancing it.</summary>
    public bool IsFinished => _worker?.IsCompleted ?? _job.IsFinished;

    /// <summary>Gets what the job met, where it failed; null for one that has not, or has not yet.</summary>
    public Exception? Failure => _worker is { IsFaulted: true } worker
        ? worker.Exception?.GetBaseException()
        : _failure?.SourceException;

    /// <summary>Asks whether the job has finished, advancing it one slice first where it runs inline.</summary>
    /// <returns><see langword="true"/> once it has finished, however it finished.</returns>
    public bool Poll()
    {
        if (_worker is { } worker)
        {
            return worker.IsCompleted;
        }

        if (_job.IsFinished)
        {
            return true;
        }

        try
        {
            return _job.Step(SliceBudget);
        }
        catch (Exception error)
        {
            _failure = ExceptionDispatchInfo.Capture(error);
            return true;
        }
    }

    /// <summary>Gets the answer of a job that finished well; the default for one not finished, cancelled or failed.</summary>
    public T? Result => _worker is { } worker
        ? (worker.IsCompletedSuccessfully ? worker.Result : default)
        : (_failure is null ? _job.Result : default);

    /// <summary>Waits for the job to finish -- running what is left of it here, where it runs inline -- and returns its answer.</summary>
    /// <returns>The answer, or the default for a job that was cancelled.</returns>
    /// <remarks>Rethrows what the job met, where it failed.</remarks>
    public T? Wait()
    {
        if (_worker is { } worker)
        {
            return worker.GetAwaiter().GetResult();
        }

        _failure?.Throw();

        try
        {
            return _job.RunToEnd();
        }
        catch (Exception error)
        {
            // Remembered as a poll would remember it, so what follows sees a failed job, not a finished one.
            _failure = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }
}

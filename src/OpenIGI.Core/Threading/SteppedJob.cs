using System.Diagnostics;

namespace OpenIGI.Core.Threading;

/// <summary>A long piece of work that can be run to the end, or a slice at a time.</summary>
/// <typeparam name="T">What it produces.</typeparam>
/// <remarks>
/// <para>
/// The level's lighting bakes take seconds. On the desktop they run to the end on a worker, beside the
/// game. A single-threaded host has no worker, so it advances them a few milliseconds a frame behind the
/// loading screen instead -- and a bake written once has to serve both.
/// </para>
/// <para>
/// So the work is an iterator whose every element is the end of a slice: <see cref="Step"/> runs slices
/// until its budget has gone, <see cref="RunToEnd"/> runs all of them. Where the work divides into
/// independent pieces, <see cref="Stepping.Range"/> runs them all at once in parallel on a scheduler that
/// can, and one per slice on one that cannot, so the desktop loses none of the parallelism it had.
/// </para>
/// <para>
/// The iterator hands its answer to the callback it is given. Cancellation -- an
/// <see cref="OperationCanceledException"/> from inside -- finishes the job with the default answer, as
/// the bakes have always treated it; any other exception propagates from the call that met it.
/// </para>
/// </remarks>
public sealed class SteppedJob<T>
{
    private readonly IEnumerator<bool> _slices;
    private readonly Lock _gate = new();
    private T? _result;

    /// <summary>Initialises a new instance of the <see cref="SteppedJob{T}"/> class.</summary>
    /// <param name="body">
    /// Builds the work's iterator, given the callback that records its answer. Nothing runs until the
    /// first step.
    /// </param>
    public SteppedJob(Func<Action<T>, IEnumerable<bool>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _slices = body(result => _result = result).GetEnumerator();
    }

    /// <summary>Gets whether the work has finished, one way or another.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Gets the answer, once finished; the default before then, or where it was cancelled.</summary>
    public T? Result => IsFinished ? _result : default;

    /// <summary>Runs slices until the budget has gone, or the work has finished.</summary>
    /// <param name="budget">How long to keep going. At least one slice runs, however small this is.</param>
    /// <returns><see langword="true"/> once finished.</returns>
    public bool Step(TimeSpan budget)
    {
        long started = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            while (!IsFinished)
            {
                Advance();

                if (Stopwatch.GetElapsedTime(started) >= budget)
                {
                    break;
                }
            }

            return IsFinished;
        }
    }

    /// <summary>Runs every remaining slice.</summary>
    /// <returns>The answer.</returns>
    public T? RunToEnd()
    {
        lock (_gate)
        {
            while (!IsFinished)
            {
                Advance();
            }

            return _result;
        }
    }

    private void Advance()
    {
        try
        {
            if (!_slices.MoveNext())
            {
                Finish();
            }
        }
        catch (OperationCanceledException)
        {
            _result = default;
            Finish();
        }
        catch
        {
            Finish();
            throw;
        }
    }

    private void Finish()
    {
        IsFinished = true;
        _slices.Dispose();
    }
}

/// <summary>Pieces of stepped work.</summary>
public static class Stepping
{
    /// <summary>Runs every slice of some stepped work, here and now.</summary>
    /// <param name="slices">The work.</param>
    /// <remarks>For a caller that wants the work done rather than spread over frames: a tool, a test.</remarks>
    public static void Drain(IEnumerable<bool> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);

        foreach (bool _ in slices)
        {
        }
    }

    /// <summary>
    /// Runs a body for every index of a range: all at once on a scheduler that runs work concurrently,
    /// one index a slice on one that does not.
    /// </summary>
    /// <param name="work">The scheduler.</param>
    /// <param name="count">How many indices, from nought.</param>
    /// <param name="maxDegreeOfParallelism">The most at once where they run together; nought or less for no limit.</param>
    /// <param name="body">The work for one index. The indices must be independent of one another.</param>
    /// <param name="cancellation">Stops indices that have not started.</param>
    /// <returns>An element at the end of each slice, for a stepped job to yield on.</returns>
    public static IEnumerable<bool> Range(
        IWorkScheduler work,
        int count,
        int maxDegreeOfParallelism,
        Action<int> body,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(body);

        if (work.IsConcurrent)
        {
            work.ForRange(0, count, maxDegreeOfParallelism, body, cancellation);
            yield return true;
            yield break;
        }

        for (int i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            body(i);
            yield return true;
        }
    }
}

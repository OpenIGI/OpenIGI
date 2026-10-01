namespace OpenIGI.Core.Threading;

/// <summary>Where work the caller does not want to do on its own thread runs, as the platform allows.</summary>
/// <remarks>
/// <para>
/// A desktop has cores to spare and runs such work beside the frame loop. A browser running .NET has one
/// thread, and a task started there does not run beside anything: it runs when the loop next yields, which
/// a loop that is itself the page's frame callback never does in time. So the platform decides, and the
/// callers say only what they want done.
/// </para>
/// <para>
/// Two kinds of work go through here, and <see cref="IsConcurrent"/> is what tells them apart. Work whose
/// result is needed -- the audio device, the shared animations, a level's backdrop -- is run either way,
/// inline where nothing runs beside the caller. Work that is only an OPPORTUNITY -- decoding textures
/// ahead of the frame that asks for them, warming the file cache -- is worth doing only where it
/// overlaps with something, and a caller skips it where it would not.
/// </para>
/// </remarks>
public interface IWorkScheduler
{
    /// <summary>Gets whether work runs beside the caller, rather than inline before the call returns.</summary>
    bool IsConcurrent { get; }

    /// <summary>Runs some work.</summary>
    /// <param name="work">The work.</param>
    /// <param name="cancellation">Stops work that has not started.</param>
    /// <returns>
    /// The work, as a task: running where the scheduler is concurrent, and already finished -- run to
    /// completion, faulted or cancelled -- where it is not.
    /// </returns>
    Task Run(Action work, CancellationToken cancellation = default);

    /// <summary>Runs some work that produces a value.</summary>
    /// <typeparam name="T">What it produces.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="cancellation">Stops work that has not started.</param>
    /// <returns>The work, as a task; see <see cref="Run(Action, CancellationToken)"/>.</returns>
    Task<T> Run<T>(Func<T> work, CancellationToken cancellation = default);

    /// <summary>Runs a body for every index of a range, as many at once as the scheduler allows.</summary>
    /// <param name="fromInclusive">The first index.</param>
    /// <param name="toExclusive">One past the last.</param>
    /// <param name="maxDegreeOfParallelism">The most at once, or zero or less for no limit of the caller's own.</param>
    /// <param name="body">The work for one index.</param>
    /// <param name="cancellation">Stops indices that have not started.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellation"/> was cancelled.</exception>
    /// <exception cref="AggregateException">A body threw.</exception>
    void ForRange(int fromInclusive, int toExclusive, int maxDegreeOfParallelism, Action<int> body, CancellationToken cancellation = default);

    /// <summary>Runs a body for every item, as many at once as the scheduler allows.</summary>
    /// <typeparam name="T">What the items are.</typeparam>
    /// <param name="source">The items.</param>
    /// <param name="maxDegreeOfParallelism">The most at once, or zero or less for no limit of the caller's own.</param>
    /// <param name="body">The work for one item.</param>
    /// <param name="cancellation">Stops items that have not started.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellation"/> was cancelled.</exception>
    /// <exception cref="AggregateException">A body threw.</exception>
    void ForEach<T>(IEnumerable<T> source, int maxDegreeOfParallelism, Action<T> body, CancellationToken cancellation = default);
}

namespace OpenIGI.Core.Threading;

/// <summary>Runs work on the thread pool, beside the caller: the desktop's scheduler.</summary>
public sealed class ThreadPoolWorkScheduler : IWorkScheduler
{
    private ThreadPoolWorkScheduler()
    {
    }

    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static ThreadPoolWorkScheduler Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsConcurrent => true;

    /// <inheritdoc/>
    public Task Run(Action work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(work, cancellation);
    }

    /// <inheritdoc/>
    public Task<T> Run<T>(Func<T> work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(work, cancellation);
    }

    /// <inheritdoc/>
    public void ForRange(int fromInclusive, int toExclusive, int maxDegreeOfParallelism, Action<int> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        Parallel.For(fromInclusive, toExclusive, Options(maxDegreeOfParallelism, cancellation), body);
    }

    /// <inheritdoc/>
    public void ForEach<T>(IEnumerable<T> source, int maxDegreeOfParallelism, Action<T> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        Parallel.ForEach(source, Options(maxDegreeOfParallelism, cancellation), body);
    }

    private static ParallelOptions Options(int maxDegreeOfParallelism, CancellationToken cancellation) => new()
    {
        MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : -1,
        CancellationToken = cancellation,
    };
}

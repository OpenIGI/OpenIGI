namespace OpenIGI.Core.Threading;

/// <summary>Runs work on the caller's thread, before the call returns: the scheduler of a single-threaded host.</summary>
/// <remarks>
/// The browser's, where there is one thread and it is the page's; and a test's, which then knows exactly
/// when everything it started has happened. A task it returns is already finished, carrying the work's
/// exception or its cancellation as a task run on the pool would, so a caller that collects it later sees
/// the same thing either way.
/// </remarks>
public sealed class InlineWorkScheduler : IWorkScheduler
{
    private InlineWorkScheduler()
    {
    }

    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static InlineWorkScheduler Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsConcurrent => false;

    /// <inheritdoc/>
    public Task Run(Action work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (cancellation.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellation);
        }

        try
        {
            work();
            return Task.CompletedTask;
        }
        catch (OperationCanceledException cancelled) when (cancelled.CancellationToken == cancellation && cancellation.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellation);
        }
        catch (Exception error)
        {
            return Task.FromException(error);
        }
    }

    /// <inheritdoc/>
    public Task<T> Run<T>(Func<T> work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (cancellation.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellation);
        }

        try
        {
            return Task.FromResult(work());
        }
        catch (OperationCanceledException cancelled) when (cancelled.CancellationToken == cancellation && cancellation.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellation);
        }
        catch (Exception error)
        {
            return Task.FromException<T>(error);
        }
    }

    /// <inheritdoc/>
    /// <remarks>In order, one at a time. Exceptions are gathered as the pool's loop gathers them.</remarks>
    public void ForRange(int fromInclusive, int toExclusive, int maxDegreeOfParallelism, Action<int> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        for (int i = fromInclusive; i < toExclusive; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Invoke(body, i);
        }
    }

    /// <inheritdoc/>
    /// <remarks>In order, one at a time. Exceptions are gathered as the pool's loop gathers them.</remarks>
    public void ForEach<T>(IEnumerable<T> source, int maxDegreeOfParallelism, Action<T> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);

        foreach (T item in source)
        {
            cancellation.ThrowIfCancellationRequested();
            Invoke(body, item);
        }
    }

    /// <summary>One body, its failure wrapped the way a parallel loop wraps it.</summary>
    private static void Invoke<T>(Action<T> body, T item)
    {
        try
        {
            body(item);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new AggregateException(error);
        }
    }
}

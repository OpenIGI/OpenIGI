using OpenIGI.Core.Threading;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The inline scheduler's ordering with the thread pool's loops: a background job still finishes before
/// <see cref="Run(Action, CancellationToken)"/> returns, so a headless capture sees every load result before its first
/// tick, but a range or a set is spread over the cores as the game itself spreads it.
/// </summary>
/// <remarks>
/// A mission load's terrain height raster alone took nine of a capture's ten minutes on one core.
/// </remarks>
internal sealed class SynchronousParallelWorkScheduler : IWorkScheduler
{
    public static SynchronousParallelWorkScheduler Instance { get; } = new();

    public bool IsConcurrent => false;

    public Task Run(Action work, CancellationToken cancellation = default) =>
        InlineWorkScheduler.Instance.Run(work, cancellation);

    public Task<T> Run<T>(Func<T> work, CancellationToken cancellation = default) =>
        InlineWorkScheduler.Instance.Run(work, cancellation);

    public void ForRange(int fromInclusive, int toExclusive, int maxDegreeOfParallelism, Action<int> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        Parallel.For(
            fromInclusive,
            toExclusive,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : -1, CancellationToken = cancellation },
            body);
    }

    public void ForEach<T>(IEnumerable<T> source, int maxDegreeOfParallelism, Action<T> body, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        Parallel.ForEach(
            source,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : -1, CancellationToken = cancellation },
            body);
    }
}

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>The one thread OpenGL is called from, and the work every other thread leaves for it.</summary>
/// <remarks>
/// <para>
/// A context is current on one thread. Once the device is made it moves to a thread of its own, and every call the RHI
/// is asked for -- making a texture, uploading, submitting, presenting -- becomes work for that thread, run in the
/// order it was asked for. The caller goes on at once: the game runs its next frame while this thread hands the last
/// one to the driver, whose cost per draw is the reason OpenGL is slow where Vulkan is not. A driver that does its own
/// work on a thread of its own still makes a caller wait whenever a call needs its answer, which a frame always has
/// one of; here only this thread waits.
/// </para>
/// <para>
/// Before the thread starts, the thread that made the device owns the context and calls run at once, as they must
/// while the device is being made.
/// </para>
/// </remarks>
internal sealed class GlThread : IDisposable
{
    // How long the thread waits for work before looking at the fences someone may be watching.
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(1);

    private readonly BlockingCollection<Action> _work = [];
    private Thread? _thread;
    private Func<bool>? _idle;
    private ExceptionDispatchInfo? _fault;
    private int _owner = Environment.CurrentManagedThreadId;
    private volatile bool _stopped;

    /// <summary>Gets whether the caller is on the owning thread.</summary>
    public bool IsOwner => Environment.CurrentManagedThreadId == _owner;

    /// <summary>Moves the context onto a thread of its own, which owns it from here on.</summary>
    /// <param name="enter">Run first on the new thread: makes the context current there.</param>
    /// <param name="leave">Run last on it, as it stops: lets the context go, so the window can be destroyed.</param>
    /// <param name="idle">
    /// Run whenever the thread has no work; answers whether to be run again at once rather than after a rest.
    /// </param>
    public void Start(Action enter, Action leave, Func<bool> idle)
    {
        ArgumentNullException.ThrowIfNull(enter);
        ArgumentNullException.ThrowIfNull(leave);
        ArgumentNullException.ThrowIfNull(idle);

        if (_thread is not null)
        {
            throw new InvalidOperationException("The OpenGL thread is already running.");
        }

        _idle = idle;
        int starter = Environment.CurrentManagedThreadId;
        using ManualResetEventSlim started = new();
        ExceptionDispatchInfo? failed = null;

        _thread = new Thread(() =>
        {
            _owner = Environment.CurrentManagedThreadId;
            try
            {
                enter();
            }
            catch (Exception e)
            {
                // The context never moved: it stays the starting thread's, for it to clean up.
                failed = ExceptionDispatchInfo.Capture(e);
                _owner = starter;
                started.Set();
                return;
            }

            started.Set();
            try
            {
                Loop();
            }
            finally
            {
                leave();
            }
        })
        {
            IsBackground = true,
            Name = "OpenGL",
        };

        _thread.Start();
        started.Wait();
        if (failed is not null)
        {
            _thread.Join();
            _thread = null;
            failed.Throw();
        }
    }

    /// <summary>Runs work now on the owning thread, or leaves it for that thread.</summary>
    /// <param name="work">The work.</param>
    public void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsOwner)
        {
            Drain();
            work();
            return;
        }

        Post(work);
    }

    /// <summary>Runs work on the owning thread and waits for it, handing back what it threw.</summary>
    /// <param name="work">The work.</param>
    /// <exception cref="InvalidOperationException">No thread owns the context to run it.</exception>
    public void Invoke(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = Invoke(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>Runs work on the owning thread and waits for its answer.</summary>
    /// <typeparam name="T">What it answers.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>Its answer.</returns>
    /// <exception cref="InvalidOperationException">No thread owns the context to run it.</exception>
    public T Invoke<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsOwner)
        {
            Drain();
            return work();
        }

        if (_thread is not { IsAlive: true })
        {
            throw new InvalidOperationException(
                "OpenGL is owned by a thread that is not running, so nothing can run there to answer.");
        }

        using ManualResetEventSlim done = new();
        T answer = default!;
        ExceptionDispatchInfo? error = null;

        Post(() =>
        {
            try
            {
                answer = work();
            }
            catch (Exception e)
            {
                error = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        error?.Throw();
        return answer;
    }

    /// <summary>Runs work on the owning thread and waits for it, unless nothing is left to run it.</summary>
    /// <param name="work">The work.</param>
    /// <returns>Whether it ran.</returns>
    public bool TryInvoke(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!IsOwner && _thread is not { IsAlive: true })
        {
            return false;
        }

        Invoke(work);
        return true;
    }

    /// <summary>Runs whatever was left, in the order it was left, before the thread starts. The owning thread only.</summary>
    /// <remarks>
    /// Once the thread runs, its loop alone takes work: work that asks for more as it runs -- a texture let go as the
    /// frame is replaced -- must not run what was left after it ahead of finishing.
    /// </remarks>
    public void Drain()
    {
        if (!IsOwner || _thread is not null)
        {
            return;
        }

        while (_work.TryTake(out Action? work))
        {
            work();
        }
    }

    /// <summary>Throws, on the caller's thread, the first thing the owning thread's work threw since the last ask.</summary>
    /// <remarks>
    /// Work left for the thread has no caller to throw to, so what it throws is kept and handed to whoever next
    /// submits or presents, rather than lost with the frame it broke.
    /// </remarks>
    public void ThrowIfFaulted() => Interlocked.Exchange(ref _fault, null)?.Throw();

    /// <summary>Runs what is left, stops the thread, and lets the context go.</summary>
    public void Dispose()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _work.CompleteAdding();
        if (_thread is { } thread && thread.ManagedThreadId != Environment.CurrentManagedThreadId)
        {
            thread.Join();
        }
    }

    // Work left once the thread has stopped has no context to run in: a resource let go after the device is dropped.
    private void Post(Action work)
    {
        if (_stopped)
        {
            return;
        }

        try
        {
            _work.Add(work);
        }
        catch (InvalidOperationException) when (_stopped)
        {
            // Stopped between the look and the add.
        }
    }

    private void Loop()
    {
        bool eager = false;
        while (!_work.IsCompleted)
        {
            Action? work;
            try
            {
                if (!_work.TryTake(out work, eager ? TimeSpan.Zero : IdlePoll))
                {
                    eager = Guard(_idle!);
                    continue;
                }
            }
            catch (InvalidOperationException)
            {
                // Completed while waiting: nothing more will come.
                return;
            }

            Guard(work);
        }
    }

    private void Guard(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref _fault, ExceptionDispatchInfo.Capture(e), null);
        }
    }

    private bool Guard(Func<bool> work)
    {
        try
        {
            return work();
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref _fault, ExceptionDispatchInfo.Capture(e), null);
            return false;
        }
    }
}

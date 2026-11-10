using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A list of tasks of one kind that the game keeps beside the tree: what can be activated, what an explosion reaches, the
/// vehicles, the humans.
/// </summary>
/// <typeparam name="T">What the listed tasks have in common: a class or an interface.</typeparam>
/// <remarks>
/// <para>
/// The original's <c>QTaskList</c> (I.G.I. 2 <c>tasklist.c</c>; Project I.G.I. keeps the same lists by hand in its
/// <c>Game</c> module, <c>0x415530</c>). A task registers itself when it is created and removes itself when it is deleted;
/// the list never looks at the tree.
/// </para>
/// <para>
/// Two of its rules are behaviour and are kept. It has a fixed capacity, and filling it is fatal. And removal moves the
/// <em>last</em> task into the gap (<c>QTaskList_Remove</c>), so the order something walks the list in changes when a task
/// leaves it: that order decides who an explosion reaches first and which random number each one draws.
/// </para>
/// </remarks>
public sealed class TaskList<T>
    where T : class
{
    private readonly T[] _tasks;

    /// <summary>Initializes a new instance of the <see cref="TaskList{T}"/> class.</summary>
    /// <param name="name">What the list holds, for the message when it fills.</param>
    /// <param name="capacity">The most tasks it holds.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public TaskList(string name, int capacity)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Name = name;
        _tasks = new T[capacity];
    }

    /// <summary>Gets what the list holds.</summary>
    public string Name { get; }

    /// <summary>Gets the most tasks the list holds.</summary>
    public int Capacity => _tasks.Length;

    /// <summary>Gets how many tasks are listed.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the task at a place in the list.</summary>
    /// <param name="index">The place, from zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a listed task's.</exception>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
            return _tasks[index];
        }
    }

    /// <summary>Gets the listed tasks in the list's order.</summary>
    /// <returns>The tasks. A walk that may add or remove one reads <see cref="Count"/> and the indexer instead, as the original's loops do.</returns>
    public ReadOnlySpan<T> AsSpan() => _tasks.AsSpan(0, Count);

    /// <summary>Adds a task at the end of the list.</summary>
    /// <param name="task">The task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The list is full, which the original treats as fatal (<c>QTaskList_Register</c>).</exception>
    /// <remarks>It does not look for the task first: one registered twice is listed twice, as in the original.</remarks>
    public void Register(T task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (Count >= _tasks.Length)
        {
            throw new EngineFatalException($"The task list '{Name}' is full ({_tasks.Length}).");
        }

        _tasks[Count++] = task;
    }

    /// <summary>Adds a task unless it is already listed.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Whether it was added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The list is full.</exception>
    public bool TryRegister(T task)
    {
        if (Contains(task))
        {
            return false;
        }

        Register(task);
        return true;
    }

    /// <summary>Removes a task, moving the last one listed into its place.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Whether it was listed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public bool Remove(T task)
    {
        int index = IndexOf(task);
        if (index < 0)
        {
            return false;
        }

        Count--;
        _tasks[index] = _tasks[Count];
        _tasks[Count] = null!; // Past the count nothing is read; cleared so the list does not keep a deleted task alive.
        return true;
    }

    /// <summary>Gets whether a task is listed.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Whether it is.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public bool Contains(T task) => IndexOf(task) >= 0;

    /// <summary>Gets a task's place in the list.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Its place, from zero, or -1 when it is not listed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public int IndexOf(T task)
    {
        ArgumentNullException.ThrowIfNull(task);
        for (int i = 0; i < Count; i++)
        {
            if (ReferenceEquals(_tasks[i], task))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Empties the list.</summary>
    public void Clear()
    {
        Array.Clear(_tasks, 0, Count);
        Count = 0;
    }
}

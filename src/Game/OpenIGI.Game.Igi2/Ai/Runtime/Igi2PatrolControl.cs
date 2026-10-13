namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// A patrol being walked, <c>PatrolPathControl_t</c>: the path, the command running, the last <c>END</c> passed (where the path
/// loops back to), and the command's own state.
/// </summary>
public sealed class Igi2PatrolControl
{
    /// <summary>Gets the path, or <see langword="null"/> when none was found.</summary>
    public Igi2PatrolPathTask? Path { get; private set; }

    /// <summary>Gets the command running.</summary>
    public Igi2PatrolPathCommandTask? Current { get; private set; }

    /// <summary>Gets the last <c>END</c> command passed, or <see langword="null"/>.</summary>
    public Igi2PatrolPathCommandTask? LastEnd { get; private set; }

    /// <summary>Gets or sets a value indicating whether the soldier crouches between commands.</summary>
    public bool IsCrouch { get; set; }

    /// <summary>Gets or sets a value indicating whether it walks.</summary>
    public bool IsWalk { get; set; }

    /// <summary>Gets or sets the running command's own state: the tick a delay ends, whether an animation began; -1 unset.</summary>
    public int UserModify { get; set; } = -1;

    /// <summary>Gets the command after the current one, the last <c>END</c> or the first when at the end, <c>PatrolPath_GetNextCommand</c>.</summary>
    public Igi2PatrolPathCommandTask? Next
    {
        get
        {
            if (Path is null || Current is null)
            {
                return null;
            }

            IReadOnlyList<Igi2PatrolPathCommandTask> commands = Path.Commands;
            int at = IndexOf(commands, Current);
            if (at >= 0 && at + 1 < commands.Count)
            {
                return commands[at + 1];
            }

            return LastEnd ?? (commands.Count > 0 ? commands[0] : null);
        }
    }

    /// <summary>
    /// Starts a patrol at a command (<c>PatrolPath_InitPatrolPath</c>): a path with no commands is none; an <c>END</c> started on
    /// moves on to the command after it.
    /// </summary>
    /// <param name="path">The path, or <see langword="null"/>.</param>
    /// <param name="offset">The command to start on.</param>
    public void Init(Igi2PatrolPathTask? path, int offset)
    {
        if (path is null || path.Commands.Count <= 0)
        {
            Path = null;
            return;
        }

        Path = path;
        LastEnd = null;
        IsWalk = true;
        IsCrouch = false;
        UserModify = -1;
        Current = ScanToOffset(offset);
        if (Current?.Command == Igi2PatrolCommand.End)
        {
            Current = Next;
        }
    }

    /// <summary>
    /// Puts a patrol back as a save game kept it: its path, the command it was running and its last <c>END</c>, both by level id, the
    /// command's own state started afresh.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="currentId">The level id of the command it was running, or -1.</param>
    /// <param name="lastEndId">The level id of its last <c>END</c>, or -1.</param>
    public void Restore(Igi2PatrolPathTask path, int currentId, int lastEndId)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        Current = path.Commands.FirstOrDefault(c => c.Id == currentId) ?? (path.Commands.Count > 0 ? path.Commands[0] : null);
        LastEnd = lastEndId < 0 ? null : path.Commands.FirstOrDefault(c => c.Id == lastEndId);
        UserModify = -1;
    }

    /// <summary>The command at an offset, noting each <c>END</c> passed on the way, <c>PatrolPath_ScanToOffset</c>.</summary>
    private Igi2PatrolPathCommandTask? ScanToOffset(int offset)
    {
        IReadOnlyList<Igi2PatrolPathCommandTask> commands = Path!.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            if (i >= offset)
            {
                return commands[i];
            }

            if (commands[i].Command == Igi2PatrolCommand.End)
            {
                LastEnd = commands[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Moves to the next command (<c>PatrolPath_StepPatrolPathForward</c>): an <c>END</c> reached becomes the loop point and the
    /// command after it runs; the loop point reached again stops the patrol where it is. Two <c>END</c>s in a row are fatal.
    /// </summary>
    public void StepForward()
    {
        if (Next is not { } next)
        {
            return;
        }

        if (next.Command == Igi2PatrolCommand.End)
        {
            if (ReferenceEquals(next, LastEnd))
            {
                return;
            }

            LastEnd = next;
            Current = next;
            IReadOnlyList<Igi2PatrolPathCommandTask> commands = Path!.Commands;
            int at = IndexOf(commands, next);
            if (at < 0 || at + 1 >= commands.Count)
            {
                return;
            }

            next = commands[at + 1];
            if (next.Command == Igi2PatrolCommand.End)
            {
                throw new OpenIGI.Core.Diagnostics.EngineFatalException("END command following END command");
            }
        }

        Current = next;
    }

    private static int IndexOf(IReadOnlyList<Igi2PatrolPathCommandTask> commands, Igi2PatrolPathCommandTask command)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            if (ReferenceEquals(commands[i], command))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>A mind's patrols, the one walked on top (<c>atPatrolPathStackItems</c>, <c>nPatrolPathStackPos</c>): at most seven.</summary>
public sealed class Igi2PatrolList
{
    /// <summary>The most patrols held.</summary>
    public const int Capacity = 7;

    private readonly List<Igi2PatrolControl> _items = [];

    /// <summary>Gets the patrol walked, or <see langword="null"/>.</summary>
    public Igi2PatrolControl? Top => _items.Count > 0 ? _items[^1] : null;

    /// <summary>Gets how many are held.</summary>
    public int Count => _items.Count;

    /// <summary>Pushes a patrol; a full stack takes nothing.</summary>
    /// <param name="control">The patrol.</param>
    public void Push(Igi2PatrolControl control)
    {
        if (_items.Count < Capacity)
        {
            _items.Add(control);
        }
    }

    /// <summary>Pops the patrol walked, <c>HumanAI_PopPatrolPathStack</c>.</summary>
    public void Pop()
    {
        if (_items.Count > 0)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    /// <summary>Lets every patrol go.</summary>
    public void Clear() => _items.Clear();
}

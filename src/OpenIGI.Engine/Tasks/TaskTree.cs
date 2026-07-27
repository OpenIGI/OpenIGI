using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A live task tree: its root, the script-id table, and the create/delete/broadcast operations over both.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps one global tree per role — a level tree and a UI tree — plus a 4096-slot id table
/// (<c>0xA71890</c>) shared between them. An instance here owns both, so tests and tools can build a tree
/// without touching process-wide state.
/// </para>
/// <para>
/// See the original for the operations reproduced here: <c>Task_New</c>
/// (<c>0x4012A0</c>), <c>Task_NewSibling</c> (<c>0x4013B0</c>), <c>Task_Delete</c> (<c>0x4015F0</c>) and
/// <c>Task_SendMessage</c> (<c>0x401D80</c>).
/// </para>
/// </remarks>
public sealed class TaskTree
{
    private const string LogCategory = "Tasks";

    /// <summary>The number of script id slots, which the original fixes at 4096.</summary>
    /// <remarks>
    /// Kept exactly: <c>Task_FromId</c> (<c>0x4F1030</c>) rejects anything at or above it, so level data
    /// can and does rely on ids outside the range not resolving.
    /// </remarks>
    public const int IdCapacity = 4096;

    private readonly GameTask?[] _idTable = new GameTask?[IdCapacity];
    private readonly Dictionary<GameTask, int> _idsByTask = [];
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="TaskTree"/> class.</summary>
    /// <param name="types">The type registry tasks in this tree are created from.</param>
    /// <param name="root">The root task, or <see langword="null"/> to create a plain <see cref="RootTask"/>.</param>
    /// <param name="logger">The logger for tree diagnostics; defaults to a sink that discards them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public TaskTree(TaskTypeRegistry types, GameTask? root = null, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        Types = types;
        _logger = logger ?? NullLogger.Instance;

        if (root is null)
        {
            TaskType rootType = types.TryGet(RootTypeName, out TaskType? existing)
                ? existing
                : types.Register<RootTask>(RootTypeName);
            root = rootType.CreateInstance();
        }

        Root = root;
    }

    /// <summary>The name the original registers its root task type under.</summary>
    public const string RootTypeName = "QTask";

    /// <summary>Gets the type registry this tree creates tasks from.</summary>
    public TaskTypeRegistry Types { get; }

    /// <summary>Gets the tree root.</summary>
    public GameTask Root { get; }

    /// <summary>Gets the walker that owns the cursor stack for this tree.</summary>
    public TaskTreeWalker Walker { get; } = new();

    /// <summary>Gets the number of live tasks, the root included.</summary>
    public int Count { get; private set; } = 1;

    /// <summary>Creates a task and appends it to a parent's children.</summary>
    /// <param name="parent">The parent, or <see langword="null"/> to create an unparented task.</param>
    /// <param name="type">The type to create.</param>
    /// <returns>The new task, already constructed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public GameTask New(GameTask? parent, TaskType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        GameTask task = type.CreateInstance();
        parent?.AppendChild(task);
        Count++;
        task.Receive(TaskMessage.Create);
        return task;
    }

    /// <summary>Creates a task and inserts it at the <em>head</em> of a parent's children.</summary>
    /// <param name="parent">The parent, or <see langword="null"/> to create an unparented task.</param>
    /// <param name="type">The type to create.</param>
    /// <returns>The new task, already constructed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original's <c>Task_NewSibling</c>, selected by a one-shot flag the <c>Task_New</c> script
    /// command consumes. Identical to <see cref="New"/> apart from the insertion end.
    /// </remarks>
    public GameTask NewSibling(GameTask? parent, TaskType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        GameTask task = type.CreateInstance();
        parent?.PrependChild(task);
        Count++;
        task.Receive(TaskMessage.Create);
        return task;
    }

    /// <summary>Destroys a task and everything beneath it.</summary>
    /// <param name="task">The task to delete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Order is exactly the original's (<c>0x4015F0</c>): patch pending walk cursors, send
    /// <see cref="TaskMessage.PreDestroy"/> to this task, destroy children <em>last to first</em>, send
    /// <see cref="TaskMessage.Destroy"/> to this task, invalidate its script id, and only then unlink it.
    /// A destructor therefore still sees itself attached to its parent.
    /// </para>
    /// <para>
    /// Deleting does not unexport the task's named variables — the original leaves that to whichever
    /// type's destructor cares, and mission scripts read stale bindings as a result.
    /// </para>
    /// </remarks>
    public void Delete(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.IsDeleted)
        {
            return;
        }

        task.IsDeleted = true;
        Count--;

        Walker.NotifyDeleted(task);

        task.Receive(TaskMessage.PreDestroy);

        for (GameTask? child = task.LastChild; child is not null;)
        {
            GameTask? previous = child.PreviousSibling;
            Delete(child);
            child = previous;
        }

        task.Receive(TaskMessage.Destroy);

        ReleaseId(task);
        task.Unlink();
    }

    /// <summary>Broadcasts a message to a task and everything beneath it.</summary>
    /// <param name="task">The subtree root.</param>
    /// <param name="message">The message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public void Send(GameTask task, TaskMessage message) => Walker.Send(task, message);

    /// <summary>Runs one logic tick over the whole tree.</summary>
    public void Update() => Walker.Send(Root, TaskMessage.Update);

    /// <summary>Runs one render pass over the whole tree.</summary>
    /// <remarks>The same walk as <see cref="Update"/>; handlers must not mutate logic state.</remarks>
    public void Render() => Walker.Send(Root, TaskMessage.Render);

    /// <summary>Broadcasts <see cref="TaskMessage.PostLoad"/>, as the end of a level load does.</summary>
    public void PostLoad() => Walker.Send(Root, TaskMessage.PostLoad);

    /// <summary>Binds a script id to a task.</summary>
    /// <param name="id">The id from level data.</param>
    /// <param name="task">The task to bind.</param>
    /// <returns><see langword="false"/> when the id is already taken by a live task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the id table.</exception>
    /// <remarks>
    /// The original's <c>Task_BindId</c> (<c>0x4F1090</c>) treats a collision as fatal, but its caller
    /// checks first and quietly drops the id instead — so a collision never reaches the fatal path, and
    /// reporting it to the caller matches the behaviour level data actually sees.
    /// </remarks>
    public bool TryBindId(int id, GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, IdCapacity);

        if (_idTable[id] is not null)
        {
            return false;
        }

        ReleaseId(task);
        _idTable[id] = task;
        _idsByTask[task] = id;
        task.Id = id;
        return true;
    }

    /// <summary>Resolves a script id to a task.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The task, or <see langword="null"/> when the id is unbound or out of range.</returns>
    /// <remarks>
    /// Out-of-range reads as unbound rather than throwing, matching <c>Task_FromId</c>'s
    /// <c>id &lt; 0x1000</c> guard.
    /// </remarks>
    public GameTask? FromId(int id) =>
        (uint)id < IdCapacity ? _idTable[id] : null;

    /// <summary>Gets the script id bound to a task.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The id, or <see cref="GameTask.NoId"/> when it has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original scans all 4096 slots (<c>0x4F1050</c>). This is a dictionary lookup; the result is
    /// the same because <c>Task_BindId</c> never lets one task occupy two slots.
    /// </remarks>
    public int GetId(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return _idsByTask.TryGetValue(task, out int id) ? id : GameTask.NoId;
    }

    /// <summary>Releases every script id without deleting any task.</summary>
    /// <remarks>
    /// This is what <c>Level_Load</c> (<c>0x4F0D40</c>) does before running a level script: it frees the
    /// id cells only. Tasks are <em>not</em> deleted there, contrary to the earlier gameplay note.
    /// </remarks>
    public void ClearIds()
    {
        Array.Clear(_idTable);
        foreach (GameTask task in _idsByTask.Keys)
        {
            task.Id = GameTask.NoId;
        }

        _idsByTask.Clear();
        _logger.Debug(LogCategory, "Released all script ids.");
    }

    private void ReleaseId(GameTask task)
    {
        if (_idsByTask.Remove(task, out int id))
        {
            _idTable[id] = null;
            task.Id = GameTask.NoId;
        }
    }
}

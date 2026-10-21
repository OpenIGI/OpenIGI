using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// One authored patrol path: an id and the ordered commands a patrolling AI runs.
/// </summary>
/// <remarks>
/// <para>
/// The original does not hold this as a separate structure — the path <em>is</em> a <c>PatrolPath</c> task
/// and the commands <em>are</em> its child tasks, walked in place. This type is the same data lifted out of
/// the tree so a patrol can be reasoned about and tested without one, which is also what lets the corpus
/// check in <c>RetailPatrolPathTests</c> look at every path in the game at once.
/// </para>
/// </remarks>
public sealed class AiPatrolPath
{
    private readonly AiPatrolCommand[] _commands;

    /// <summary>Initializes a new instance of the <see cref="AiPatrolPath"/> class.</summary>
    /// <param name="id">The path id — the <c>PatrolPath</c> task's own script id.</param>
    /// <param name="commands">The commands, in child order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is <see langword="null"/>.</exception>
    public AiPatrolPath(int id, IEnumerable<AiPatrolCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Id = id;
        _commands = [.. commands];
    }

    /// <summary>Gets the path id <c>AIAction_Patrol</c> names.</summary>
    public int Id { get; }

    /// <summary>Gets the commands, in the order they are children of the path task.</summary>
    public IReadOnlyList<AiPatrolCommand> Commands => _commands;

    /// <summary>Gets the index of the <see cref="AiPatrolCommandKind.End"/> marker, or <c>-1</c>.</summary>
    /// <remarks>
    /// Everything before it runs once; everything after it is the looping body. A path with none never
    /// wraps at all — the cursor's fallback then aims at the first command instead.
    /// </remarks>
    public int EndIndex
    {
        get
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                if (_commands[i].Kind == AiPatrolCommandKind.End)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Gets a value indicating whether every command is one a human patrol can execute.</summary>
    /// <remarks>
    /// False means the path contains a <see cref="AiPatrolCommandKind.SetSpeed"/>, which would wedge a
    /// human walker on that step forever. Vehicles are unaffected.
    /// </remarks>
    public bool IsWalkableByAHuman =>
        Array.TrueForAll(_commands, c => c.Kind == AiPatrolCommandKind.End || c.Kind.IsExecutableByAHuman());

    /// <inheritdoc/>
    public override string ToString() => $"PatrolPath {Id} ({_commands.Length} command(s))";
}

/// <summary>
/// Every patrol path in a level, keyed by id — the port's stand-in for the global list at <c>0x5BDA70</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original links each <c>PatrolPath</c> task onto that list as it is parsed (<c>0x453680</c>) and
/// <c>sub_453840(id)</c> walks it comparing each task's <c>+40</c>. Holding a dictionary instead changes
/// the lookup from linear to constant, which is not observable: the list is never enumerated for anything
/// else, and a duplicate id could not be distinguished either way because the original returns the first
/// match and stops.
/// </para>
/// </remarks>
public sealed class AiPatrolPathRegistry
{
    private readonly Dictionary<int, AiPatrolPath> _paths = [];

    /// <summary>Gets the paths, in insertion order of their ids.</summary>
    public IReadOnlyCollection<AiPatrolPath> Paths => _paths.Values;

    /// <summary>Gets the number of registered paths.</summary>
    public int Count => _paths.Count;

    /// <summary>Adds a path, keeping the first registration of any id as the original's walk does.</summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> when it was added, <see langword="false"/> when the id was taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public bool Add(AiPatrolPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _paths.TryAdd(path.Id, path);
    }

    /// <summary>Looks up a path by id — <c>sub_453840</c>.</summary>
    /// <param name="id">The path id.</param>
    /// <returns>The path, or <see langword="null"/> when no path carries that id.</returns>
    /// <remarks>
    /// The original returns a null pointer for an unknown id and its caller checks, so a missing path is
    /// an ordinary outcome rather than an error.
    /// </remarks>
    public AiPatrolPath? Find(int id) => _paths.GetValueOrDefault(id);

    /// <summary>Collects every patrol path in a task tree.</summary>
    /// <param name="root">The task to search from, usually the level root.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Paths are scattered through the level's object tree wherever the author put them — often as a
    /// sibling of the <c>HumanSoldier</c> that walks them, sometimes several levels deep inside a
    /// <c>ConditionalContainer</c> — so this is a full walk rather than a scan of the root's children.
    /// </para>
    /// <para>
    /// A path whose task was given no script id cannot be named by <c>AIAction_Patrol</c> and is skipped,
    /// which matches the original: <c>0x453680</c> only links a task whose id is not −1.
    /// </para>
    /// </remarks>
    public static AiPatrolPathRegistry Build(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);

        AiPatrolPathRegistry registry = new();
        Collect(root, registry);
        return registry;
    }

    private static void Collect(GameTask task, AiPatrolPathRegistry registry)
    {
        if (IsPatrolPath(task) && task.Id != GameTask.NoId)
        {
            registry.Add(new AiPatrolPath(task.Id, ReadCommands(task)));
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry);
        }
    }

    private static bool IsPatrolPath(GameTask task) =>
        task is PatrolPathTask ||
        string.Equals(TypeNameOf(task), AiTaskTypes.PatrolPath, StringComparison.Ordinal);

    private static IEnumerable<AiPatrolCommand> ReadCommands(GameTask path)
    {
        foreach (GameTask child in path.Children)
        {
            if (TryReadCommand(child, out AiPatrolCommand command))
            {
                yield return command;
            }
        }
    }

    private static bool TryReadCommand(GameTask task, out AiPatrolCommand command)
    {
        if (task is PatrolPathCommandTask typed)
        {
            command = typed.Step;
            return true;
        }

        // A level loaded before the AI types were registered holds these as DeclaredTask, which parses the
        // same two parameters by name. Reading both shapes keeps the registry usable either way.
        if (task is DeclaredTask declared &&
            string.Equals(TypeNameOf(task), AiTaskTypes.PatrolPathCommand, StringComparison.Ordinal) &&
            declared.TryGetValue(AiPatrolCommand.KindParameter, out object? kind) &&
            declared.TryGetValue(AiPatrolCommand.OperandParameter, out object? operand) &&
            kind is int kindValue &&
            operand is int operandValue)
        {
            command = new AiPatrolCommand((AiPatrolCommandKind)kindValue, operandValue);
            return true;
        }

        command = default;
        return false;
    }

    private static string TypeNameOf(GameTask task)
    {
        try
        {
            return task.Type.Name;
        }
        catch (InvalidOperationException)
        {
            // A task built outside a tree has no type; it is simply not a patrol path.
            return string.Empty;
        }
    }
}

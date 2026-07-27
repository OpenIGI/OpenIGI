namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One registered task type: a name scripts spawn by, a parent type, and the parameter list level data
/// writes into.
/// </summary>
/// <remarks>
/// The original keeps these in a 384-entry table at <c>0xAF5EE0</c> holding a name, an instance size, a
/// parent id and a copy of the parent's 254-slot vtable taken at registration time. Method installation
/// and dispatch are ordinary C# virtual calls here; see the re-note, §2.2 and §11.
/// </remarks>
public sealed class TaskType
{
    private readonly Func<GameTask> _factory;

    internal TaskType(int id, string name, TaskType? parent, Func<GameTask> factory, bool isDeclarationOnly)
    {
        Id = id;
        Name = name;
        Parent = parent;
        _factory = factory;
        IsDeclarationOnly = isDeclarationOnly;
        Parameters = [];
    }

    /// <summary>Gets the id assigned at registration.</summary>
    /// <remarks>
    /// The original assigns the first free slot rather than a monotonic counter, so ids are reused after
    /// a type is unregistered. Nothing in level data depends on a particular id.
    /// </remarks>
    public int Id { get; }

    /// <summary>Gets the name scripts and level data spawn this type by.</summary>
    public string Name { get; }

    /// <summary>Gets the parent type, or <see langword="null"/> for a root type.</summary>
    public TaskType? Parent { get; }

    /// <summary>Gets a value indicating whether this is a root type.</summary>
    /// <remarks>The original marks these with flag bit <c>0x20000000</c> and a parent id of 385.</remarks>
    public bool IsRoot => Parent is null;

    /// <summary>
    /// Gets a value indicating whether this type exists only because level data declared it, with no
    /// implementing class behind it.
    /// </summary>
    /// <remarks>
    /// Retail level data declares every type it uses through <c>Task_DeclareParameters</c> before
    /// spawning any. That lets a whole mission's object tree load and be inspected long before all ~150
    /// task classes are implemented, which is what makes the retail corpus usable as a test fixture now.
    /// </remarks>
    public bool IsDeclarationOnly { get; }

    /// <summary>Gets the script parameters in wire order.</summary>
    public IReadOnlyList<TaskParameter> Parameters { get; internal set; }

    /// <summary>Gets a value indicating whether level data declared this type's parameters.</summary>
    /// <remarks>
    /// Distinct from having parameters: retail data declares <c>PatrolPath</c> with none at all
    /// (<c>Task_DeclareParameters("PatrolPath");</c>), which is a declaration nonetheless.
    /// </remarks>
    public bool IsDeclared { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this type's argument count depends on the data, because one of
    /// its parameters is an <c>AnimData</c>.
    /// </summary>
    public bool HasVariableArgumentCount
    {
        get
        {
            foreach (TaskParameter parameter in Parameters)
            {
                if (ScriptParamTypeInfo.IsVariableLength(parameter.Type))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the total number of script arguments an instance of this type consumes.</summary>
    /// <exception cref="NotSupportedException">
    /// <see cref="HasVariableArgumentCount"/> is <see langword="true"/>, so there is no fixed count.
    /// </exception>
    public int ArgumentCount
    {
        get
        {
            int total = 0;
            foreach (TaskParameter parameter in Parameters)
            {
                total += parameter.ArgumentCount;
            }

            return total;
        }
    }

    /// <summary>Determines whether this type is, or derives from, another.</summary>
    /// <param name="other">The candidate ancestor.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is this type or an ancestor of it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsSubtypeOf(TaskType other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (TaskType? type = this; type is not null; type = type.Parent)
        {
            if (ReferenceEquals(type, other))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    internal GameTask CreateInstance()
    {
        GameTask task = _factory();
        task.SetType(this);
        return task;
    }
}

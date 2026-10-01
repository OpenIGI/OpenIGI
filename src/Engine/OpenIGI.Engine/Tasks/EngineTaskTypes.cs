namespace OpenIGI.Engine.Tasks;

/// <summary>
/// The structural task types the engine registers at start-up, which level data spawns without ever
/// declaring.
/// </summary>
/// <remarks>
/// <para>
/// Retail <c>objects.qsc</c> opens with a <c>Task_DeclareParameters</c> block covering every type it
/// spawns — except these. They take no parameters and exist only to group other tasks, so the editor
/// emits no declaration for them and the engine is expected to know them already. In
/// <c>location0/level1</c> exactly three of the 59 spawned types are undeclared: <c>Container</c>,
/// <c>Static</c> and <c>Dynamic</c>.
/// </para>
/// <para>
/// <c>Level</c>, <c>Static</c>, <c>Dynamic</c>, <c>Idle</c> and <c>CameraBase</c> are registered by
/// <c>0x4EE970</c>; <c>Container</c> by <c>0x505C90</c>. All are direct children of the <c>QTask</c>
/// root type with an instance size of 32 — the bare header — apart from <c>Level</c> at 56.
/// </para>
/// <para>
/// They are not interchangeable, because the update message propagates one level at a time and each of
/// them answers it differently: <c>Container</c> and <c>CameraBase</c> forward to their children,
/// <c>Dynamic</c> forwards unless dynamics are frozen, <c>Level</c> forwards and then advances the level
/// clock, and <c>Static</c> and <c>Idle</c> do nothing — freezing their subtrees. Getting this wrong
/// does not throw; it silently stops or starts whole regions of a level.
/// </para>
/// </remarks>
public static class EngineTaskTypes
{
    /// <summary>The name of the root type every other type descends from.</summary>
    public const string Root = TaskTree.RootTypeName;

    /// <summary>The name of the type that roots a loaded level's subtree.</summary>
    public const string Level = "Level";

    /// <summary>The name of the type whose subtree never ticks.</summary>
    public const string Static = "Static";

    /// <summary>The name of the type whose subtree ticks unless dynamics are frozen.</summary>
    public const string Dynamic = "Dynamic";

    /// <summary>The name of the second type whose subtree never ticks.</summary>
    public const string Idle = "Idle";

    /// <summary>The name of the base type for camera tasks, which forwards updates.</summary>
    public const string CameraBase = "CameraBase";

    /// <summary>The name of the grouping type that forwards updates.</summary>
    public const string Container = "Container";

    /// <summary>The names of the structural types, in the order the engine registers them.</summary>
    /// <remarks>
    /// <b>This is the set retail Project I.G.I. uses, and a title may differ.</b> What makes a type
    /// structural is that the title's editor emits no declaration for it, which is a property of the
    /// editor rather than of the engine: I.G.I. 2 declares <see cref="Container"/> with a parameter of
    /// its own (<c>Exclude from lightmaps</c>) and so must not have it pre-registered, or the arity
    /// check rejects every one of its levels. Pass the title's own set to
    /// <see cref="Register(TaskTypeRegistry, IReadOnlyList{string})"/>.
    /// </remarks>
    public static IReadOnlyList<string> StructuralNames { get; } =
        [Level, Static, Dynamic, Idle, CameraBase, Container];

    /// <summary>Registers the root type and every structural type into a registry.</summary>
    /// <param name="types">The registry to fill.</param>
    /// <returns>The root type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <remarks>Types already registered are left alone, so a class may claim a name before this runs.</remarks>
    public static TaskType Register(TaskTypeRegistry types) => Register(types, StructuralNames);

    /// <summary>Registers the root type and a title's structural types into a registry.</summary>
    /// <param name="types">The registry to fill.</param>
    /// <param name="structuralNames">The names this title's level data spawns without declaring.</param>
    /// <returns>The root type.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="structuralNames"/> holds a name that is not structural.</exception>
    /// <remarks>
    /// Types already registered are left alone, so a class may claim a name before this runs. A name
    /// left out of <paramref name="structuralNames"/> is simply not pre-registered, which is what lets
    /// level data declare it with parameters of its own.
    /// </remarks>
    public static TaskType Register(TaskTypeRegistry types, IReadOnlyList<string> structuralNames)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(structuralNames);

        TaskType root = types.TryGet(Root, out TaskType? existing) ? existing : types.Register<RootTask>(Root);

        foreach (string name in structuralNames)
        {
            switch (name)
            {
                case Level: RegisterIfAbsent<LevelTask>(types, Level, root); break;
                case Static: RegisterIfAbsent<RootTask>(types, Static, root); break;
                case Dynamic: RegisterIfAbsent<DynamicTask>(types, Dynamic, root); break;
                case Idle: RegisterIfAbsent<RootTask>(types, Idle, root); break;
                case CameraBase: RegisterIfAbsent<ContainerTask>(types, CameraBase, root); break;
                case Container: RegisterIfAbsent<ContainerTask>(types, Container, root); break;
                default:
                    throw new ArgumentException(
                        $"'{name}' is not a structural task type; the engine has no class for it.",
                        nameof(structuralNames));
            }
        }

        return root;
    }

    private static void RegisterIfAbsent<T>(TaskTypeRegistry types, string name, TaskType root)
        where T : GameTask, new()
    {
        if (!types.TryGet(name, out _))
        {
            types.Register<T>(name, root);
        }
    }
}

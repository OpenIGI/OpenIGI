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
/// </remarks>
public static class EngineTaskTypes
{
    /// <summary>The name of the root type every other type descends from.</summary>
    public const string Root = TaskTree.RootTypeName;

    /// <summary>The names of the structural types, in the order the engine registers them.</summary>
    public static IReadOnlyList<string> StructuralNames { get; } =
        ["Level", "Static", "Dynamic", "Idle", "CameraBase", "Container"];

    /// <summary>Registers the root type and every structural type into a registry.</summary>
    /// <param name="types">The registry to fill.</param>
    /// <returns>The root type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <remarks>Types already registered are left alone, so a class may claim a name before this runs.</remarks>
    public static TaskType Register(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);

        TaskType root = types.TryGet(Root, out TaskType? existing) ? existing : types.Register<RootTask>(Root);

        foreach (string name in StructuralNames)
        {
            if (!types.TryGet(name, out _))
            {
                types.Register<RootTask>(name, root);
            }
        }

        return root;
    }
}

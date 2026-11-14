using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Flow;

/// <summary>The level task types both titles share a class for, registered in one place.</summary>
/// <remarks>
/// A title's content module registers these before a level is loaded, and so does anything that loads a level by hand. A
/// type registered here is made as its class; one that is not is made as a placeholder that holds its parameters and does
/// nothing, which is how a level could be loaded before its types were written.
/// </remarks>
public static class LevelTaskTypes
{
    /// <summary>Registers the flow types both titles make from the very same class.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterShared(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Add<EditVariableTask>(types, EditVariableTask.TypeName);
        Add<LevelTimerTask>(types, LevelTimerTask.TypeName);
    }

    /// <summary>Registers a class whose parameters stay the ones the level declares, unless a class already makes the type.</summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <param name="types">The registry.</param>
    /// <param name="name">The type's script name.</param>
    public static void AddDeclared<T>(TaskTypeRegistry types, string name)
        where T : DeclaredTask, new()
    {
        if (types.TryGet(name, out TaskType? existing) && !existing.IsDeclarationOnly)
        {
            return;
        }

        types.RegisterDeclared<T>(name);
    }

    /// <summary>
    /// Registers a class for a type, unless a class already makes it: a level's loader may register its own types before
    /// or after these.
    /// </summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <param name="types">The registry.</param>
    /// <param name="name">The type's script name.</param>
    public static void Add<T>(TaskTypeRegistry types, string name)
        where T : GameTask, new()
    {
        if (types.TryGet(name, out TaskType? existing) && !existing.IsDeclarationOnly)
        {
            return;
        }

        types.Register<T>(name);
    }
}

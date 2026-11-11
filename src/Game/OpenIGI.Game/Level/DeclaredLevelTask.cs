using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>A task with a run handler of its own whose parameters are still the ones its level declared, read by name.</summary>
/// <remarks>
/// A type's behaviour can be given to its task before its parameter list is written out as a class's members: everything
/// that reads the type's level data by name goes on reading it. Registered with
/// <see cref="TaskTypeRegistry.RegisterDeclared{T}"/>.
/// </remarks>
public abstract class DeclaredLevelTask : DeclaredTask, ILevelTicked
{
    /// <inheritdoc/>
    public abstract void Run(LevelRun level);

    /// <summary>Reads one of the parameters the level declared for this type, as the class's own member of that name.</summary>
    /// <typeparam name="T">The parameter's type.</typeparam>
    /// <param name="name">The name the level declares it under.</param>
    /// <param name="fallback">What it is when the level declared none of that name and type.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// Project I.G.I.'s levels declare each type's parameters themselves, and every level declares a type alike, so a class
    /// names its parameters once, here, and what works with the task reads the class's members.
    /// </remarks>
    protected T Declared<T>(string name, T fallback) =>
        TryGetValue(name, out object? value) && value is T typed ? typed : fallback;
}

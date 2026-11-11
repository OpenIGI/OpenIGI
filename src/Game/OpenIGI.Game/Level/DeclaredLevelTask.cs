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
}

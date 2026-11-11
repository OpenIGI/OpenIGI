using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

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
        types.Register<EditVariableTask>(EditVariableTask.TypeName);
        types.Register<LevelTimerTask>(LevelTimerTask.TypeName);
    }

    /// <summary>Registers the flow types of a Project I.G.I. level: the shared ones, and those I.G.I. 2 derives its own class from.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterFlow(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        RegisterShared(types);
        types.Register<AreaActivateTask>(AreaActivateTask.TypeName);
        types.Register<LevelFlowTask>(LevelFlowTask.TypeName);
        types.Register<Igi1DefineComputerObjectiveTask>(Igi1DefineComputerObjectiveTask.TypeName);
    }

    /// <summary>Registers every shared type a Project I.G.I. level is made of.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <remarks>I.G.I. 2 registers its own class for a conditional container, which adds what its saves keep.</remarks>
    public static void Register(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        RegisterFlow(types);
    }
}

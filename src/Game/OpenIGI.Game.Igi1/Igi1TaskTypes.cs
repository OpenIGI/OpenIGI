using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1;

/// <summary>The level task types Project I.G.I. alone has a class for, registered in one place.</summary>
/// <remarks>
/// A level's loader registers these beside the shared ones (<see cref="OpenIGI.Game.Flow.LevelTaskTypes"/>); a type registered
/// by neither is made as a placeholder that holds its parameters and does nothing.
/// </remarks>
public static class Igi1TaskTypes
{
    /// <summary>Registers the first game's own level types, leaving alone any a class already makes.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void Register(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Add<Igi1AmbientAreaTask>(types, Igi1AmbientAreaTask.TypeName);
        Add<Igi1MineFieldTask>(types, Igi1MineFieldTask.TypeName);
        AddDeclared<Ai.Igi1AlarmControlTask>(types, Ai.Igi1AlarmControlTask.TypeName);
        AddDeclared<World.Igi1SmokeTask>(types, World.Igi1SmokeTask.TypeName);
        AddDeclared<Ai.Igi1GunTask>(types, Ai.Igi1GunTask.TypeName);
    }

    // A class whose parameters stay the ones the level declares.
    private static void AddDeclared<T>(TaskTypeRegistry types, string name)
        where T : DeclaredTask, new()
    {
        if (types.TryGet(name, out TaskType? existing) && !existing.IsDeclarationOnly)
        {
            return;
        }

        types.RegisterDeclared<T>(name);
    }

    private static void Add<T>(TaskTypeRegistry types, string name)
        where T : GameTask, new()
    {
        if (types.TryGet(name, out TaskType? existing) && !existing.IsDeclarationOnly)
        {
            return;
        }

        types.Register<T>(name);
    }
}

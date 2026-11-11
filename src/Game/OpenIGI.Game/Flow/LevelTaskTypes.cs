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
        Add<EditVariableTask>(types, EditVariableTask.TypeName);
        Add<LevelTimerTask>(types, LevelTimerTask.TypeName);
    }

    /// <summary>Registers the flow types of a Project I.G.I. level: the shared ones, and those I.G.I. 2 derives its own class from.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterFlow(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        RegisterShared(types);
        Add<AreaActivateTask>(types, AreaActivateTask.TypeName);
        Add<LevelFlowTask>(types, LevelFlowTask.TypeName);
        Add<Igi1DefineComputerObjectiveTask>(types, Igi1DefineComputerObjectiveTask.TypeName);
        Add<Igi1StatusMessageTask>(types, Igi1StatusMessageTask.TypeName);
        AddDeclared<ExplodeObjectTask>(types, ExplodeObjectTask.TypeName);
        AddDeclared<RadioTask>(types, RadioTask.TypeName);
        AddDeclared<GenericPickupTask>(types, GenericPickupTask.TypeName);
        AddDeclared<Ai.AlarmLightTask>(types, Ai.AlarmLightTask.TypeName);
        AddDeclared<Ai.SirenTask>(types, Ai.SirenTask.TypeName);
        AddDeclared<DoorTask>(types, DoorTask.TypeName);
        AddDeclared<RotatingObjectTask>(types, RotatingObjectTask.TypeName);
        AddDeclared<ElevatorTask>(types, ElevatorTask.TypeName);
        AddDeclared<Ai.SecurityCameraTask>(types, Ai.SecurityCameraTask.TypeName);
        AddDeclared<Ai.SecurityCameraControlTask>(types, Ai.SecurityCameraControlTask.TypeName);
        AddDeclared<SwitchTask>(types, SwitchTask.TypeName);
        AddDeclared<TerminalTask>(types, TerminalTask.TypeName);
        AddDeclared<CabinetTask>(types, CabinetTask.TypeName);
        AddDeclared<GenericTbaTask>(types, GenericTbaTask.TypeName);
        AddDeclared<PlaceExplosiveTbaTask>(types, PlaceExplosiveTbaTask.TypeName);
        AddDeclared<FenceTask>(types, FenceTask.TypeName);
        AddDeclared<StationaryGunTask>(types, StationaryGunTask.TypeName);
        AddDeclared<WireTask>(types, WireTask.TypeName);
        AddDeclared<GeneratorTask>(types, GeneratorTask.TypeName);

        AddDeclared<AnimTaskTask>(types, AnimTaskTask.TypeName);
        AddDeclared<ConditionalSoundTask>(types, ConditionalSoundTask.TypeName);
        AddDeclared<CarTask>(types, CarTask.TypeName);
        AddDeclared<HeliTask>(types, HeliTask.TypeName);
        AddDeclared<PlaneTask>(types, PlaneTask.TypeName);
        AddDeclared<TrainTask>(types, TrainTask.TypeName);
        AddDeclared<CarAiTask>(types, CarAiTask.TypeName);
        AddDeclared<StationaryGunHolderTask>(types, StationaryGunHolderTask.TypeName);
        foreach (string name in PickupTask.TypeNames)
        {
            AddDeclared<PickupTask>(types, name);
        }
    }

    /// <summary>Registers every shared type a Project I.G.I. level is made of.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <remarks>I.G.I. 2 registers its own class for a conditional container, which adds what its saves keep.</remarks>
    public static void Register(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Add<ConditionalContainerTask>(types, ConditionalContainerTask.TypeName);
        RegisterFlow(types);
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

    // A type a class already makes is left as it is, so a level's loader may register its own types before or after these.
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

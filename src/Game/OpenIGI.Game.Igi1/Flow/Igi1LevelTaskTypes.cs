using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Flow;

/// <summary>The level task types a Project I.G.I. level is made of, registered in one place.</summary>
/// <remarks>
/// The title's content module registers these before a level is loaded, and so does anything that loads a level by hand.
/// The types both titles make from one class are <see cref="LevelTaskTypes.RegisterShared"/>'s.
/// </remarks>
public static class Igi1LevelTaskTypes
{
    /// <summary>Registers the flow types of a Project I.G.I. level: the shared ones, and those I.G.I. 2 derives its own class from.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterFlow(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        LevelTaskTypes.RegisterShared(types);
        LevelTaskTypes.Add<AreaActivateTask>(types, AreaActivateTask.TypeName);
        LevelTaskTypes.Add<LevelFlowTask>(types, LevelFlowTask.TypeName);
        LevelTaskTypes.Add<Igi1DefineComputerObjectiveTask>(types, Igi1DefineComputerObjectiveTask.TypeName);
        LevelTaskTypes.Add<Igi1StatusMessageTask>(types, Igi1StatusMessageTask.TypeName);
        LevelTaskTypes.AddDeclared<ExplodeObjectTask>(types, ExplodeObjectTask.TypeName);
        LevelTaskTypes.AddDeclared<RadioTask>(types, RadioTask.TypeName);
        LevelTaskTypes.AddDeclared<GenericPickupTask>(types, GenericPickupTask.TypeName);
        LevelTaskTypes.AddDeclared<Ai.AlarmLightTask>(types, Ai.AlarmLightTask.TypeName);
        LevelTaskTypes.AddDeclared<Ai.SirenTask>(types, Ai.SirenTask.TypeName);
        LevelTaskTypes.AddDeclared<DoorTask>(types, DoorTask.TypeName);
        LevelTaskTypes.AddDeclared<RotatingObjectTask>(types, RotatingObjectTask.TypeName);
        LevelTaskTypes.AddDeclared<ElevatorTask>(types, ElevatorTask.TypeName);
        LevelTaskTypes.AddDeclared<Ai.SecurityCameraTask>(types, Ai.SecurityCameraTask.TypeName);
        LevelTaskTypes.AddDeclared<Ai.SecurityCameraControlTask>(types, Ai.SecurityCameraControlTask.TypeName);
        LevelTaskTypes.AddDeclared<SwitchTask>(types, SwitchTask.TypeName);
        LevelTaskTypes.AddDeclared<TerminalTask>(types, TerminalTask.TypeName);
        LevelTaskTypes.AddDeclared<CabinetTask>(types, CabinetTask.TypeName);
        LevelTaskTypes.AddDeclared<GenericTbaTask>(types, GenericTbaTask.TypeName);
        LevelTaskTypes.AddDeclared<PlaceExplosiveTbaTask>(types, PlaceExplosiveTbaTask.TypeName);
        LevelTaskTypes.AddDeclared<FenceTask>(types, FenceTask.TypeName);
        LevelTaskTypes.AddDeclared<StationaryGunTask>(types, StationaryGunTask.TypeName);
        LevelTaskTypes.AddDeclared<WireTask>(types, WireTask.TypeName);
        LevelTaskTypes.AddDeclared<GeneratorTask>(types, GeneratorTask.TypeName);

        LevelTaskTypes.AddDeclared<AnimTaskTask>(types, AnimTaskTask.TypeName);
        LevelTaskTypes.AddDeclared<ConditionalSoundTask>(types, ConditionalSoundTask.TypeName);
        LevelTaskTypes.AddDeclared<CarTask>(types, CarTask.TypeName);
        LevelTaskTypes.AddDeclared<HeliTask>(types, HeliTask.TypeName);
        LevelTaskTypes.AddDeclared<PlaneTask>(types, PlaneTask.TypeName);
        LevelTaskTypes.AddDeclared<TrainTask>(types, TrainTask.TypeName);
        LevelTaskTypes.AddDeclared<CarAiTask>(types, CarAiTask.TypeName);
        LevelTaskTypes.AddDeclared<StationaryGunHolderTask>(types, StationaryGunHolderTask.TypeName);
        foreach (string name in PickupTask.TypeNames)
        {
            LevelTaskTypes.AddDeclared<PickupTask>(types, name);
        }
    }

    /// <summary>Registers every shared type a Project I.G.I. level is made of.</summary>
    /// <param name="types">The registry a level's tasks are made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <remarks>I.G.I. 2 registers its own class for a conditional container, which adds what its saves keep.</remarks>
    public static void Register(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        LevelTaskTypes.Add<ConditionalContainerTask>(types, ConditionalContainerTask.TypeName);
        RegisterFlow(types);
    }
}

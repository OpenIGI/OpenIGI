using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>I.G.I. 2's level systems, in the order its level is brought up.</summary>
/// <remarks>
/// <para>
/// The order is the original's load: the tree's update and after-load handlers, which run task by task in the tree's order,
/// gathered here by what they are about. What later systems read is made by earlier ones -- the sectors and the listener by
/// <see cref="Ambience"/>, what the detectors see by <see cref="Detectors"/>, the mounted guns and cars by their systems before
/// the AI looks them up, every list of things to activate and to move before <see cref="Activation"/> and
/// <see cref="Motions"/> -- and the level's random streams are drawn from in this order, so it is part of the simulation.
/// </para>
/// <para>A mod edits <see cref="Create"/>'s list by these names.</para>
/// </remarks>
public static class Igi2LevelSystems
{
    /// <summary>The level's services handed to every task that declares them (<see cref="Engine.Tasks.LevelServiceAttribute"/>).</summary>
    public const string Services = "Services";

    /// <summary>The flow, the menu and the player bound to its task.</summary>
    public const string Flow = "Flow";

    /// <summary>The sectors, the listener and the ambient areas.</summary>
    public const string Ambience = "Ambience";

    /// <summary>The map computer's tasks.</summary>
    public const string MapComputer = "MapComputer";

    /// <summary>The areas that count humans and cars.</summary>
    public const string Areas = "Areas";

    /// <summary>The status messages.</summary>
    public const string StatusMessages = "StatusMessages";

    /// <summary>The level flow's clock for the player and its grenades.</summary>
    public const string LevelClock = "LevelClock";

    /// <summary>What the detectors see.</summary>
    public const string Detectors = "Detectors";

    /// <summary>The level's water.</summary>
    public const string Water = "Water";

    /// <summary>The electric fences.</summary>
    public const string Fences = "Fences";

    /// <summary>The list every human tells its place to, and the mines.</summary>
    public const string Notify = "Notify";

    /// <summary>The cabinets.</summary>
    public const string Cabinets = "Cabinets";

    /// <summary>The level's model LOD settings.</summary>
    public const string ModelLod = "ModelLod";

    /// <summary>The bird flocks.</summary>
    public const string Flocks = "Flocks";

    /// <summary>The cutscene cameras' links.</summary>
    public const string CutsceneCameraLinks = "CutsceneCameraLinks";

    /// <summary>The scripted overlays.</summary>
    public const string ScreenOverlay = "ScreenOverlay";

    /// <summary>The wires.</summary>
    public const string Wires = "Wires";

    /// <summary>The ladders.</summary>
    public const string Ladders = "Ladders";

    /// <summary>The switches.</summary>
    public const string Switches = "Switches";

    /// <summary>The lifts.</summary>
    public const string Lifts = "Lifts";

    /// <summary>The doors.</summary>
    public const string Doors = "Doors";

    /// <summary>The doors' sector portals.</summary>
    public const string DoorPortals = "DoorPortals";

    /// <summary>The spline path guides.</summary>
    public const string SplineGuides = "SplineGuides";

    /// <summary>The props moved along guides.</summary>
    public const string Props = "Props";

    /// <summary>The trains.</summary>
    public const string Trains = "Trains";

    /// <summary>The animations.</summary>
    public const string Animations = "Animations";

    /// <summary>The mounted guns.</summary>
    public const string StationaryGuns = "StationaryGuns";

    /// <summary>The automatic guns.</summary>
    public const string Turrets = "Turrets";

    /// <summary>The cars.</summary>
    public const string Cars = "Cars";

    /// <summary>The AI's lookup of mounted guns by id.</summary>
    public const string StationaryGunLookup = "StationaryGunLookup";

    /// <summary>What can be blown up, destroyed or picked up.</summary>
    public const string Destructibles = "Destructibles";

    /// <summary>The level's tasks by id.</summary>
    public const string PlacedTasks = "PlacedTasks";

    /// <summary>The smokes.</summary>
    public const string Smoke = "Smoke";

    /// <summary>The level's natives.</summary>
    public const string LevelNatives = "LevelNatives";

    /// <summary>The minefields.</summary>
    public const string MineFields = "MineFields";

    /// <summary>The conveyor belts.</summary>
    public const string Conveyors = "Conveyors";

    /// <summary>The player's materials.</summary>
    public const string Materials = "Materials";

    /// <summary>What the player can activate.</summary>
    public const string Activation = "Activation";

    /// <summary>The cameras and the movers.</summary>
    public const string Motions = "Motions";

    /// <summary>The profile's difficulty.</summary>
    public const string Difficulty = "Difficulty";

    /// <summary>The soldiers' minds.</summary>
    public const string Ai = "Ai";

    /// <summary>Makes the title's systems, in order.</summary>
    /// <returns>A fresh list, which the caller may edit.</returns>
    public static LevelSystems<Igi2LevelBuild> Create() => new LevelSystems<Igi2LevelBuild>()
        .Add(System(Services, static b => b.BindTaskServices()))
        .Add(System(Flow, Igi2FlowSystems.Flow))
        .Add(System(Ambience, Igi2FlowSystems.Ambience))
        .Add(System(MapComputer, Igi2FlowSystems.MapComputer))
        .Add(System(Areas, Igi2FlowSystems.Areas))
        .Add(System(StatusMessages, Igi2FlowSystems.StatusMessages))
        .Add(System(LevelClock, Igi2FlowSystems.LevelClock))
        .Add(System(Detectors, Igi2WorldSystems.Detectors))
        .Add(System(Water, Igi2WorldSystems.LevelWater))
        .Add(System(Fences, Igi2InteractionSystems.Fences))
        .Add(System(Notify, Igi2InteractionSystems.Notify))
        .Add(System(Cabinets, Igi2InteractionSystems.Cabinets))
        .Add(System(ModelLod, Igi2WorldSystems.ModelLod))
        .Add(System(Flocks, Igi2WorldSystems.Flocks))
        .Add(System(CutsceneCameraLinks, Igi2WorldSystems.CutsceneCameraLinks))
        .Add(System(ScreenOverlay, Igi2WorldSystems.ScreenOverlay))
        .Add(System(Wires, Igi2InteractionSystems.Wires))
        .Add(System(Ladders, Igi2InteractionSystems.Ladders))
        .Add(System(Switches, Igi2InteractionSystems.Switches))
        .Add(System(Lifts, Igi2InteractionSystems.Lifts))
        .Add(System(Doors, Igi2InteractionSystems.Doors))
        .Add(System(DoorPortals, Igi2InteractionSystems.DoorPortals))
        .Add(System(SplineGuides, Igi2WorldSystems.SplineGuides))
        .Add(System(Props, Igi2InteractionSystems.Props))
        .Add(System(Trains, Igi2VehicleSystems.Trains))
        .Add(System(Animations, Igi2WorldSystems.Animations))
        .Add(System(StationaryGuns, Igi2VehicleSystems.StationaryGuns))
        .Add(System(Turrets, Igi2VehicleSystems.Turrets))
        .Add(System(Cars, Igi2VehicleSystems.Cars))
        .Add(System(StationaryGunLookup, Igi2VehicleSystems.StationaryGunLookup))
        .Add(System(Destructibles, Igi2InteractionSystems.Destructibles))
        .Add(System(PlacedTasks, Igi2ScriptSystems.PlacedTasks))
        .Add(System(Smoke, Igi2WorldSystems.Smoke))
        .Add(System(LevelNatives, Igi2ScriptSystems.LevelNatives))
        .Add(System(MineFields, Igi2InteractionSystems.MineFields))
        .Add(System(Conveyors, Igi2WorldSystems.Conveyors))
        .Add(System(Materials, Igi2WorldSystems.PlayerMaterials))
        .Add(System(Activation, Igi2InteractionSystems.Activation))
        .Add(System(Motions, Igi2InteractionSystems.Motions))
        .Add(System(Difficulty, Igi2WorldSystems.Difficulty))
        .Add(System(Ai, Igi2ScriptSystems.Ai));

    private static LevelSystem<Igi2LevelBuild> System(string name, Action<Igi2LevelBuild> build) => new(name, build);
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A mission's soldiers loaded into a renderer over a recording device, with the loop, the level and, when
/// its containers were prepared, the cutscene and objective runtimes beside them.
/// </summary>
internal sealed class SoldierRendererScenario(
    SoldierRenderer renderer,
    RecordingGraphicsDevice device,
    GameLoop loop,
    LevelTask root,
    CutsceneRuntime? cutscenes,
    MissionObjectiveRuntime? objectives) : IDisposable
{
    /// <summary>
    /// The collection every soldier renderer scenario class joins: they share the ragdoll setting, which
    /// some of them change for a run, so they take turns as when they were one class.
    /// </summary>
    public const string CollectionName = "soldier renderer scenarios";

    public SoldierRenderer Renderer { get; } = renderer;

    public RecordingGraphicsDevice Device { get; } = device;

    public GameLoop Loop { get; } = loop;

    public LevelTask Root { get; } = root;

    public CutsceneRuntime? Cutscenes { get; } = cutscenes;

    public MissionObjectiveRuntime? Objectives { get; } = objectives;

    /// <inheritdoc/>
    public void Dispose() => Renderer.Dispose();

    /// <summary>Loads a mission's soldiers into a fresh renderer, and with them the level's other runtimes when asked.</summary>
    /// <param name="prepareContainers">Whether to prepare the level's containers, terrain, collision, graphs and weapons too.</param>
    /// <param name="missionNumber">The mission.</param>
    /// <returns>The scenario; the caller disposes it.</returns>
    internal static SoldierRendererScenario CreateScenario(bool prepareContainers = false, int missionNumber = 4)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionNumber);

        CutsceneRuntime? cutscenes = null;
        MissionObjectiveRuntime? objectives = null;
        if (prepareContainers)
        {
            new ConditionalContainerGate(loop.Variables).Prepare(root);
            cutscenes = new CutsceneRuntime(root, loop.Variables);
            objectives = new MissionObjectiveRuntime(root, loop.Variables, native: cutscenes.TryInvokeNative);
        }

        RecordingGraphicsDevice device = new();
        SoldierRenderer renderer = new(device, resources);
        if (!prepareContainers)
        {
            Assert.True(renderer.Load(mission, root) > 0);
            Assert.True(renderer.Simulation.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0);
            return new SoldierRendererScenario(renderer, device, loop, root, cutscenes, objectives);
        }

        WorldScene scene = WorldScene.Build(mission, root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        Assert.True(renderer.Load(
            mission,
            root,
            mission.Terrain is { IsUsable: true } terrain ? terrain : null,
            solids.Caster()) > 0);
        Dictionary<int, GraphFile> graphs = NavGraphs.Read(
            Path.Combine(GameData.RequireRoot(), "missions", "location0", $"level{missionNumber}", "graphs"),
            root,
            NullLogger.Instance);
        Assert.True(renderer.Simulation.Animate(
            graphs,
            solids.Tracer(),
            resources,
            loop.Natives,
            root,
            variables: loop.Variables) > 0);
        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        Assert.True(weapons.Load(fileSystem, resources, loop.Natives) > 0);
        Assert.True(renderer.Arm(mission, weapons) > 0);
        return new SoldierRendererScenario(renderer, device, loop, root, cutscenes, objectives);
    }

    internal static void WriteInt(SoldierRendererScenario scenario, string name, int value) =>
        Assert.True(scenario.Loop.Variables.TryWrite(
            name,
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(value)));

    // Opens or closes a container the way the mission eventually does: its isRun, and the registration the level
    // walk's edge brings with it (0x4874E0).
    internal static void ForceContainer(SoldierRendererScenario scenario, int id, bool run)
    {
        WriteInt(scenario, $"ConditionalContainer_{id}.isRun", run ? 1 : 0);
        Assert.IsType<ConditionalContainerTask>(scenario.Root.Tree.FromId(id)).IsActive = run;
    }

    internal static int ReadInt(SoldierRendererScenario scenario, string name)
    {
        Assert.True(scenario.Loop.Variables.TryRead(name, out OpenIGI.Scripting.Runtime.QvmValue value));
        return value.ToInt32();
    }
}

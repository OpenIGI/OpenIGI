using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4's detection records take the global round-robin counter (<c>dword_5C8B08</c>) in the order retail
/// initialised them on tick 0 -- probed with a hook on <c>0x4896C0</c>.
/// </summary>
public sealed class MissionFourVisionPhaseTests
{
    private static readonly int[] Squad = [500, 501, 502, 503, 504, 505];
    private static readonly int[] WarehouseGuards = [508, 509, 510, 511, 512];
    private static readonly int[] Village = [523, 524, 525, 526, 532];

    [RequiresGameDataFact]
    public void EveryRecordTakesTheSweepPhaseRetailGaveIt()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);

        IReadOnlyDictionary<GameTask, int> phases = AiVisionPhases.Assign(root).Phases;
        int PhaseOf(int id) => phases[root.Tree.FromId(id)!];

        // The probe's order: cutscene HumanAI 2099 first, then helicopter 356, then the rest in tree order.
        Assert.Equal(0, PhaseOf(2099));
        Assert.Equal(1, PhaseOf(356));

        // Harrison's squad (HumanAI 500..505 of soldiers 400..405) and the guards around the med warehouse.
        Assert.Equal([0, 1, 2, 3, 0, 1], Squad.Select(PhaseOf));
        Assert.Equal([0, 1, 2, 3, 0], WarehouseGuards.Select(PhaseOf));
        Assert.Equal([3, 0, 1, 2, 3], Village.Select(PhaseOf));

        // A plain StationaryGun has no record; a GuardGenerator's template is not initialised at load.
        Assert.False(phases.ContainsKey(root.Tree.FromId(775)!));
        Assert.DoesNotContain(phases.Keys, task => task.Parent?.Parent is GuardGeneratorTask);
    }
}

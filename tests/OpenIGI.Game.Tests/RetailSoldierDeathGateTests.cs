using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The authored gates that open when named guards die, played through on the shipped missions.
/// </summary>
/// <remarks>
/// <para>
/// These read the real <c>objects.qvm</c> expressions rather than restating them: level 5's
/// <c>EditVariable_661</c> adds on
/// <c>EditVariable_661.nValue == 0 &amp;&amp; HumanSoldier_2025.isDead &amp;&amp; …</c> across five named
/// guards, and <c>M5_OBJ3</c> is complete only once that variable reaches one. Level 4 steps
/// <c>EditVariable_752</c> through two guard trios in turn.
/// </para>
/// <para>
/// The whole chain is exercised, not just the binding: the host reports occupants, the objective
/// runtime writes those reports through to the tasks, <c>HumanTask</c>'s export reads the latch, and
/// the authored expression reads the export.
/// </para>
/// <para>
/// <b>Why a counter steps rather than latching.</b> <c>EditVariable</c>'s per-tick update
/// (<c>sub_4889B0</c>) is <c>nValue += eval(Add)</c> then <c>nValue -= eval(Sub)</c>, on the int at
/// <c>+216</c> that <c>sub_488AD0</c> exports as <c>nValue</c> — so an Add expression that reads as a
/// condition contributes one per tick for as long as it holds. That is why every authored branch here
/// opens with a test on the counter's own current value: <c>nValue == 0 &amp;&amp; …</c> makes the
/// add one-shot, and level 4's chain of <c>(nValue == 0 &amp;&amp; trioA) || (nValue == 1 &amp;&amp;
/// trioB) || …</c> walks one step per trio. The evaluator is handed the task as its owner
/// (<c>sub_4F16E0(expr, a1)</c>), which is what lets level 5 write its own guard as
/// <c>this.nValue == 0</c>.
/// </para>
/// </remarks>
public sealed class RetailSoldierDeathGateTests
{
    private sealed record Mission(GameLoop Loop, LevelTask Root, MissionObjectiveRuntime Runtime);

    private static Mission Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        LevelLoader levels = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(registry, fileSystem, resources, levels).Load(missionId);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        return new Mission(loop, root, new MissionObjectiveRuntime(root, loop.Variables));
    }

    /// <summary>Reports the given guards dead and everything else alive, then settles the flow.</summary>
    private static void KillAndSettle(Mission mission, params int[] dead)
    {
        mission.Runtime.SetSoldierOccupants(
            dead.Select(id => new HumanOccupant(id, 1, default, IsDead: true)));

        for (int tick = 0; tick < 5; tick++)
        {
            mission.Runtime.TickLevelState();
            mission.Runtime.TickAfterLevelState();
        }
    }

    private static int Read(Mission mission, string symbol)
    {
        Assert.True(mission.Loop.Variables.TryRead(symbol, out QvmValue value), $"missing {symbol}");
        return value.ToInt32();
    }

    /// <summary>
    /// Level 5's third objective is unreachable unless all five named guards report dead — the exact
    /// blocker #118 describes. One survivor has to keep it shut, or the conjunction is not being read.
    /// </summary>
    [RequiresGameDataFact]
    public void LevelFivesThirdObjectiveNeedsAllFiveGuards()
    {
        Mission mission = Load(5);
        int[] all = [2022, 2023, 2024, 2025, 2026];

        KillAndSettle(mission);
        Assert.Equal(0, Read(mission, "EditVariable_661.nValue"));

        // Four of the five is not enough.
        KillAndSettle(mission, 2022, 2023, 2024, 2025);
        Assert.Equal(0, Read(mission, "EditVariable_661.nValue"));

        KillAndSettle(mission, all);
        Assert.Equal(1, Read(mission, "EditVariable_661.nValue"));
        Assert.All(all, id => Assert.Equal(1, Read(mission, $"HumanSoldier_{id}.isDead")));

        MissionObjective third = Assert.Single(
            mission.Runtime.Objectives,
            objective => objective.TextResource == "M5_OBJ3");
        Assert.Equal(MissionObjectiveState.Complete, third.State);
    }

    /// <summary>
    /// Level 4 walks <c>EditVariable_752</c> through two guard trios in order: 406/407/408 take it to
    /// one, and only then do 417/418/419 take it to two.
    /// </summary>
    [RequiresGameDataFact]
    public void LevelFourStepsItsCombatFlowThroughEachGuardTrioInTurn()
    {
        Mission mission = Load(4);

        KillAndSettle(mission);
        Assert.Equal(0, Read(mission, "EditVariable_752.nValue"));

        KillAndSettle(mission, 406, 407, 408);
        Assert.Equal(1, Read(mission, "EditVariable_752.nValue"));

        KillAndSettle(mission, 406, 407, 408, 417, 418, 419);
        Assert.Equal(2, Read(mission, "EditVariable_752.nValue"));
    }

    /// <summary>
    /// And the order is the authored one: the second trio on its own leaves the flow where it was,
    /// because that branch is gated on the variable having already reached one.
    /// </summary>
    /// <remarks>
    /// A fresh mission, because the latch is permanent — the original clears the byte only in the
    /// <c>Human</c> reset (<c>0x45ECD0</c>) — so a guard reported dead once cannot be reported alive
    /// again to stage the next case.
    /// </remarks>
    [RequiresGameDataFact]
    public void LevelFoursSecondGuardTrioCannotSkipTheFirst()
    {
        Mission mission = Load(4);

        KillAndSettle(mission, 417, 418, 419);
        Assert.Equal(0, Read(mission, "EditVariable_752.nValue"));
    }

    /// <summary>
    /// And the friendly squad's own losses are readable, which is what mission 4's status messages
    /// gate on — <c>HumanSoldier_400.isDead</c> and <c>HumanSoldier_428.isDead</c> each open one.
    /// </summary>
    [RequiresGameDataFact]
    public void LevelFoursFriendlyCasualtiesAreReadable()
    {
        Mission mission = Load(4);

        KillAndSettle(mission);
        Assert.Equal(0, Read(mission, "HumanSoldier_400.isDead"));
        Assert.Equal(0, Read(mission, "HumanSoldier_428.isDead"));

        KillAndSettle(mission, 400, 428);
        Assert.Equal(1, Read(mission, "HumanSoldier_400.isDead"));
        Assert.Equal(1, Read(mission, "HumanSoldier_428.isDead"));
    }
}

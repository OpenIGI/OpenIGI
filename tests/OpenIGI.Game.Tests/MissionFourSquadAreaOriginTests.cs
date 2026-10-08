using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4's Spetsnaz squad (Harrison, task 400) runs into "Team one stop 1" (<c>AreaActivate_150</c>,
/// <c>CRITERIA_HUMAN0</c>) and opens the med-warehouse guards (<c>ConditionalContainer_302</c>) on itself.
/// </summary>
/// <remarks>
/// An area counts a human at its origin, <c>Human+32</c>: the criteria callbacks <c>0x460A10</c> and
/// <c>0x460A70</c> pass <c>+32</c> to the box test <c>0x5042B0</c>. The position here is the one a probe's
/// <c>patrolwatch</c> hook read off Harrison on retail's level tick 3819, the first tick he is inside; the
/// container opened on the same tick. The box's floor sits 3308 units under that origin and above his feet,
/// so a runtime fed the feet never counts him (issue #200).
/// </remarks>
public sealed class MissionFourSquadAreaOriginTests
{
    // Retail Human+32 of HumanSoldier_400 at level tick 3819, running (state 2).
    private static readonly Vector3d RetailOrigin = new(112726873.2, -55715788.8, 175291285.6);

    [RequiresGameDataFact]
    public void HarrisonFillsTeamOneStopAtHisOriginButNotAtHisFeet()
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
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        MissionObjectiveRuntime objectives = new(root, loop.Variables);

        // By then the intro is over (ConditionalContainer_3901) and the squad's container has opened
        // (ConditionalContainer_300, AnimTask_850 past 66 s).
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3901)).IsActive = true;
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(300)).IsActive = true;
        HumanSoldierTask harrison = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(400));
        Assert.True(ConditionalContainerGate.IsTaskActive(harrison));

        Assert.Equal(1, ActiveAt(RetailOrigin));

        // His feet: the origin less a standing clip's root height (SoldierView.RootSample, 3877 units).
        Assert.Equal(0, ActiveAt(RetailOrigin - new Vector3d(0, 0, 3877)));

        int ActiveAt(Vector3d point)
        {
            objectives.SetSoldierOccupants([new HumanOccupant(400, harrison.Team, point, IsDead: false)]);
            objectives.TickLevelState(playerPosition: null, playerTeam: 0, extraPlayerPosition: null);
            Assert.True(loop.Variables.TryRead("AreaActivate_150.nActive", out QvmValue value));
            return value.ToInt32();
        }
    }
}

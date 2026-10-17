using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The shipped levels that declare the soldier's siblings: Level 10's <c>HumanSoldierRPG</c> and Level 14's
/// <c>HumanSoldierFemale</c>s.
/// </summary>
public sealed class RetailSoldierVariantTests
{
    [RequiresGameDataFact]
    public void LevelFourteensFemaleSoldiersAreSoldiersThatExportTheirDeath()
    {
        Mission mission = Mission.Load(14);
        HumanSoldierFemaleTask[] females = [.. FindAll<HumanSoldierFemaleTask>(mission.Root)];

        // 454 is the mission's end and 456 a branch; 700-702 are the rest of the class in this level.
        Assert.Equal([454, 456, 700, 701, 702], females.Select(f => f.Id).Order().ToArray());
        foreach (HumanSoldierFemaleTask female in females)
        {
            Assert.Equal(6, female.BoneHierarchy);
            Assert.True(mission.Loop.Variables.TryRead($"{female.Name}.isDead", out QvmValue dead), female.Name);
            Assert.Equal(0, dead.ToInt32());
            Assert.True(mission.Loop.Variables.TryRead($"{female.Name}.eTeam", out _), female.Name);
        }
    }

    [RequiresGameDataFact]
    public void LevelFourteenCompletesWhenHumanSoldierFemale454Dies()
    {
        // The authored completion: "HumanSoldierFemale_454.isDead && !HumanPlayer_0.isDead". Before the
        // class was registered the task loaded as a prop, the name resolved to nothing, and the level
        // could never end.
        Mission mission = Mission.Load(14);
        LevelExpressionRuntime expressions = new(mission.Loop.Variables);
        const string completion = "HumanSoldierFemale_454.isDead && !HumanPlayer_0.isDead";
        HumanSoldierFemaleTask target = Assert.IsType<HumanSoldierFemaleTask>(mission.Root.Tree.FromId(454));

        Assert.False(expressions.EvaluateCondition(completion, emptyValue: false));
        target.IsDead = true;
        Assert.True(expressions.EvaluateCondition(completion, emptyValue: false));
    }

    [RequiresGameDataFact]
    public void LevelTensRpgSoldierIsASoldierOfItsOwnClass()
    {
        Mission mission = Mission.Load(10);
        HumanSoldierRpgTask rpg = Assert.Single(FindAll<HumanSoldierRpgTask>(mission.Root));

        Assert.Equal(431, rpg.Id);
        Assert.Same(HumanSoldierClass.RpgSoldier, rpg.Class);
        Assert.True(mission.Loop.Variables.TryRead("HumanSoldierRPG_431.isDead", out _));
    }

    [RequiresGameDataTheory]
    [InlineData(10)]
    [InlineData(14)]
    public void EveryVariantBuildsItsBodyFromTheSetItsSkeletonNames(int missionId)
    {
        Mission mission = Mission.Load(missionId);
        HumanSoldierTask[] variants =
        [
            .. FindAll<HumanSoldierTask>(mission.Root).Where(s => s.Class != HumanSoldierClass.Soldier),
        ];
        Assert.NotEmpty(variants);

        foreach (HumanSoldierTask soldier in variants)
        {
            Assert.True(
                mission.Loaded.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set) && set is not null,
                $"{soldier.Name} names skeleton {soldier.BoneHierarchy}, which no pack loads.");

            SoldierView view = new(soldier, set);
            Assert.True(set.TryGet(view.StandAnimation, out _), $"{soldier.Name} stands in a clip its set lacks.");
            Assert.True(set.TryGet(soldier.Class.WalkAnimation, out _), $"{soldier.Name}'s walk is missing.");
            Assert.True(set.TryGet(soldier.Class.RunAnimation, out _), $"{soldier.Name}'s run is missing.");
            for (int state = 0; state < soldier.Class.States.StateCount; state++)
            {
                int clip = soldier.Class.States.AnimationOf(state);
                Assert.True(clip < 0 || set.TryGet(clip, out _), $"{soldier.Name}: state {state}'s clip {clip} is missing.");
            }
        }
    }

    private static IEnumerable<T> FindAll<T>(GameTask task)
        where T : GameTask
    {
        foreach (GameTask child in task.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T deeper in FindAll<T>(child))
            {
                yield return deeper;
            }
        }
    }

    private sealed record Mission(GameLoop Loop, LevelTask Root, LoadedMission Loaded)
    {
        public static Mission Load(int id)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());
            PlayerTaskTypes.RegisterAll(loop.Types);
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

            MissionRegistry missions = new();
            missions.Register(loop.Natives);
            missions.Scan(fileSystem, resources, loop.Natives);
            LoadedMission loaded = new MissionLoader(
                missions,
                fileSystem,
                resources,
                new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(id);
            return new Mission(loop, root, loaded);
        }
    }
}

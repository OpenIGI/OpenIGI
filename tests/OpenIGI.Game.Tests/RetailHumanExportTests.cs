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
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The death latches the shipped missions actually gate on, read off a loaded level rather than a
/// tree built for the test.
/// </summary>
/// <remarks>
/// Level 5's <c>EditVariable_661</c> adds on
/// <c>EditVariable_661.nValue == 0 &amp;&amp; HumanSoldier_2025.isDead &amp;&amp; …</c> for five named
/// guards, and <c>M5_OBJ3</c> is complete only once that variable reaches one — so those five names
/// existing is the difference between that objective being reachable and not. Level 1's failure gate
/// opens on <c>HumanPlayer_0.isDead || Car_1099.isExploded || HumanSoldier_4023.isDead</c>.
/// </remarks>
public sealed class RetailHumanExportTests
{
    private static (GameLoop Loop, LevelTask Root) Load(int missionId)
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
        return (loop, root);
    }

    /// <summary>Level 5's five-guard completion gate names five soldiers; all five must export.</summary>
    [RequiresGameDataFact]
    public void LevelFivesFiveGuardGateHasEveryLatchItNames()
    {
        (GameLoop loop, _) = Load(5);

        foreach (int id in new[] { 2022, 2023, 2024, 2025, 2026 })
        {
            Assert.True(
                loop.Variables.TryRead($"HumanSoldier_{id}.isDead", out QvmValue dead),
                $"HumanSoldier_{id}.isDead is unreachable, so M5_OBJ3 can never complete");
            Assert.Equal(0, dead.ToInt32());

            Assert.True(
                loop.Variables.TryRead($"HumanSoldier_{id}.eTeam", out _),
                $"HumanSoldier_{id}.eTeam is unreachable");
        }
    }

    /// <summary>And level 1's failure gate names the player and one guard.</summary>
    [RequiresGameDataFact]
    public void LevelOnesFailureGateHasThePlayerAndItsNamedGuard()
    {
        (GameLoop loop, LevelTask root) = Load(1);

        Assert.True(loop.Variables.TryRead("HumanPlayer_0.isDead", out QvmValue player));
        Assert.Equal(0, player.ToInt32());
        Assert.True(loop.Variables.TryRead("HumanPlayer_0.eTeam", out _));
        Assert.True(loop.Variables.TryRead("HumanSoldier_4023.isDead", out _));

        // And the latch is live: setting it on the task is what the gate reads, with nothing in
        // between rebinding or caching it.
        HumanPlayerTask task = Assert.IsType<HumanPlayerTask>(root.Tree.FromId(0));
        task.IsDead = true;
        Assert.True(loop.Variables.TryRead("HumanPlayer_0.isDead", out QvmValue dead));
        Assert.Equal(1, dead.ToInt32());
    }

    /// <summary>
    /// Every character in a shipped level exports, whichever subclass it is — which is the point of
    /// putting the export on the class they share.
    /// </summary>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryCharacterInTheLevelExportsBothNames(int missionId)
    {
        (GameLoop loop, LevelTask root) = Load(missionId);

        int humans = 0;
        foreach (GameTask task in Walk(root))
        {
            // An expression can only name a character by its bound id, so a task the level never bound
            // one to is unaddressable whatever it exports. Retail levels declare a few of those inside
            // cutscene subtrees; they are not what a mission gate reads.
            if (task is not HumanTask human || human.Id < 0)
            {
                continue;
            }

            humans++;
            Assert.True(
                loop.Variables.TryRead($"{human.Name}.isDead", out _),
                $"{human.Name} exports no isDead");
            Assert.True(
                loop.Variables.TryRead($"{human.Name}.eTeam", out _),
                $"{human.Name} exports no eTeam");
        }

        Assert.True(humans > 10, $"mission {missionId} declared only {humans} characters");
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        yield return root;
        foreach (GameTask child in root.Children)
        {
            foreach (GameTask found in Walk(child))
            {
                yield return found;
            }
        }
    }
}

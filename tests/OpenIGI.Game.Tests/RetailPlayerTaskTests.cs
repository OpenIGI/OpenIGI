using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the player task types against the retail level scripts that declare them.
/// </summary>
/// <remarks>
/// <para>
/// The parameter lists on the task classes are the wire format: <c>Task_New</c> parses positionally, so
/// a wrong name, a wrong type or a wrong <em>order</em> shifts every field after it and swallows the
/// child tasks that follow. This checks them against the script rather than against the C#.
/// </para>
/// </remarks>
public sealed class RetailPlayerTaskTests
{
    [RequiresGameDataFact]
    public void EveryLevelDeclaresThePlayerTypesTheSameWay()
    {
        // All fourteen levels declare them, and identically - so one transcription serves all of them.
        for (int level = 1; level <= 14; level++)
        {
            TaskTypeRegistry types = Load(level, out _);

            AssertParameters(types, PlayerTaskTypes.HumanPlayer,
                [
                    ("Position", ScriptParamType.ObjectPos),
                    ("Gamma", ScriptParamType.Angle),
                    ("Model", ScriptParamType.String16),
                    ("Team", ScriptParamType.Int32),
                ]);

            AssertParameters(types, PlayerTaskTypes.HumanPlayerInput,
                [
                    ("Record", ScriptParamType.Bool8),
                    ("Playback", ScriptParamType.Bool8),
                    ("File", ScriptParamType.String256),
                    ("Mapcomputer on expression", ScriptParamType.VarString),
                ]);
        }
    }

    [RequiresGameDataFact]
    public void TheSoldierCarriesASkeletonAndThePlayerDoesNot()
    {
        // This is the level-data half of the finding that a model does not name its own
        // skeleton. The soldier declares "Bone Heirachy" - misspelling and all - and a starting
        // animation; the player declares neither, because its constructor hard-codes skeleton 0.
        TaskTypeRegistry types = Load(1, out _);

        AssertParameters(types, PlayerTaskTypes.HumanSoldier,
            [
                ("Position", ScriptParamType.ObjectPos),
                ("Gamma", ScriptParamType.Angle),
                ("Model", ScriptParamType.String16),
                ("Team", ScriptParamType.Int32),
                ("Bone Heirachy", ScriptParamType.Int32),
                ("Stand Animation", ScriptParamType.Int32),
            ]);

        TaskType player = types.Get(PlayerTaskTypes.HumanPlayer);
        Assert.DoesNotContain(player.Parameters, p => p.Name.Contains("Bone", StringComparison.Ordinal));
        Assert.DoesNotContain(player.Parameters, p => p.Name.Contains("Animation", StringComparison.Ordinal));
    }

    [RequiresGameDataFact]
    public void LevelOnePlacesExactlyOnePlayerAndOneInput()
    {
        Load(1, out TaskTree tree);

        Dictionary<string, int> counts = [];
        Walk(tree.Root, counts);

        Assert.Equal(1, counts[PlayerTaskTypes.HumanPlayer]);
        Assert.Equal(1, counts[PlayerTaskTypes.HumanPlayerInput]);
        Assert.Equal(29, counts[PlayerTaskTypes.HumanSoldier]);
    }

    [RequiresGameDataFact]
    public void ThePlacedPlayerBindsItsFieldsFromTheScript()
    {
        Load(1, out TaskTree tree);
        HumanPlayerTask player = Find<HumanPlayerTask>(tree.Root)!;

        Assert.NotNull(player);
        Assert.NotEqual(default, player.Position);
        Assert.NotEmpty(player.Model);

        // The position is in engine units, so a level-1 spawn is thousands of units from the origin
        // rather than a handful.
        Assert.True(Math.Abs(player.Position.X) > 1000);
    }

    [RequiresGameDataFact]
    public void EverySoldierNamesASkeletonThatShips()
    {
        // The shipped sets are 0..3, 5 and 6 - there is no 004.iff. Every soldier in every level has
        // to name one of those, which is a real check on both halves.
        int[] shipped = [0, 1, 2, 3, 5, 6];
        int soldiers = 0;

        for (int level = 1; level <= 14; level++)
        {
            Load(level, out TaskTree tree);
            foreach (HumanSoldierTask soldier in FindAll<HumanSoldierTask>(tree.Root))
            {
                Assert.Contains(soldier.BoneHierarchy, shipped);
                soldiers++;
            }
        }

        Assert.True(soldiers > 300, $"only {soldiers} soldiers across the campaign");
    }

    private static void AssertParameters(
        TaskTypeRegistry types,
        string name,
        (string Name, ScriptParamType Type)[] expected)
    {
        TaskType type = types.Get(name);
        Assert.Equal(expected.Length, type.Parameters.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, type.Parameters[i].Name);
            Assert.Equal(expected[i].Type, type.Parameters[i].Type);
        }
    }

    private static void Walk(GameTask task, Dictionary<string, int> counts)
    {
        foreach (GameTask child in task.Children)
        {
            counts[child.Type.Name] = counts.GetValueOrDefault(child.Type.Name) + 1;
            Walk(child, counts);
        }
    }

    private static T? Find<T>(GameTask task)
        where T : GameTask
    {
        foreach (GameTask child in task.Children)
        {
            if (child is T match)
            {
                return match;
            }

            T? deeper = Find<T>(child);
            if (deeper is not null)
            {
                return deeper;
            }
        }

        return null;
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

    private static TaskTypeRegistry Load(int level, out TaskTree tree)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);
        PlayerTaskTypes.RegisterAll(types);

        tree = new TaskTree(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(
            System.IO.File.ReadAllBytes(GameData.Resolve($"missions/location0/level{level}/objects.qvm")));
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        return types;
    }
}

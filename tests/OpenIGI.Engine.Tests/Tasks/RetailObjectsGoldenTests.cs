using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Builds task trees from every retail level's <c>objects.qvm</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that makes the parameter arity table trustworthy. Level data is one flat argument
/// list per task — parameters first, then nested <c>Task_New</c> calls — so a parameter whose declared
/// argument count is wrong does not fail loudly. It quietly eats the child expressions that follow it,
/// or stops short and lets a child be read as a number, and the tree comes out a different shape.
/// Nothing throws; the level is simply wrong.
/// </para>
/// <para>
/// So the assertion is on shape, against ground truth nobody here produced: the source the community
/// research repository's <c>DConv</c> tool recovered from the same bytecode. Its <c>location0/level1</c>
/// <c>objects.qsc</c> contains 1360 <c>Task_New</c> calls nested up to ten deep, and the counts per
/// depth below are read straight off that text. Getting one arity wrong anywhere in the file moves
/// tasks between depths and breaks the histogram.
/// </para>
/// <para>
/// Skipped unless <c>IGI_GAME_PATH</c> points at an original installation.
/// </para>
/// </remarks>
public sealed class RetailObjectsGoldenTests
{
    private const string Level1Objects = "missions/location0/level1/objects.qvm";

    [RequiresGameDataFact]
    public void Level1_BuildsTheTreeShapeItsDecompiledSourceShows()
    {
        Loaded level = Load(GameData.Resolve(Level1Objects));

        Assert.Equal(1360, level.Tasks.Count);

        // Task_New calls per nesting depth in the recovered objects.qsc, root's children being depth 1.
        Assert.Equal(
            new Dictionary<int, int>
            {
                [1] = 4,
                [2] = 15,
                [3] = 183,
                [4] = 196,
                [5] = 505,
                [6] = 202,
                [7] = 49,
                [8] = 104,
                [9] = 66,
                [10] = 36,
            },
            level.DepthHistogram);

        Assert.Equal(59, level.Tasks.Select(t => t.Task.Type.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(264, level.Tasks.Count(t => t.Task.Id >= 0));
    }

    [RequiresGameDataFact]
    public void Level1_MatchesTheOpeningOfItsDecompiledSourceExactly()
    {
        Loaded level = Load(GameData.Resolve(Level1Objects));

        // Task_New(-1, "Container", "EnvironmentalEffects",
        //     Task_New(-1, "MipMapControl", "", TRUE, 1, -0.4, -0.4),
        //     Task_New(-1, "LODSettings", "LODSettings", 50.0, 65.0, ...),
        //     Task_New(4, "GlobalLight", "GlobalLight", 36.5, ...
        //         Task_New(-1, "GlobalLightKeyframe", "", ...),
        //         Task_New(-1, "GlobalLightKeyframe", "", ...)),
        //     Task_New(5, "Dirlight", "TheSun", TRUE, TRUE, 18.0,
        //         Task_New(-1, "DirlightKeyframe", "", ...),
        //         Task_New(-1, "DirlightKeyframe", "", ...)),
        //     Task_New(-1, "FlatSky", "TheSky", ...
        //         Task_New(-1, "FlatSkyLayer", "", ...),
        //         Task_New(-1, "FlatSkyLayer", "", ...)));
        // Task_New(-1, "Static", "",
        //     Task_New(-1, "Container", "TerrainModifiers",
        //         Task_New(-1, "Container", "Heightmaps",
        //             Task_New(-1, "HeightMap", "", ...) x3),
        //         Task_New(-1, "Container", "TerrainTexturing",
        //             Task_New(-1, "TextureModifier", "Grass", ...) x7 ...
        string[] expected =
        [
            "1 Container -1",
            "2 MipMapControl -1",
            "2 LODSettings -1",
            "2 GlobalLight 4",
            "3 GlobalLightKeyframe -1",
            "3 GlobalLightKeyframe -1",
            "2 Dirlight 5",
            "3 DirlightKeyframe -1",
            "3 DirlightKeyframe -1",
            "2 FlatSky -1",
            "3 FlatSkyLayer -1",
            "3 FlatSkyLayer -1",
            "1 Static -1",
            "2 Container -1",
            "3 Container -1",
            "4 HeightMap -1",
            "4 HeightMap -1",
            "4 HeightMap -1",
            "3 Container -1",
            "4 TextureModifier -1",
        ];

        string[] actual = level.Tasks
            .Take(expected.Length)
            .Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Depth} {t.Task.Type.Name} {t.Task.Id}"))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [RequiresGameDataFact]
    public void Level1_PreservesTheEditorLabelsFromLevelData()
    {
        Loaded level = Load(GameData.Resolve(Level1Objects));

        Assert.Equal("EnvironmentalEffects", level.Tasks[0].Task.Label);
        Assert.Equal("LODSettings", level.Tasks[2].Task.Label);
        Assert.Equal("TheSun", level.Tasks[6].Task.Label);

        // The label is decoration; scripts address the task by TypeName_id.
        Assert.Equal("Dirlight_5", level.Tasks[6].Task.Name);
    }

    [RequiresGameDataFact]
    public void EveryRetailLevelBuildsANestedTree()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("objects.qvm");
        Assert.NotEmpty(scripts);

        foreach (string script in scripts)
        {
            Loaded level = Load(script);

            // A level whose parameters were mis-counted still "loads"; what it cannot do is come out
            // deeply nested with every task accounted for.
            Assert.True(level.Tasks.Count > 100, $"{script} produced only {level.Tasks.Count} tasks.");
            Assert.True(level.MaxDepth >= 5, $"{script} nested only {level.MaxDepth} deep.");
        }
    }

    [RequiresGameDataFact]
    public void TheOnlyUndeclaredTypesAreTheStructuralOnes()
    {
        foreach (string script in GameData.EnumerateFiles("objects.qvm"))
        {
            Loaded level = Load(script);

            foreach (Visited visited in level.Tasks)
            {
                TaskType type = visited.Task.Type;

                // A level declares every type it spawns except the parameterless structural ones. If
                // any other type turned up undeclared, level data would be relying on a parameter list
                // we never saw — and would be parsed with the wrong argument count.
                Assert.True(
                    type.IsDeclared || EngineTaskTypes.StructuralNames.Contains(type.Name),
                    $"{script}: '{type.Name}' was spawned with neither a declaration nor a known engine type.");
            }
        }
    }

    [RequiresGameDataFact]
    public void ScriptIdsAreUniqueAndResolveBackToTheirTasks()
    {
        foreach (string script in GameData.EnumerateFiles("objects.qvm"))
        {
            Loaded level = Load(script);

            foreach (Visited visited in level.Tasks.Where(v => v.Task.Id >= 0))
            {
                Assert.Same(visited.Task, level.Tree.FromId(visited.Task.Id));
            }
        }
    }

    private static Loaded Load(string path)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();

        // Container, Static and Dynamic are spawned without ever being declared: they take no
        // parameters, so the editor emits nothing for them and the engine is expected to know them.
        EngineTaskTypes.Register(types);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        // Level data reads a handful of engine constants; TRUE/FALSE are the only ones objects.qvm
        // needs, and GAME_FREQUENCY appears inside expression strings rather than as bytecode.
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
        QvmInterpreter interpreter = new(program, natives);

        QvmError error = interpreter.Run();
        Assert.True(
            error == QvmError.None,
            $"{path} faulted with {error}: {interpreter.ErrorMessage} ({interpreter.ErrorIdentifier})");

        List<Visited> visited = [];
        Collect(tree.Root, 0, visited);

        return new Loaded(tree, types, visited);
    }

    private static void Collect(GameTask task, int depth, List<Visited> into)
    {
        foreach (GameTask child in task.Children)
        {
            into.Add(new Visited(child, depth + 1));
            Collect(child, depth + 1, into);
        }
    }

    private sealed record Visited(GameTask Task, int Depth);

    private sealed class Loaded(TaskTree tree, TaskTypeRegistry types, IReadOnlyList<Visited> tasks)
    {
        public TaskTree Tree => tree;

        public TaskTypeRegistry Types => types;

        public IReadOnlyList<Visited> Tasks => tasks;

        public int MaxDepth => tasks.Count == 0 ? 0 : tasks.Max(t => t.Depth);

        public Dictionary<int, int> DepthHistogram
        {
            get
            {
                Dictionary<int, int> histogram = [];
                foreach (Visited task in tasks)
                {
                    histogram[task.Depth] = histogram.GetValueOrDefault(task.Depth) + 1;
                }

                return histogram;
            }
        }
    }
}

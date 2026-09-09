using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Level;

/// <summary>
/// I.G.I. 2's levels, built into task trees by the engine written for I.G.I. 1.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here implements an I.G.I. 2 task class. <see cref="TaskTypeRegistry.Declare"/> creates a
/// <see cref="DeclaredTask"/> placeholder for any type level data declares, so a level loads
/// <em>structurally</em> — the right tree, the right types, the right parameter values — with the
/// gameplay behaviour simply absent. That is what makes the sequel's level format testable long before
/// any of its gameplay exists.
/// </para>
/// <para>
/// The one thing it must be told is which types are structural. I.G.I. 2's editor declares
/// <c>Container</c> with a parameter of its own, where I.G.I. 1's emits no declaration for it at all, so
/// pre-registering the engine's parameterless <c>Container</c> makes every I.G.I. 2 level fail the arity
/// check.
/// </para>
/// </remarks>
public sealed class RetailIgi2LevelLoadTests
{
    /// <summary>The structural types I.G.I. 2 spawns without declaring — I.G.I. 1's set less Container.</summary>
    private static readonly string[] Igi2StructuralTypes =
    [
        EngineTaskTypes.Level,
        EngineTaskTypes.Static,
        EngineTaskTypes.Dynamic,
        EngineTaskTypes.Idle,
        EngineTaskTypes.CameraBase,
    ];

    /// <summary>Every retail I.G.I. 2 level builds a task tree.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_level_builds_a_task_tree()
    {
        IReadOnlyList<string> levels = LevelScripts();
        Assert.NotEmpty(levels);

        foreach (string script in levels)
        {
            Harness harness = new(script, Igi2StructuralTypes);
            harness.Loader.LoadScript(Harness.ScriptName);

            int tasks = Count(harness.LevelRoot);
            Assert.True(tasks > 0, $"'{script}' built an empty tree.");
        }
    }

    /// <summary>
    /// The tree is built out of placeholders, and that is the point: no I.G.I. 2 task class exists yet.
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed, because a placeholder silently becoming a real class is how a level
    /// would start looking loaded while behaving like nothing at all.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_tree_is_built_from_declared_placeholders()
    {
        Harness harness = new(LevelScripts()[0], Igi2StructuralTypes);
        harness.Loader.LoadScript(Harness.ScriptName);

        List<GameTask> tasks = [];
        Collect(harness.LevelRoot, tasks);

        Assert.NotEmpty(tasks);
        Assert.Contains(tasks, static t => t is DeclaredTask);
    }

    /// <summary>Pre-registering I.G.I. 1's Container is what stops an I.G.I. 2 level loading.</summary>
    /// <remarks>
    /// The negative half of the structural-type finding. Without it the fix looks like a tweak that
    /// happened to help, rather than the one thing that was wrong.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_igi1_structural_set_rejects_an_igi2_level()
    {
        Harness harness = new(LevelScripts()[0], EngineTaskTypes.StructuralNames);

        EngineFatalException failure = Assert.Throws<EngineFatalException>(
            () => harness.Loader.LoadScript(Harness.ScriptName));

        Assert.Contains("Container", failure.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> LevelScripts()
    {
        string missions = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions");
        return
        [
            .. Directory
                .EnumerateFiles(missions, LevelLoader.ObjectsScriptName, SearchOption.AllDirectories)
                .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static int Count(GameTask task)
    {
        int total = 0;
        foreach (GameTask child in task.Children)
        {
            total += 1 + Count(child);
        }

        return total;
    }

    private static void Collect(GameTask task, List<GameTask> into)
    {
        foreach (GameTask child in task.Children)
        {
            into.Add(child);
            Collect(child, into);
        }
    }

    private sealed class Harness
    {
        public const string ScriptName = "LOCAL:objects.qvm";

        public Harness(string scriptPath, IReadOnlyList<string> structuralTypes)
        {
            Loop = new GameLoop(new ManualTimeSource(1_000), structuralTypes: structuralTypes);

            // Every identifier I.G.I. 2's level scripts reference, and there are only nine: the two
            // task commands, TRUE and FALSE, and these five constants. Counted from the symbol table of
            // all 24 objects.qvm rather than discovered one failure at a time.
            //
            // The values come from igi2.exe -- AISquad_RegisterScriptValues (0x49A880) for the AI types
            // and OverlayHolder_Open (0x533BC0) for the overlays -- rather than being guessed. A wrong
            // value would not show up here at all: it is a parameter on a placeholder task, so the tree
            // still builds, and it would surface much later looking like a gameplay bug.
            //
            // Registering them here is the shape of what a title content module will own; see the
            // IContentModule seam in the plan.
            Loop.Natives.RegisterConstant("AIType_Defensive", 0);
            Loop.Natives.RegisterConstant("AIType_Offensive", 1);
            Loop.Natives.RegisterConstant("AIType_OnVehicle", 2);
            Loop.Natives.RegisterConstant("OVERLAY_BINOCULAR", 0);
            Loop.Natives.RegisterConstant("OVERLAY_SNIPER", 2);

            LevelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));

            Resources = new ResourceRegistry(new VirtualFileSystem());
            Resources.Register(ScriptName, File.ReadAllBytes(scriptPath));
            Loader = new LevelLoader(LevelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        }

        public GameLoop Loop { get; }

        public LevelTask LevelRoot { get; }

        public ResourceRegistry Resources { get; }

        public LevelLoader Loader { get; }
    }
}

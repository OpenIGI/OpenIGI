using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// I.G.I. 2's levels, built into task trees by the engine written for I.G.I. 1.
/// </summary>
/// <remarks>
/// <para>
/// Everything the sequel needs in order to load comes from <see cref="Igi2Runtime"/> — its structural
/// type set, its content modules and the script vocabulary those modules declare. The list used to be
/// kept by hand in the test, which is the same arrangement that let the desktop host and the preview
/// loader drift apart before <see cref="TitleSession"/> existed.
/// </para>
/// <para>
/// Types no module covers still load: <see cref="TaskTypeRegistry.Declare"/> creates a
/// <see cref="DeclaredTask"/> placeholder for anything level data declares, so a level is
/// <em>structurally</em> complete — the right tree, the right types, the right parameter values — with
/// the gameplay behaviour simply absent. That is what makes the sequel's level format testable long
/// before its gameplay exists.
/// </para>
/// </remarks>
public sealed class RetailIgi2LevelLoadTests
{
    /// <summary>Every retail I.G.I. 2 level builds a task tree.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_level_builds_a_task_tree()
    {
        IReadOnlyList<string> levels = LevelScripts();
        Assert.NotEmpty(levels);

        foreach (string script in levels)
        {
            Harness harness = new(script);
            harness.Loader.LoadScript(Harness.ScriptName);

            int tasks = Count(harness.LevelRoot);
            Assert.True(tasks > 0, $"'{script}' built an empty tree.");
        }
    }

    /// <summary>
    /// The tree is still built largely out of placeholders, and that is the point: the module list covers
    /// what the gameplay milestones have reached and no more.
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed, because a placeholder silently becoming a real class is how a level
    /// would start looking loaded while behaving like nothing at all.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_tree_is_built_from_declared_placeholders()
    {
        Harness harness = new(LevelScripts()[0]);
        harness.Loader.LoadScript(Harness.ScriptName);

        List<GameTask> tasks = [];
        Collect(harness.LevelRoot, tasks);

        Assert.NotEmpty(tasks);
        Assert.Contains(tasks, static t => t is DeclaredTask);
    }

    /// <summary>Pre-registering I.G.I. 1's Container is what stops an I.G.I. 2 level loading.</summary>
    /// <remarks>
    /// The negative half of the structural-type finding, and the reason
    /// <see cref="ITitleDataLayout.StructuralTaskTypes"/> exists. Without it the fix looks like a tweak
    /// that happened to help, rather than the one thing that was wrong.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_igi1_structural_set_rejects_an_igi2_level()
    {
        Harness harness = new(LevelScripts()[0], EngineTaskTypes.StructuralNames);

        EngineFatalException failure = Assert.Throws<EngineFatalException>(
            () => harness.Loader.LoadScript(Harness.ScriptName));

        Assert.Contains("Container", failure.Message, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> LevelScripts()
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

    /// <summary>One level loaded the way a host loads it: through the title, not through a hand-kept list.</summary>
    internal sealed class Harness
    {
        public const string ScriptName = "LOCAL:objects.qvm";

        public Harness(string scriptPath, IReadOnlyList<string>? structuralTypes = null)
        {
            Igi2Runtime title = Igi2Runtime.Instance;
            Loop = new GameLoop(
                new ManualTimeSource(1_000),
                structuralTypes: structuralTypes ?? title.Layout.StructuralTaskTypes);

            Resources = new ResourceRegistry(new VirtualFileSystem());
            Resources.Register(ScriptName, File.ReadAllBytes(scriptPath));

            // Registration only. The tables a module would load live beside a mounted mission, and this
            // harness mounts nothing but the one script under test.
            Session = TitleSession.Start(
                title.Modules, Loop, new VirtualFileSystem(), Resources, loadTables: false);

            LevelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
            Loader = new LevelLoader(LevelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        }

        public GameLoop Loop { get; }

        public TitleSession Session { get; }

        public LevelTask LevelRoot { get; }

        public ResourceRegistry Resources { get; }

        public LevelLoader Loader { get; }
    }
}

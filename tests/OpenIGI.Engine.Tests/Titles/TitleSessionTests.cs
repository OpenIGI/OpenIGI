using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using Xunit;

namespace OpenIGI.Engine.Tests.Titles;

/// <summary>Registering a title's content from one list rather than from a copy of it.</summary>
public sealed class TitleSessionTests
{
    /// <summary>
    /// Two sessions started from the same module list register the same task types.
    /// </summary>
    /// <remarks>
    /// This is the test the duplication made impossible. The desktop host and the mission-preview loader
    /// each kept their own copy of the registration block, one with a comment saying it matched the
    /// other, and they had already drifted by an entry — a drift nothing could have caught, because
    /// there was no single list to compare either of them against.
    /// </remarks>
    [Fact]
    public void Two_sessions_from_one_list_register_the_same_types()
    {
        Fixture playing = new();
        Fixture preview = new();

        TitleSession.Start(Modules, playing.Loop, playing.FileSystem, playing.Resources, loadTables: false);
        TitleSession.Start(Modules, preview.Loop, preview.FileSystem, preview.Resources, loadTables: false);

        Assert.Equal(TypeNames(playing.Loop), TypeNames(preview.Loop));
        Assert.Contains("Recorded", TypeNames(playing.Loop));
    }

    /// <summary>Registration runs for every module before any module loads its tables.</summary>
    /// <remarks>
    /// The ordering the original's start-up has, and the reason the phases are separate: a module's
    /// table is built by running a script that calls natives another module declared, so every
    /// declaration has to be in place before the first table is read.
    /// </remarks>
    [Fact]
    public void Every_module_registers_before_any_module_loads()
    {
        List<string> order = [];
        Fixture fixture = new();

        TitleSession.Start(
            [new RecordingModule("A", order), new RecordingModule("B", order)],
            fixture.Loop,
            fixture.FileSystem,
            fixture.Resources);

        Assert.Equal(["register:A", "register:B", "load:A", "load:B"], order);
    }

    /// <summary>A host that only wants the tree's shape stops after registration.</summary>
    [Fact]
    public void Skipping_the_tables_runs_only_the_first_phase()
    {
        List<string> order = [];
        Fixture fixture = new();

        TitleSession.Start(
            [new RecordingModule("A", order)],
            fixture.Loop,
            fixture.FileSystem,
            fixture.Resources,
            loadTables: false);

        Assert.Equal(["register:A"], order);
    }

    /// <summary>An inherited catalogue is reused rather than rebuilt.</summary>
    /// <remarks>
    /// What keeps a preview reading the playing session's tables. Without it the preview would build a
    /// second, empty magic-object table and quietly show a level with no attachments.
    /// </remarks>
    [Fact]
    public void An_inherited_table_is_shared_not_rebuilt()
    {
        Fixture playing = new();
        Fixture preview = new();

        TitleSession first = TitleSession.Start(
            [new TableModule()], playing.Loop, playing.FileSystem, playing.Resources, loadTables: false);

        TitleSession second = TitleSession.Start(
            [new TableModule()],
            preview.Loop,
            preview.FileSystem,
            preview.Resources,
            loadTables: false,
            inherited: first.Services);

        Assert.Same(first.Services.Require<Table>(), second.Services.Require<Table>());
    }

    /// <summary>A table that will not load leaves the title running.</summary>
    /// <remarks>
    /// The original carries on with an empty table and shows the consequences in play. Refusing to start
    /// would turn one missing optional file into a game that does not run at all.
    /// </remarks>
    [Fact]
    public void A_failing_table_does_not_stop_the_session()
    {
        Fixture fixture = new();
        List<string> order = [];

        TitleSession.Start(
            [new FailingModule(), new RecordingModule("after", order)],
            fixture.Loop,
            fixture.FileSystem,
            fixture.Resources);

        Assert.Contains("load:after", order);
    }

    private static IReadOnlyList<IContentModule> Modules => [new TypeModule()];

    private static IReadOnlyList<string> TypeNames(GameLoop loop) =>
        [.. loop.Types.Types.Select(static t => t.Name).OrderBy(static n => n, StringComparer.Ordinal)];

    private sealed class Fixture
    {
        public Fixture()
        {
            Loop = new GameLoop(new ManualTimeSource(1_000));
            FileSystem = new VirtualFileSystem();
            Resources = new ResourceRegistry(FileSystem);
        }

        public GameLoop Loop { get; }

        public VirtualFileSystem FileSystem { get; }

        public ResourceRegistry Resources { get; }
    }

    private sealed class TypeModule : IContentModule
    {
        public string Name => "Types";

        public void Register(ContentRegistrationContext context) =>
            context.Types.Register<ContainerTask>("Recorded");

        public void LoadTables(ContentLoadContext context)
        {
        }
    }

    private sealed class TableModule : IContentModule
    {
        public string Name => "Table";

        public void Register(ContentRegistrationContext context) =>
            context.Services.GetOrProvide(static () => new Table());

        public void LoadTables(ContentLoadContext context)
        {
        }
    }

    private sealed class RecordingModule(string name, List<string> order) : IContentModule
    {
        public string Name => name;

        public void Register(ContentRegistrationContext context) => order.Add($"register:{name}");

        public void LoadTables(ContentLoadContext context) => order.Add($"load:{name}");
    }

    private sealed class FailingModule : IContentModule
    {
        public string Name => "Failing";

        public void Register(ContentRegistrationContext context)
        {
        }

        public void LoadTables(ContentLoadContext context) =>
            throw new Core.Diagnostics.EngineFatalException("the table is not there");
    }

    private sealed class Table;
}

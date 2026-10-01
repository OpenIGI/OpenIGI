using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Engine.Titles;

/// <summary>One title, started against one installation: its content registered and its tables loaded.</summary>
/// <remarks>
/// <para>
/// This is the single call that replaces the registration block a host used to keep by hand. Before it
/// existed the desktop host and the mission-preview loader each maintained their own copy of that block,
/// with a comment in one saying it matched the other; they had already drifted by one entry.
/// </para>
/// <para>
/// A host that only needs the tree's shape passes <c>loadTables: false</c>. That is the preview loader's
/// case: it wants every task type registered so a level builds properly typed, and none of the tables,
/// because it is not going to play the mission.
/// </para>
/// </remarks>
public sealed class TitleSession
{
    private const string LogCategory = "Title";

    private readonly ServiceCatalog _services;

    private TitleSession(IReadOnlyList<IContentModule> modules, ServiceCatalog services)
    {
        Modules = modules;
        _services = services;
    }

    /// <summary>Gets the modules this session registered, in registration order.</summary>
    public IReadOnlyList<IContentModule> Modules { get; }

    /// <summary>Gets the tables the modules published.</summary>
    public IServiceCatalog Services => _services;

    /// <summary>Registers a title's content and, optionally, loads its tables.</summary>
    /// <param name="modules">The title's content modules, in registration order.</param>
    /// <param name="loop">The loop whose registries the content is declared into.</param>
    /// <param name="fileSystem">The virtual file system tables are read from.</param>
    /// <param name="resources">The resource registry tables are read through.</param>
    /// <param name="logger">Where diagnostics go; defaults to a sink that discards them.</param>
    /// <param name="loadTables">Whether to run the second phase.</param>
    /// <param name="inherited">
    /// Tables an existing session already built, which modules reuse rather than rebuild. The
    /// mission-preview loader passes the playing session's, so a preview reads the same magic-object
    /// table rather than a second empty one.
    /// </param>
    /// <returns>The started session.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Both phases run in module order, and every module registers before any module loads. That is the
    /// original's own ordering, and it is what lets one module's table be built by running a script that
    /// calls a native another module declared.
    /// </remarks>
    public static TitleSession Start(
        IReadOnlyList<IContentModule> modules,
        GameLoop loop,
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        IIgiLogger? logger = null,
        bool loadTables = true,
        IServiceCatalog? inherited = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;
        TitleSession session = new(
            modules,
            inherited is ServiceCatalog seed ? new ServiceCatalog(seed) : new ServiceCatalog());

        ContentRegistrationContext registration = new(
            loop.Types, loop.Natives, loop.Variables, session._services, log);

        foreach (IContentModule module in modules)
        {
            module.Register(registration);
        }

        if (loadTables)
        {
            session.LoadTables(loop, fileSystem, resources, log);
        }

        return session;
    }

    /// <summary>Runs the second phase, loading every module's tables.</summary>
    /// <param name="loop">The loop whose registries the tables were declared into.</param>
    /// <param name="fileSystem">The virtual file system tables are read from.</param>
    /// <param name="resources">The resource registry tables are read through.</param>
    /// <param name="logger">Where diagnostics go; defaults to a sink that discards them.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Separate from <see cref="Start"/> so a host can register the content, do its own start-up work
    /// against the registries, and load the tables afterwards — which is what the desktop host does,
    /// because its mission scan has to run first.
    /// </remarks>
    public void LoadTables(
        GameLoop loop,
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;
        ContentLoadContext load = new(fileSystem, resources, loop.Natives, loop.Tasks, _services, log);

        foreach (IContentModule module in Modules)
        {
            try
            {
                module.LoadTables(load);
            }
            catch (EngineFatalException ex)
            {
                // One table failing is not the same as the title failing. The original carries on with
                // an empty table and shows the consequences in play; refusing to start would turn a
                // missing optional file into a game that will not run at all.
                log.Warning(LogCategory, $"The {module.Name} module's tables did not load: {ex.Message}");
            }
        }
    }
}

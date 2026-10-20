using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Engine.Titles;

/// <summary>
/// One game started inside another's session, against its own installation: its own loop, file system and tables, which the
/// host game's level borrows from.
/// </summary>
/// <remarks>
/// <para>
/// A game's content declares its task types, natives and script variables into one loop's registries, and the two games
/// name many of the same things differently (both have a <c>HumanSoldier</c>, with other parameters). So a guest keeps a loop
/// of its own: its task types and natives are never mixed into the host's, its tables read its own files through its own
/// devices, and the tasks it makes live in its own tree, ticked by whatever the host's level binds to them.
/// </para>
/// <para>
/// Started as the host starts a game: the installation's file system configuration applied over a device at its root, the
/// layout's structural task types, and every content module registered before any loads its tables. Its clock runs only
/// when the host advances it.
/// </para>
/// </remarks>
public sealed class GuestTitle
{
    private GuestTitle(IGameTitleRuntime runtime, GameLoop loop, VirtualFileSystem fileSystem, ResourceRegistry resources, TitleSession session)
    {
        Runtime = runtime;
        Loop = loop;
        FileSystem = fileSystem;
        Resources = resources;
        Session = session;
    }

    /// <summary>Gets the game.</summary>
    public IGameTitleRuntime Runtime { get; }

    /// <summary>Gets the guest's own loop: its task types, natives, script variables and task tree.</summary>
    public GameLoop Loop { get; }

    /// <summary>Gets the guest's file system, over its own installation.</summary>
    public VirtualFileSystem FileSystem { get; }

    /// <summary>Gets the resources the guest's tables were read through.</summary>
    public ResourceRegistry Resources { get; }

    /// <summary>Gets the guest's session, whose services hold its tables.</summary>
    public TitleSession Session { get; }

    /// <summary>Starts a game as a guest.</summary>
    /// <param name="runtime">The game.</param>
    /// <param name="dataRoot">Where the game is installed.</param>
    /// <param name="logger">Where its diagnostics go; defaults to a sink that discards them.</param>
    /// <returns>The guest, its tables loaded.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static GuestTitle Start(IGameTitleRuntime runtime, string dataRoot, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        VirtualFileSystem fileSystem = new(log);
        fileSystem.RegisterDevice(new LocalFileDevice(dataRoot));
        FileSystemConfig.LoadOrDefault(Path.Combine(dataRoot, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource(), logger: log, structuralTypes: runtime.Layout.StructuralTaskTypes);
        TitleSession session = TitleSession.Start(runtime.Modules, loop, fileSystem, resources, log);
        return new GuestTitle(runtime, loop, fileSystem, resources, session);
    }
}

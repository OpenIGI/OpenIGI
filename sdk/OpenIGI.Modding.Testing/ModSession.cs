using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Level;
using OpenIGI.TestSupport;

namespace OpenIGI.Modding.Testing;

/// <summary>A mod's modules registered and loaded, as a session of a title has them (<see cref="ModHarness.Start"/>).</summary>
/// <remarks>
/// The session is the title's own in what the mod reads of it: the game events its listeners hear, and the games'
/// installations its <see cref="IModContext.TryGetDataRoot"/> finds (<c>IGI_GAME_PATH</c>, <c>IGI2_GAME_PATH</c>). Its file
/// system has nothing mounted: a mod that reads files is tested on a mission (<see cref="MissionFixture"/>).
/// </remarks>
public sealed class ModSession
{
    private ModSession(GameLoop loop, ServiceCatalog services)
    {
        Loop = loop;
        Services = services;
    }

    /// <summary>Gets the loop whose task types, natives and tasks the modules registered into.</summary>
    public GameLoop Loop { get; }

    /// <summary>Gets what the modules published.</summary>
    public IServiceCatalog Services { get; }

    /// <summary>Gets the game events the mod's listeners hear.</summary>
    public GameEvents Events => Services.Require<GameEvents>();

    /// <summary>The level systems a host lists, as the mod edits them.</summary>
    /// <typeparam name="TBuild">The level build the systems run on.</typeparam>
    /// <param name="hostSystems">The host's systems, by name, in its order.</param>
    /// <returns>Every system's name, in the order the edited list runs them.</returns>
    public IReadOnlyList<string> LevelSystems<TBuild>(params string[] hostSystems)
        where TBuild : LevelBuild
    {
        ArgumentNullException.ThrowIfNull(hostSystems);
        LevelSystems<TBuild> host = new();
        foreach (string name in hostSystems)
        {
            host.Add(new LevelSystem<TBuild>(name, static _ => { }));
        }

        return Services.TryGet(out LevelSystemEdits? edits) ? [.. edits.Apply(host).Select(static s => s.Name)] : [.. hostSystems];
    }

    /// <summary>Raises a game event, as the game does, to the mod's listeners.</summary>
    /// <typeparam name="TEvent">The event's type.</typeparam>
    /// <param name="gameEvent">The event.</param>
    public void Raise<TEvent>(in TEvent gameEvent)
        where TEvent : struct, IGameEvent => Events.Raise(gameEvent);

    internal static ModSession Start(IEnumerable<IContentModule> modules)
    {
        GameLoop loop = new(new ManualTimeSource());
        ServiceCatalog services = new();
        services.Provide(new GameEvents());
        services.Provide<ITitleDataLocator>(new InstalledGames());
        List<IContentModule> mine = [.. modules];
        ContentRegistrationContext registration = new(loop.Types, loop.Natives, new ScriptVarRegistry(loop.Natives), services, NullLogger.Instance);
        foreach (IContentModule module in mine)
        {
            module.Register(registration);
        }

        VirtualFileSystem files = new();
        ContentLoadContext load = new(files, new ResourceRegistry(files), loop.Natives, loop.Tasks, services, NullLogger.Instance);
        foreach (IContentModule module in mine)
        {
            module.LoadTables(load);
        }

        return new ModSession(loop, services);
    }

    // The games' installations as the test machine has them.
    private sealed class InstalledGames : ITitleDataLocator
    {
        public bool TryGetDataRoot(GameTitle title, [NotNullWhen(true)] out string? dataRoot)
        {
            dataRoot = GameDataSource.All.FirstOrDefault(source => source.Title == title)?.Root;
            return dataRoot is not null;
        }
    }
}

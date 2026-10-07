using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>An I.G.I. 2 session over the installed game, with the tables it reads before any level loaded.</summary>
/// <param name="Session">The session, whose services hold the tables.</param>
/// <param name="Loop">The loop, whose namespace and task tree the tables were read into.</param>
internal sealed record Igi2Session(TitleSession Session, GameLoop Loop)
{
    /// <summary>Starts a session as the game does at start-up.</summary>
    /// <returns>The session.</returns>
    public static Igi2Session Start()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        GameLoop loop = new(new ManualTimeSource(1_000), structuralTypes: Igi2Runtime.Instance.Layout.StructuralTaskTypes);
        TitleSession session = TitleSession.Start(Igi2Runtime.Instance.Modules, loop, files, new ResourceRegistry(files), loadTables: true);
        return new Igi2Session(session, loop);
    }

    /// <summary>Gets a table the session published.</summary>
    /// <typeparam name="T">The table's type.</typeparam>
    /// <returns>The table.</returns>
    public T Require<T>()
        where T : class => Session.Services.Require<T>();
}

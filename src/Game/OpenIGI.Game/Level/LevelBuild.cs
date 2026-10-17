using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;

namespace OpenIGI.Game.Level;

/// <summary>
/// A level being brought up: its tree, its run, and the session's services, which every <see cref="ILevelSystem{TBuild}"/>
/// reads and adds to.
/// </summary>
/// <remarks>
/// A title derives from this to carry what its own systems hand each other -- the player, the collision world, the lists of
/// things that can be activated or moved -- in the order its systems run. A system written against this base alone runs in
/// any title, which is what a mod that spans titles writes.
/// </remarks>
public abstract class LevelBuild
{
    /// <summary>Initializes a new instance of the <see cref="LevelBuild"/> class.</summary>
    /// <param name="run">The level's run, whose root is the level's tree.</param>
    /// <param name="services">The session's services.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected LevelBuild(LevelRun run, IServiceCatalog services)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Gets the level's run, which a system binds the tasks it drives to.</summary>
    public LevelRun Run { get; }

    /// <summary>Gets the level's root task.</summary>
    public GameTask Root => Run.Root;

    /// <summary>Gets the session's services: the tables the content modules loaded, and what the title published.</summary>
    public IServiceCatalog Services { get; }
}

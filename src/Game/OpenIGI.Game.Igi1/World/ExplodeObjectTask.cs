using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>A task that is an <c>ExplodeObject</c> or one of the types built on it, and so runs an explode object's update.</summary>
/// <remarks>
/// In the original a terminal, a timed action, a generator, a radio, an alarm control and a camera control are registered
/// under <c>ExplodeObject</c>, and each one's run handler calls its parent's first. Here they are classes of their own, so
/// what they share is this: the update, handed to the task by what keeps the level's destructibles.
/// </remarks>
public interface IExplodeObjectTask
{
    /// <summary>Gets or sets the explode object's update, or <see langword="null"/> for a task the level's destructibles do not keep.</summary>
    ExplodeObjectUpdate? Explosion { get; set; }
}

/// <summary>
/// One explode object's update, as its task runs it where the level's walk reaches it: its explosion expression, its fuse,
/// and the blast they set off (<c>0x481970</c>, after the handler has run the task's children).
/// </summary>
public sealed class ExplodeObjectUpdate
{
    private readonly DestructibleObjectRegistry _registry;
    private readonly DestructibleObject _item;

    /// <summary>Initializes a new instance of the <see cref="ExplodeObjectUpdate"/> class.</summary>
    /// <param name="registry">What keeps the level's destructibles.</param>
    /// <param name="item">The object.</param>
    internal ExplodeObjectUpdate(DestructibleObjectRegistry registry, DestructibleObject item)
    {
        _registry = registry;
        _item = item;
    }

    /// <summary>Runs the update.</summary>
    public void Run() => _registry.RunFromWalk(_item);
}

/// <summary>Something that blows up: Project I.G.I.'s <c>ExplodeObject</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x481970</c>) runs its children, then, unless it has exploded, reads its explosion expression and
/// explodes when that holds or its fuse has run out. The state is kept with the level's other destructibles
/// (<see cref="DestructibleObjectRegistry"/>); the task runs its own where the level's walk reaches it, so what follows it
/// in the tree sees this tick's <c>isExploded</c> and what precedes it last tick's.
/// </remarks>
public sealed class ExplodeObjectTask : DeclaredLevelTask, IExplodeObjectTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ExplodeObject";

    /// <inheritdoc/>
    public ExplodeObjectUpdate? Explosion { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Explosion?.Run();
    }
}

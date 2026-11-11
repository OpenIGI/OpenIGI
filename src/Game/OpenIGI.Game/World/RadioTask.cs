using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>A radio that chatters while it is on: Project I.G.I.'s <c>Radio</c>, built on <c>ExplodeObject</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x43AF90</c>) runs the <c>ExplodeObject</c>'s first -- its children, then its blast -- and then its
/// own: off once blown up, else as its expression says; while on it plays one of its four sounds, drawn from the level's
/// stream, and waits a drawn while before the next. The state is kept beside the task (<see cref="RadioRuntime"/>); the task
/// runs it where the level's walk reaches it, so its draws fall in the stream where the tree places the radio.
/// </remarks>
public sealed class RadioTask : DeclaredLevelTask, IExplodeObjectTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Radio";

    /// <inheritdoc/>
    public ExplodeObjectUpdate? Explosion { get; set; }

    /// <summary>Gets or sets the radio's state, or <see langword="null"/> until the level's interactions keep it.</summary>
    internal RadioRuntime? Radio { get; set; }

    /// <summary>Gets or sets the level's interactions, which hold where the radios' sounds go.</summary>
    internal WorldInteractionRegistry? Interactions { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Explosion?.Run();
        if (Radio is { } radio && Interactions is { RunsInWalk: true } interactions)
        {
            radio.Tick(interactions.RadioSounds);
        }
    }
}

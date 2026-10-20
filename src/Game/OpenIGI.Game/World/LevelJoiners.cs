using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>
/// The members of the engine's level-wide lists that the level's own tree did not make: another game's tasks, standing in the
/// level beside its own.
/// </summary>
/// <remarks>
/// <para>
/// The engine keeps a level's explosion targets and its humans in lists each task joins as it is made (Project I.G.I.'s
/// <c>dword_57BAA8</c> and <c>0x460C60</c>, I.G.I. 2's <c>GameFunctions_ptExplosionQTaskList</c> and <c>Human_ptQTaskList</c>),
/// so a blast and an AI event reach them in the order they were made. A game reads its own members off its level's tree; the
/// tasks another game made for the level live in that game's tree, so they join here, after every task the level made, in the
/// order they join.
/// </para>
/// </remarks>
public sealed class LevelJoiners
{
    private readonly List<IExplosionHitTarget> _explosionTargets = [];
    private readonly List<IHumanAiEventTarget> _humans = [];

    /// <summary>Gets the explosion targets that joined, in the order they joined.</summary>
    public IReadOnlyList<IExplosionHitTarget> ExplosionTargets => _explosionTargets;

    /// <summary>Gets the humans that joined, in the order they joined.</summary>
    public IReadOnlyList<IHumanAiEventTarget> Humans => _humans;

    /// <summary>A task joins the explosion list.</summary>
    /// <param name="target">The task.</param>
    public void JoinExplosions(IExplosionHitTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _explosionTargets.Add(target);
    }

    /// <summary>A human joins the human list.</summary>
    /// <param name="human">The human.</param>
    public void JoinHumans(IHumanAiEventTarget human)
    {
        ArgumentNullException.ThrowIfNull(human);
        _humans.Add(human);
    }

    /// <summary>Empties both lists, as a level is left.</summary>
    public void Clear()
    {
        _explosionTargets.Clear();
        _humans.Clear();
    }
}

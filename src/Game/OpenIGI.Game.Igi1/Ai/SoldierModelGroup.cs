namespace OpenIGI.Game.Igi1.Ai;

/// <summary>The level's soldiers of one model, in the order the level made them: the simulation's roster.</summary>
/// <param name="body">What the simulation reads of the model they share.</param>
/// <remarks>
/// The level's soldiers are walked group by group and, within a group, in the order they were made -- their spawn, then
/// any a generator raised since -- and that walk is the order the brains tick in, so it is part of the simulation. Each
/// group's renderer draws the same soldiers in the same order.
/// </remarks>
public sealed class SoldierModelGroup(SoldierBodyModel body)
{
    private readonly List<SoldierAgent> _agents = [];

    /// <summary>Gets what the simulation reads of the model.</summary>
    public SoldierBodyModel Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    /// <summary>Gets the group's soldiers, in the order they were made.</summary>
    public IReadOnlyList<SoldierAgent> Agents => _agents;

    /// <summary>Adds a soldier the level or a generator made.</summary>
    /// <param name="agent">The soldier.</param>
    public void Add(SoldierAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents.Add(agent);
    }
}

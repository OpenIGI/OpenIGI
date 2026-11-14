namespace OpenIGI.Scripting.Vocabulary;

/// <summary>One of the fixed-size lists a game keeps its tasks in, and the task types that take a place in it.</summary>
/// <remarks>
/// The original makes each list once, at a size of its own, and stops with a fatal error when one more task is added than
/// it holds. A task takes a place when it is created, so a level's script alone says how full a list starts; what the game
/// adds while it plays (thrown grenades, laid mines, parts of models) comes on top, which <see cref="Reserve"/> leaves room for.
/// </remarks>
/// <param name="Name">The list's name, in a few plain words (<c>people</c>).</param>
/// <param name="Capacity">How many tasks the list holds.</param>
/// <param name="TaskTypes">The task types a level's script makes that take a place each.</param>
public sealed record TaskListLimit(string Name, int Capacity, IReadOnlyList<string> TaskTypes)
{
    /// <summary>Gets how many places to leave free for what the game adds as it plays.</summary>
    public int Reserve { get; init; }

    /// <summary>Gets where the original makes the list, for a reader checking the number.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets how many places a level's script may take.</summary>
    public int Room => Capacity - Reserve;
}

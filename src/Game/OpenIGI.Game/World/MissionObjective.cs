using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One row in the active map-computer objective definition.</summary>
public sealed record MissionObjective(
    string TextResource,
    int LinkTaskId,
    Vector3d? Position,
    MissionObjectiveState State)
{
    /// <summary>Gets the localized text resolved when this snapshot was produced.</summary>
    /// <remarks>
    /// A renderer should consume this rather than owning another language-table lookup.  The resource key
    /// remains available for the retail map computer and save/debug tooling that addresses it directly.
    /// </remarks>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the row's place among its definition's six, nought first, which the computer numbers it by.</summary>
    /// <remarks>An empty row is not listed, so the place need not be the row's index in the list.</remarks>
    public int Slot { get; init; }

    /// <summary>Gets where in the level it is still to be done: each target not done yet, or its link's place alone.</summary>
    /// <remarks>Not retail; the remastered display marks each. Empty for a row that is done, failed or has no place.</remarks>
    public IReadOnlyList<Vector3d> Places { get; init; } = [];

    /// <summary>Gets the places a row is still to be done at, as <see cref="Places"/> holds them.</summary>
    /// <param name="row">The row.</param>
    /// <param name="link">Where its link stands, or <see langword="null"/> for none.</param>
    /// <param name="placeOf">Where a target task stands, or <see langword="null"/> where it stands nowhere.</param>
    /// <returns>Each open target's place; the link's for a row done by no targets; nothing once the row is settled.</returns>
    public static IReadOnlyList<Vector3d> PlacesOf(
        Flow.ComputerObjective row,
        Vector3d? link,
        Func<Engine.Tasks.GameTask, Vector3d?> placeOf)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(placeOf);
        if (row.IsComplete || row.IsFailed)
        {
            return [];
        }

        if (!row.HasTargets)
        {
            return link is { } linked ? [linked] : [];
        }

        List<Vector3d> places = [];
        foreach (Engine.Tasks.GameTask target in row.OpenTargets)
        {
            if (placeOf(target) is { } place)
            {
                places.Add(place);
            }
        }

        return places;
    }
}

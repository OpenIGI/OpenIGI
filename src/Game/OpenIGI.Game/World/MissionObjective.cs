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
}

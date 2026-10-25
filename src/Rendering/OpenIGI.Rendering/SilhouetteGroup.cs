namespace OpenIGI.Rendering;

/// <summary>Which group a silhouette-marked body is highlighted as: each has a look of its own, and a body overlapping another of its group merges into one silhouette with it.</summary>
/// <remarks>Laid lowest first: where groups overlap on screen the hostile lies on top.</remarks>
public enum SilhouetteGroup
{
    /// <summary>A hostile: a red outline over a faint dark-red fill, seen as plainly at the edge of the view as at its centre.</summary>
    Hostile,

    /// <summary>One of the player's side, or anyone not hostile: a green outline over a faint green fill.</summary>
    Team,

    /// <summary>Out of the fight -- down, or out of reach: a blue outline over a faint blue fill.</summary>
    Disabled,
}

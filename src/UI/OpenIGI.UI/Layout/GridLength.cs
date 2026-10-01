namespace OpenIGI.UI.Layout;

/// <summary>How a grid track (a row or a column) is sized.</summary>
public enum GridUnit
{
    /// <summary>A fixed size in design units.</summary>
    Absolute,

    /// <summary>Sized to the largest child that sits in the track.</summary>
    Auto,

    /// <summary>A share of the space left after the absolute and auto tracks, in proportion to the weight.</summary>
    Star,
}

/// <summary>
/// The size of one grid track: a fixed length, sized-to-content, or a weighted share of the remainder.
/// </summary>
/// <param name="Unit">How the track is sized.</param>
/// <param name="Value">The design-unit length for <see cref="GridUnit.Absolute"/>, or the weight for <see cref="GridUnit.Star"/>.</param>
public readonly record struct GridLength(GridUnit Unit, float Value)
{
    /// <summary>Gets a track sized to the largest child in it.</summary>
    public static GridLength Auto => new(GridUnit.Auto, 0f);

    /// <summary>Gets a single-weight star track.</summary>
    public static GridLength Star => new(GridUnit.Star, 1f);

    /// <summary>Creates a fixed-size track.</summary>
    /// <param name="designUnits">The length in design units.</param>
    /// <returns>An absolute track.</returns>
    public static GridLength Absolute(float designUnits) => new(GridUnit.Absolute, designUnits);

    /// <summary>Creates a weighted star track.</summary>
    /// <param name="weight">The share weight.</param>
    /// <returns>A star track.</returns>
    public static GridLength Stars(float weight) => new(GridUnit.Star, weight);
}

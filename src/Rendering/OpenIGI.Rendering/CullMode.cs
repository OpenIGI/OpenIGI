namespace OpenIGI.Rendering;

/// <summary>Which triangle facings are discarded.</summary>
/// <remarks>
/// The original culls with a signed 2D cross product of the projected vertices and flips the sign per
/// triangle inside a strip (<c>sub_512C30</c>), which is the same rule a hardware winding test applies
/// once a strip is expanded into independent triangles — as OpenIGI expands it.
/// </remarks>
public enum CullMode
{
    /// <summary>Draw both facings.</summary>
    None,

    /// <summary>Discard triangles wound clockwise on screen.</summary>
    Clockwise,

    /// <summary>Discard triangles wound counter-clockwise on screen.</summary>
    CounterClockwise,
}

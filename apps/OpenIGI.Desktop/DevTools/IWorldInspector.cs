using OpenIGI.Core.Numerics;

namespace OpenIGI.Desktop;

/// <summary>One thing in the world the inspector can list and act on.</summary>
/// <param name="Id">A stable handle for this session, as the console's commands name it.</param>
/// <param name="Kind">What sort of thing it is — <c>soldier</c>, <c>player</c>.</param>
/// <param name="Name">The level's own name for it.</param>
/// <param name="Position">Where it is, in world units.</param>
/// <param name="State">A short description of what it is doing.</param>
public readonly record struct InspectableEntity(
    int Id,
    string Kind,
    string Name,
    Vector3d Position,
    string State);

/// <summary>
/// The seam between the developer tools and the world they inspect.
/// </summary>
/// <remarks>
/// <para>
/// Declared here, in the application, and implemented by the thing that owns the world. That keeps
/// gameplay types out of <c>OpenIGI.Core</c> and <c>OpenIGI.UI</c> entirely: the inspector panel knows
/// only this interface, and a console running with no world attached simply has nothing to list rather
/// than a reference it cannot satisfy.
/// </para>
/// <para>
/// Everything is a snapshot. The panel asks once per frame and draws what it was given; it never holds
/// a reference to a soldier that a mission change could invalidate underneath it.
/// </para>
/// </remarks>
public interface IWorldInspector
{
    /// <summary>Lists what is in the world, optionally narrowed by a name fragment.</summary>
    /// <param name="filter">A fragment to match against names and kinds, or empty for everything.</param>
    /// <param name="into">The list to fill, cleared first.</param>
    void Describe(string filter, List<InspectableEntity> into);

    /// <summary>Reports what is known about one entity, a line at a time.</summary>
    /// <param name="id">The entity's handle.</param>
    /// <param name="into">The list to fill, cleared first.</param>
    void Detail(int id, List<string> into);

    /// <summary>Moves the player to an entity.</summary>
    /// <param name="id">The entity's handle.</param>
    /// <returns><see langword="true"/> when the player was moved.</returns>
    bool TryTeleportPlayerTo(int id);

    /// <summary>Moves the free camera to look at an entity.</summary>
    /// <param name="id">The entity's handle.</param>
    /// <returns><see langword="true"/> when the camera was moved.</returns>
    bool TryMoveCameraTo(int id);

    /// <summary>Projects an entity to where it appears on screen.</summary>
    /// <param name="id">The entity's handle.</param>
    /// <param name="x">Receives the horizontal position in back-buffer pixels.</param>
    /// <param name="y">Receives the vertical position in back-buffer pixels.</param>
    /// <returns><see langword="true"/> when the entity is in front of the camera and on screen.</returns>
    /// <remarks>
    /// A screen-space bracket is the only highlight available: there is no debug-line renderer and the
    /// 2D painter has no line primitive, so a real wireframe box would be a new render path rather
    /// than a use of an existing one.
    /// </remarks>
    bool TryProject(int id, out float x, out float y);
}

using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Host.DevTools;

/// <summary>Where a selected entity lands on screen.</summary>
/// <param name="X">The horizontal position in back-buffer pixels; may fall outside the viewport.</param>
/// <param name="Y">The vertical position in back-buffer pixels; may fall outside the viewport.</param>
/// <param name="DistanceMetres">How far the entity is from the eye.</param>
/// <param name="IsInFront">Whether it is in front of the camera at all.</param>
public readonly record struct ScreenMarker(float X, float Y, double DistanceMetres, bool IsInFront);

/// <summary>One thing in the world the inspector can list and act on.</summary>
/// <param name="Id">
/// A handle that survives the list being rebuilt under a different filter, so a selection and the
/// detail shown for it cannot come apart.
/// </param>
/// <param name="Kind">What sort of thing it is — <c>object</c>, <c>door</c>, <c>soldier</c>, <c>terrain</c>.</param>
/// <param name="Name">The level's own name for it.</param>
/// <param name="Position">Where it is, in metres.</param>
/// <param name="State">A short description of what it is doing right now.</param>
/// <param name="DistanceMetres">How far it is from the viewpoint, which is how a reader finds it.</param>
public readonly record struct InspectableEntity(
    int Id,
    string Kind,
    string Name,
    Vector3d Position,
    string State,
    double DistanceMetres = 0.0);

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
    /// <param name="marker">Receives where it lands and how far away it is.</param>
    /// <returns><see langword="true"/> when the entity exists and could be projected.</returns>
    /// <remarks>
    /// <para>
    /// Screen space rather than a world-space wireframe, and deliberately so. There is no debug-line
    /// renderer and the 2D painter has no line primitive, so a real box would be a new render path;
    /// but more importantly a marker drawn in the interface pass is <em>over</em> the scene, which is
    /// exactly what "visible through a wall" means. A world-space box would need a depth-test toggle
    /// to achieve the same thing.
    /// </para>
    /// <para>
    /// The point may land outside the viewport, or behind the camera entirely. Both are reported
    /// rather than refused, so the caller can pin the marker to the nearest edge and point at it —
    /// which is what keeps a selection findable once the reader has turned away from it.
    /// </para>
    /// </remarks>
    bool TryProject(int id, out ScreenMarker marker);
}

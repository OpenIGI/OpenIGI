using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.World;

/// <summary>
/// Says which placements a title's own view culling leaves out of the frame, and whether the world outside its buildings is
/// drawn at all.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="IPlacementDestruction"/> for whether a placement is drawn rather than what it is drawn as. A title
/// whose engine decides visibility by more than the frustum -- I.G.I. 2's sector portals, which leave everything outside a
/// building undrawn while the camera stands in a room with no opening in view -- computes that once a frame from the eye, and
/// every pass of the frame then asks it, so a placement left out of the picture casts no shadow and meets no ray either.
/// </para>
/// <para>
/// A placement the title has no opinion on is visible: the answer narrows what the renderer's own culling keeps, never widens it.
/// </para>
/// </remarks>
public interface IPlacementVisibility
{
    /// <summary>
    /// Gets a value indicating whether the world outside the title's enclosed spaces -- its terrain and its forests -- is drawn
    /// this frame.
    /// </summary>
    bool IsOutsideVisible { get; }

    /// <summary>Decides the frame's visibility from the eye, before any pass draws.</summary>
    /// <param name="eye">The frame's eye, in metres, with the camera's convention: rows are the view's right, up and forward axes.</param>
    /// <param name="tangentHalfFovX">The tangent of half the view's horizontal angle.</param>
    /// <param name="tangentHalfFovY">The tangent of half the view's vertical angle.</param>
    void BeginView(in Camera3D eye, float tangentHalfFovX, float tangentHalfFovY);

    /// <summary>Gets a value indicating whether a placement is drawn this frame.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <returns><see langword="false"/> when the title's culling leaves it out.</returns>
    bool IsVisible(WorldPlacement placement);

    /// <summary>Gets the part of the view a placement is drawn through this frame, or <see langword="null"/> for all of it.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <returns>The window it is clipped to, for a placement seen only through one.</returns>
    /// <remarks>
    /// Project I.G.I. clips everything beyond the inside sphere of the building the camera stands in to the outside's window, by
    /// narrowing the display area its triangles are clipped against. Applied only in passes drawn from the eye.
    /// </remarks>
    ViewClip? ClipOf(WorldPlacement placement) => null;

    /// <summary>
    /// Gets the part of the view the world outside the buildings -- the terrain and the forests -- is drawn through this frame, or
    /// <see langword="null"/> for all of it.
    /// </summary>
    /// <remarks>I.G.I. 2 clips its terrain and forests to the frustum enclosing the outside's windows.</remarks>
    ViewClip? OutsideClip => null;

    /// <summary>Describes what the last frame was decided by -- the sector the eye stood in, and whether it saw out -- for diagnostics.</summary>
    /// <returns>One line, the developer telemetry's.</returns>
    string DescribeView();
}

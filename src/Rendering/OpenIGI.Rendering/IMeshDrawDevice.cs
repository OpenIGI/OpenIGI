using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>Drawing uploaded meshes into the frame: a range, several ranges, and a selection outline.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>. The meshes themselves are <see cref="IMeshDevice"/>'s.</remarks>
public interface IMeshDrawDevice
{
    /// <summary>Draws part of a mesh.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="indexOffset">The first index to draw.</param>
    /// <param name="indexCount">How many indices to draw; a multiple of three.</param>
    /// <param name="modelView">The model-to-view transform, from <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>.</param>
    /// <param name="diffuse">The diffuse texture, or <see cref="TextureHandle.None"/> for untextured geometry.</param>
    /// <param name="lightmap">The lightmap texture, or <see cref="TextureHandle.None"/> to skip the lightmap term.</param>
    /// <param name="blend">How fragments combine with the render target.</param>
    /// <exception cref="ArgumentException">The handle is unknown, or the range lies outside the mesh.</exception>
    /// <remarks>
    /// One call per sub-mesh: a model's draw records each name their own texture, so a model with four
    /// records is four calls into the same mesh at different index ranges.
    /// </remarks>
    void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend);

    /// <summary>Draws several ranges from one mesh with shared transform, texture and state.</summary>
    /// <remarks>
    /// Backends that support multi-draw submit the ranges together. Keeping this on the graphics seam
    /// lets terrain batch visible chunks without exposing OpenGL buffers or commands to the game.
    /// </remarks>
    void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend);

    /// <summary>Draws a glowing selection outline around a mesh.</summary>
    /// <param name="mesh">The mesh to outline.</param>
    /// <param name="ranges">
    /// Every index range making up the model. All of them at once, not one call each: the interior is
    /// masked out before the glow is drawn, and a model outlined a piece at a time would show each
    /// later piece's glow lying across the pieces before it.
    /// </param>
    /// <param name="modelView">The model-to-view transform the mesh itself would be drawn with.</param>
    /// <param name="colour">The glow's colour; its alpha scales the whole effect.</param>
    /// <param name="radius">
    /// How far the glow reaches past the silhouette, <b>in pixels</b> — not in world units, so a
    /// distant object keeps a mark of the same visible size instead of collapsing into a hairline.
    /// </param>
    /// <param name="throughWalls">
    /// Whether the glow ignores the depth buffer. Set, it marks something the reader cannot otherwise
    /// see; clear, it is occluded by the world like any other geometry.
    /// </param>
    /// <exception cref="ArgumentException">The handle is unknown, or a range lies outside the mesh.</exception>
    /// <remarks>
    /// <para>
    /// An inverted hull expanded in screen space, drawn as a stack of shells that fade outwards, with
    /// the model's own silhouette masked out so only the rim survives. It costs a handful of extra
    /// draws of geometry the caller already has, and generates nothing.
    /// </para>
    /// <para>
    /// Colour is never written where the model is, and depth is never written except by the mask, so
    /// the effect cannot occlude what is drawn after it. The mask does leave the near plane written
    /// across the model's silhouette, which is why this belongs late in a frame rather than in the
    /// middle of the world pass.
    /// </para>
    /// <para>
    /// A posed mesh is outlined in the pose currently set through
    /// <see cref="IDrawStateDevice.SetPoseInterpolation"/>, so an animating character's glow tracks its limbs.
    /// </para>
    /// </remarks>
    void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls);

    /// <summary>Marks a mesh for this frame's silhouette highlight: a translucent fill inside a soft outline, seen through walls.</summary>
    /// <param name="mesh">The mesh, as created.</param>
    /// <param name="ranges">The index ranges that make up the model, so its silhouette is the whole model's.</param>
    /// <param name="modelView">The same transform the model was drawn with.</param>
    /// <param name="group">Which group it is highlighted as.</param>
    /// <exception cref="ArgumentException">The handle is unknown, or a range lies outside the mesh.</exception>
    /// <remarks>
    /// Noted when called and drawn once the picture is finished, over it and under the interface: every body marked in a
    /// frame goes into a mask of its group with no depth test, and one pass over the screen then finds each group's
    /// silhouette edges in it and lays the group's fill and outline over the picture. A posed mesh is marked in the pose
    /// currently set through <see cref="IDrawStateDevice.SetPoseInterpolation"/>.
    /// </remarks>
    void DrawSilhouette(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        SilhouetteGroup group);
}

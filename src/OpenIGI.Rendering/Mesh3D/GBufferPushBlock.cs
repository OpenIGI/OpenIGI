using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Mesh3D;

/// <summary>Everything a geometry draw needs to know about the frame it is in.</summary>
/// <param name="Projection">The projection the frame is drawn with.</param>
/// <param name="ViewToWorld">The view-to-world rotation, which makes the pushed normal matrix world-space.</param>
/// <param name="Material">What the surface is made of.</param>
/// <param name="LightmapScale">What a baked lightmap's values are multiplied by.</param>
/// <param name="PoseBlend">How much of the previous pose is in the position drawn.</param>
/// <param name="AlphaHashSeed">The seed a hashed alpha cut dithers against.</param>
/// <param name="DetailFade">The terrain's level-of-detail slide.</param>
/// <param name="TerrainCameraOffset">Where a terrain page sits relative to the camera, for that slide.</param>
/// <param name="GroundDetail">Whether the lightmap slot holds the ground's detail map.</param>
/// <param name="PaintedSky">Whether this draw is a sky the title painted rather than a surface.</param>
/// <param name="Exposure">What the resolve will multiply by, which a painted sky is divided by first.</param>
public readonly record struct GBufferDrawState(
    Matrix4f Projection,
    Matrix3f ViewToWorld,
    SurfaceMaterial Material,
    float LightmapScale = 1f,
    float PoseBlend = 0f,
    float AlphaHashSeed = 0f,
    DetailFade DetailFade = default,
    Vector3f TerrainCameraOffset = default,
    bool GroundDetail = false,
    bool PaintedSky = false,
    float Exposure = 1f);

/// <summary>
/// The 128 bytes a geometry draw pushes: where it is, which way it faces, and what it is made of.
/// </summary>
/// <remarks>
/// <para>
/// The layout: the model-view-projection in the first sixteen, then three rows that are read two
/// different ways, then the material. The rows are normally the object-to-world rotation the G-buffer's
/// world-space normals need — the model-view's own rotation followed by the view-to-world one, composed
/// here so the shader needs one matrix rather than two.
/// </para>
/// <para>
/// A terrain page puts something else there. It slides each vertex along the offset in its normal
/// channel as the ground changes level of detail, and that needs three numbers for the ramp and three
/// for where the page sits relative to the camera — and the block is full. It has room all the same,
/// because a page is drawn with no rotation of its own: its normal matrix is the identity, and nine
/// floats of identity carry nothing. The fourth component of the first row, nought for everything else,
/// is what says which reading applies. The identity is CHECKED rather than assumed, so a page that ever
/// came in rotated keeps its matrix and simply does not morph.
/// </para>
/// </remarks>
public static class GBufferPushBlock
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 32;

    /// <summary>The mode that says the lightmap slot holds the ground's detail map.</summary>
    public const float GroundDetailMode = 3f;

    /// <summary>The mode that says this draw is the ground's bake, for the baked attachment.</summary>
    public const float GroundBakeMode = 4f;

    /// <summary>Writes the block for one geometry draw.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="state">What the frame holds.</param>
    /// <param name="modelView">The draw's transform into view space.</param>
    /// <param name="blend">How it is blended, which is what names the ground's bake.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(
        Span<float> block,
        in GBufferDrawState state,
        in Matrix4f modelView,
        BlendMode blend)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The G-buffer push block is exactly {Floats} floats.", nameof(block));
        }

        block.Clear();

        Matrix4f mvp = state.Projection * modelView;
        mvp.CopyColumnMajorTo(block[..16]);

        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);

        Matrix3f normalMatrix = state.ViewToWorld * modelViewRotation;

        if (state.DetailFade.Active && IsIdentity(normalMatrix))
        {
            block[16] = state.DetailFade.Scale;
            block[17] = state.DetailFade.Offset;
            block[18] = state.DetailFade.Slope;
            block[19] = state.DetailFade.Mode;

            block[20] = state.TerrainCameraOffset.X;
            block[21] = state.TerrainCameraOffset.Y;
            block[22] = state.TerrainCameraOffset.Z;

            block[27] = state.AlphaHashSeed;
        }
        else
        {
            block[16] = normalMatrix.M11;
            block[17] = normalMatrix.M21;
            block[18] = normalMatrix.M31;

            // Three says the lightmap slot is the ground's DETAIL map; four says this draw is the
            // ground's BAKE and belongs in the baked attachment rather than in the colour. Nothing but
            // I.G.I. 2's ground asks for either.
            block[19] = blend == BlendMode.Modulate2X
                ? GroundBakeMode
                : state.GroundDetail ? GroundDetailMode : 0f;

            block[20] = normalMatrix.M12;
            block[21] = normalMatrix.M22;
            block[22] = normalMatrix.M32;

            block[24] = normalMatrix.M13;
            block[25] = normalMatrix.M23;
            block[26] = normalMatrix.M33;
            block[27] = state.AlphaHashSeed;
        }

        // The second normal row's fourth component is spare whichever branch wrote it: a terrain page
        // puts its offset in the first three and a rotated mesh a matrix column, and neither has a use
        // for the fourth.
        block[23] = state.PoseBlend;

        // The pane's roughness is NOT the frame's, and this is where the two part company. A glass
        // texture holds both, and everything of it that reaches the G-buffer is by definition the opaque
        // remainder — the mullions, the surround, the putty — because the pane's own texels fail the
        // alpha cut and are drawn by the glazing pass instead. A window's authored 0.08 describes the
        // glass; applied to what is left here it makes painted metal read as polished chrome.
        block[28] = state.Material.Glass ? SurfaceMaterial.Default.Roughness : state.Material.Roughness;
        block[29] = state.Material.Metallic;

        // A painted sky is written as emission — it is not lit, it IS light — but what it carries is the
        // colour the artist wanted on the screen rather than a radiance. Divided here by what the resolve
        // will multiply back, so it comes out of the tone map at the colour it was painted.
        block[30] = state.PaintedSky && state.Material.Emissive > 0f && state.Exposure > 0f
            ? state.Material.Emissive / state.Exposure
            : state.Material.Emissive;

        block[31] = state.LightmapScale;
    }

    /// <summary>Where a terrain page's origin sits relative to the camera, in world axes.</summary>
    /// <param name="modelView">The page's object-to-view transform.</param>
    /// <param name="viewToWorld">The rotation that undoes that view's, whichever view it is.</param>
    /// <returns>The page origin minus the camera position.</returns>
    /// <remarks>
    /// The model-view carries the camera subtraction already, folded in while it was still in double
    /// precision. Undoing its rotation recovers the offset in world axes, which is the frame the page's
    /// own vertices are in.
    /// </remarks>
    public static Vector3f TerrainCameraOffset(in Matrix4f modelView, in Matrix3f viewToWorld)
    {
        Matrix3f m = viewToWorld;

        return new Vector3f(
            (m.M11 * modelView.M14) + (m.M12 * modelView.M24) + (m.M13 * modelView.M34),
            (m.M21 * modelView.M14) + (m.M22 * modelView.M24) + (m.M23 * modelView.M34),
            (m.M31 * modelView.M14) + (m.M32 * modelView.M24) + (m.M33 * modelView.M34));
    }

    /// <summary>Whether a rotation is the identity, which is what says a page may morph.</summary>
    /// <param name="matrix">The composed normal matrix.</param>
    /// <returns><see langword="true"/> when it carries no rotation at all.</returns>
    /// <remarks>
    /// Loose rather than exact: the matrix is a product of two rotations that undo each other, so it
    /// arrives with the rounding of nine multiplies on it and an exact comparison would refuse every
    /// page.
    /// </remarks>
    public static bool IsIdentity(in Matrix3f matrix)
    {
        const float Tolerance = 1e-4f;

        return MathF.Abs(matrix.M11 - 1f) < Tolerance
            && MathF.Abs(matrix.M22 - 1f) < Tolerance
            && MathF.Abs(matrix.M33 - 1f) < Tolerance
            && MathF.Abs(matrix.M12) < Tolerance
            && MathF.Abs(matrix.M13) < Tolerance
            && MathF.Abs(matrix.M21) < Tolerance
            && MathF.Abs(matrix.M23) < Tolerance
            && MathF.Abs(matrix.M31) < Tolerance
            && MathF.Abs(matrix.M32) < Tolerance;
    }
}

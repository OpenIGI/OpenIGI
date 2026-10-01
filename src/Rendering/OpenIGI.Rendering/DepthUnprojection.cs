using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// What a full-screen pass needs to rebuild a view-space position from the depth buffer.
/// </summary>
/// <remarks>
/// <para>
/// Every effect the remaster track adds after the G-buffer — shadows, ambient occlusion, reflections,
/// volumetric fog — starts by asking "where in the world is this pixel". The projection's last row is
/// <c>(0, 0, 1, 0)</c>, so <c>w</c> is the view depth and the inverse has a closed form; passing these
/// four numbers is cheaper and steadier than inverting a matrix per frame and uploading it.
/// </para>
/// <para>
/// Build it from the same projection the scene was drawn with. Taking the scales from anywhere else —
/// a remembered field of view, a window size — is how a reconstruction ends up subtly stretched.
/// </para>
/// </remarks>
/// <param name="XScale">The projection's horizontal scale, its <c>M11</c>.</param>
/// <param name="YScale">The projection's vertical scale, its <c>M22</c>.</param>
/// <param name="Near">The near plane distance.</param>
/// <param name="Far">The far plane distance.</param>
public readonly record struct DepthUnprojection(float XScale, float YScale, float Near, float Far)
{
    /// <summary>Reads the four values out of a projection matrix.</summary>
    /// <param name="projection">The projection the scene was drawn with.</param>
    /// <param name="near">The near plane it was built with.</param>
    /// <param name="far">The far plane it was built with.</param>
    /// <returns>The unprojection.</returns>
    public static DepthUnprojection From(in Matrix4f projection, float near, float far) =>
        new(projection.M11, projection.M22, near, far);
}

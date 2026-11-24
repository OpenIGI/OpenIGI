using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// Whether a soldier is in a view: the sector cull's gate for a body drawn outside the scene, and the frustum sphere
/// <c>Human_DrawHandler</c> (<c>0x45F280</c>) puts it through. One test, for the soldiers' drawer and for the simulation's own
/// look at where the original's camera stood (<see cref="SoldierSimulation.ObserveFrom"/>).
/// </summary>
public static class SoldierViewBounds
{
    /// <summary>The soldier model's bounding radius the draw callback's frustum test scales by 1.41, in metres.</summary>
    /// <remarks>
    /// An approximation: retail reads it from the model's LOD record (<c>+60</c>), which is not parsed yet, and the skinned
    /// geometry's own bounds are not in posed model space. A metre about the model origin -- the pelvis, 0.8 m under the eye
    /// bone -- covers a standing body, and only decides guards at the screen edge.
    /// </remarks>
    public const float SkinCullRadiusMetres = 1.0f;

    /// <summary>How far from the camera a drawn soldier's eye bone is still refreshed, in metres.</summary>
    /// <remarks>
    /// Measured on the retail game: past about 51 m the soldier is drawn at its fifth LOD (0x4CFF70 returns 4; 50.6 m gave 3 and
    /// 51.9 m gave 4), and that LOD's skinning leaves the eye bone's palette entry as it was -- a guard skinned at 65 m kept its
    /// rest-pose eye, one at 50 m followed its body.
    /// </remarks>
    public const double EyeRefreshDistanceMetres = 51.0;

    // The nearest a view sees, in metres: well inside the frustum sphere, so it decides only a body behind the eye.
    private const double NearMetres = 0.1;

    /// <summary>Whether the sector cull reaches a soldier's body.</summary>
    /// <param name="cull">The level's sector cull, decided for the view.</param>
    /// <param name="body">The soldier's model.</param>
    /// <param name="originUnits">Where its origin is, in world units.</param>
    /// <param name="orientation">How its body is turned.</param>
    /// <returns><see langword="false"/> when the cull leaves it out.</returns>
    /// <remarks>
    /// The soldier's own position is what the inside sphere is measured to; its exclusion sphere is its mesh's render sphere,
    /// moved by the root bone and turned by the body, as a bone object is excluded.
    /// </remarks>
    public static bool IsInSector(Igi1SectorVisibility cull, SoldierBodyModel body, Vector3d originUnits, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(cull);
        ArgumentNullException.ThrowIfNull(body);
        if (body.Model is not { } model)
        {
            return cull.IsObjectVisible(originUnits, originUnits, 0f);
        }

        Vector3f root = model.BoneHierarchy is { BoneCount: > 0 } rig ? rig.Translations[0] : Vector3f.Zero;
        Vector3f turned = orientation.Transposed().Transform(model.RenderSphereCentre + root);
        return cull.IsObjectVisible(
            originUnits,
            new Vector3d(originUnits.X + turned.X, originUnits.Y + turned.Y, originUnits.Z + turned.Z),
            model.RenderSphereRadius);
    }

    /// <summary>Whether the frustum sphere about a soldier's origin is in a symmetric view.</summary>
    /// <param name="eye">The view, in metres: rows its right, up and forward.</param>
    /// <param name="originMetres">The soldier's origin, in metres.</param>
    /// <param name="tangentHalfFovX">The tangent of half the view's horizontal angle.</param>
    /// <param name="tangentHalfFovY">The tangent of half its vertical angle.</param>
    /// <returns>Whether any of the sphere is inside.</returns>
    /// <remarks>Without a far plane: the original's reaches past the fog, and the fog is how the level looks, not how it plays.</remarks>
    public static bool IsInFrustum(in Camera3D eye, Vector3d originMetres, float tangentHalfFovX, float tangentHalfFovY)
    {
        double radius = SkinCullRadiusMetres * 1.41;
        Vector3d offset = new(originMetres.X - eye.Position.X, originMetres.Y - eye.Position.Y, originMetres.Z - eye.Position.Z);
        Vector3d view = eye.Orientation.Transposed().Transform(offset);
        if (view.Z + radius < NearMetres)
        {
            return false;
        }

        double edgeX = radius * Math.Sqrt(1 + ((double)tangentHalfFovX * tangentHalfFovX));
        double edgeY = radius * Math.Sqrt(1 + ((double)tangentHalfFovY * tangentHalfFovY));
        return Math.Abs(view.X) <= (view.Z * tangentHalfFovX) + edgeX && Math.Abs(view.Y) <= (view.Z * tangentHalfFovY) + edgeY;
    }
}

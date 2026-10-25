using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using Member = OpenIGI.Presentation.Igi1.SoldierMember;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Puts a body's pose on its own mesh: skinned into its scratch vertices, then uploaded over the mesh's current stream.</summary>
internal static class SoldierSkinning
{
    /// <summary>Skins a pose into a body's scratch vertices and uploads them to its mesh.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="member">The body.</param>
    /// <param name="geometry">The body's group geometry, which the scratch starts as.</param>
    /// <param name="model">The model the pose is skinned through.</param>
    /// <param name="pose">The pose.</param>
    /// <returns><see langword="false"/> when the body has no mesh, or the pose does not skin or upload.</returns>
    /// <remarks>
    /// The upload moves the mesh's current stream into its previous one before writing this pose, which is the pair the
    /// motion vectors and the between-tick blending read.
    /// </remarks>
    public static bool Upload(IGraphicsDevice device, Member member, MeshGeometry geometry, MefModel model, BonePose pose)
    {
        if (!member.Mesh.IsValid)
        {
            return false;
        }

        Vertex3D[]? skinned = member.SkinScratch;
        if (skinned is null)
        {
            skinned = new Vertex3D[geometry.Vertices.Length];
            geometry.Vertices.CopyTo(skinned, 0);
            member.SkinScratch = skinned;
        }

        try
        {
            MefSkinner.Skin(model, pose, (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
            device.UpdateAnimatedMesh(member.Mesh, skinned);
        }
        catch (ArgumentException)
        {
            return false;
        }

        member.PoseFlippedSinceFrame = true;
        return true;
    }
}

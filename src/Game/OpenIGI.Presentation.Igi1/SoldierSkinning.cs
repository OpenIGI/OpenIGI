using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Physics;
using Group = OpenIGI.Presentation.Igi1.SoldierRenderer.Group;
using HeldWeapon = OpenIGI.Presentation.Igi1.SoldierRenderer.HeldWeapon;
using Member = OpenIGI.Presentation.Igi1.SoldierRenderer.Member;

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
    public static bool Upload(IGraphicsDevice device, SoldierRenderer.Member member, MeshGeometry geometry, MefModel model, BonePose pose)
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

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

    /// <summary>Takes a tick's pose for the body's mesh without skinning it yet.</summary>
    /// <param name="member">The body.</param>
    /// <param name="model">The model the pose will be skinned through.</param>
    /// <param name="pose">The pose.</param>
    /// <returns><see langword="false"/> when the body has no mesh or the model cannot be skinned, as <see cref="Upload"/> would have.</returns>
    /// <remarks>
    /// <para>
    /// Every soldier in the level was skinned and uploaded on every tick, whether or not anything drew
    /// it. Skinning is the whole rig through every vertex, and in a level of forty guards only a handful
    /// are ever on screen: on the web that was a tenth of the frame. The pose is now queued here and put
    /// on the mesh by <see cref="Flush"/> the first time a pass is about to draw the body.
    /// </para>
    /// <para>
    /// What is drawn is unchanged. The mesh holds two streams and each upload pushes the current one
    /// into the previous one, so only the last two poses a tick gave it can ever be on it when it is
    /// drawn -- exactly the two kept here, uploaded in the order they came. The bookkeeping around them
    /// (the pose pair hit tests read, the stream flip the interpolation reads) stays on the tick.
    /// </para>
    /// </remarks>
    public static bool Defer(Member member, MefModel model, BonePose pose)
    {
        if (!member.Mesh.IsValid || !MefSkinner.IsSkinned(model))
        {
            return false;
        }

        BonePose?[] queue = member.DeferredPoses;
        if (member.DeferredPoseCount == queue.Length)
        {
            queue[0] = queue[1];
            queue[1] = pose;
        }
        else
        {
            queue[member.DeferredPoseCount++] = pose;
        }

        member.PoseFlippedSinceFrame = true;
        return true;
    }

    /// <summary>Puts the poses <see cref="Defer"/> queued on the body's mesh, oldest first.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="member">The body.</param>
    /// <param name="geometry">The body's group geometry.</param>
    /// <param name="model">The model the poses are skinned through.</param>
    /// <remarks>Called before any pass draws the body's own mesh; nothing waiting costs nothing.</remarks>
    public static void Flush(IGraphicsDevice device, Member member, MeshGeometry geometry, MefModel? model)
    {
        int waiting = member.DeferredPoseCount;
        if (waiting == 0)
        {
            return;
        }

        member.DeferredPoseCount = 0;
        bool flipped = member.PoseFlippedSinceFrame;
        for (int i = 0; i < waiting; i++)
        {
            BonePose pose = member.DeferredPoses[i]!;
            member.DeferredPoses[i] = null;
            if (model is not null)
            {
                Upload(device, member, geometry, model, pose);
            }
        }

        // The flip belongs to the tick that queued the pose and has already been read or cleared by the
        // frame's own bookkeeping; uploading late must not report it a second time.
        member.PoseFlippedSinceFrame = flipped;
    }
}

using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// A soldier drawn at a lower level of detail as its body gets further away, Project I.G.I.'s bone mesh draw (<c>0x49F700</c>):
/// the level is picked for each view (<c>0x4CFF70</c>) and that level's mesh is skinned from the body's pose.
/// </summary>
/// <remarks>
/// The full mesh stays skinned whatever is drawn, since the outlines and the shots read it. The lower level is a second mesh posed
/// alongside it each tick, both of its streams from the same pair of poses, so the between-tick blend draws it as it draws the
/// full one.
/// </remarks>
internal static class SoldierLevelOfDetail
{
    /// <summary>Finds a soldier model's lower levels: the instances named after it with the last character counted on.</summary>
    /// <param name="mission">The mission whose packs hold the models.</param>
    /// <param name="modelName">The first level's instance name.</param>
    /// <param name="first">The first level's model.</param>
    /// <param name="triangles">Each level's triangle count, the first level's first.</param>
    /// <returns>The lower levels, the second first.</returns>
    public static IReadOnlyList<SoldierLevel> FindLower(LoadedMission mission, string modelName, MefModel first, out int[] triangles)
    {
        List<SoldierLevel> lower = [];
        List<int> counts = [first.TriangleCount];
        string name = modelName;
        for (int level = 1; level < LodModelChain.MaxLevels && name.Length > 0; level++)
        {
            name = name[..^1] + (char)(name[^1] + 1);
            if (!mission.TryGetModel(name, out MefModel? found) || found is not { } model || !MefSkinner.IsSkinned(model))
            {
                break;
            }

            if (MefMeshBuilder.Build(model, 1f) is not { } geometry)
            {
                break;
            }

            (LoadedPack? pack, MtpInstance? instance) = SoldierMeshBuilder.FindInstance(mission, name);
            lower.Add(new SoldierLevel(model, geometry, instance, pack));
            counts.Add(model.TriangleCount);
        }

        triangles = [.. counts];
        return lower;
    }

    /// <summary>Picks the level a soldier is drawn at from a view.</summary>
    /// <param name="lod">The level's settings.</param>
    /// <param name="group">The soldier's group.</param>
    /// <param name="member">The soldier.</param>
    /// <param name="camera">The view.</param>
    /// <param name="fieldOfView">The view's horizontal half-angle tangent.</param>
    /// <returns>The level, or <c>-1</c> when the soldier is beyond its cutoff and not drawn.</returns>
    public static int Select(ModelLodPolicy lod, SoldierGroup group, SoldierMember member, in Camera3D camera, float fieldOfView) =>
        group.Body.Model is { } first && group.Instance is { } instance
            ? lod.SelectBone(instance.Name, first, group.Triangles, member.DrawnWhere, camera, fieldOfView)
            : 0;

    /// <summary>Keeps a soldier's lower-level mesh posed at the level it was last drawn at, once its tick's pose is settled.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="group">The soldier's group.</param>
    /// <param name="member">The soldier.</param>
    /// <param name="level">The level the soldier is drawn at now.</param>
    /// <param name="posed">Whether the full mesh took a new pose this tick.</param>
    public static void Follow(IGraphicsDevice device, SoldierGroup group, SoldierMember member, int level, bool posed)
    {
        if (level <= 0 || level > group.Lower.Count || member.SkinnedPose is not { } current)
        {
            Drop(device, member);
            return;
        }

        SoldierLevel lower = group.Lower[level - 1];
        try
        {
            if (level != member.Level || !member.LevelMesh.IsValid)
            {
                // A new level starts with both of its streams the pair the full mesh holds.
                Drop(device, member);
                Vertex3D[] scratch = new Vertex3D[lower.Geometry.Vertices.Length];
                lower.Geometry.Vertices.CopyTo(scratch, 0);
                Skin(lower.Model, member.PreviousSkinnedPose ?? current, scratch);
                member.LevelMesh = device.CreateAnimatedMesh(scratch, lower.Geometry.Indices);
                member.LevelScratch = scratch;
                member.Level = level;
                posed = true;
            }

            if (posed && member.LevelScratch is { } skinned)
            {
                Skin(lower.Model, current, skinned);
                device.UpdateAnimatedMesh(member.LevelMesh, skinned);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            Drop(device, member);
        }
    }

    /// <summary>Frees a soldier's lower-level mesh.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="member">The soldier.</param>
    public static void Drop(IGraphicsDevice device, SoldierMember member)
    {
        if (member.LevelMesh.IsValid)
        {
            device.DestroyMesh(member.LevelMesh);
        }

        member.LevelMesh = default;
        member.LevelScratch = null;
        member.Level = 0;
    }

    private static void Skin(MefModel model, BonePose pose, Vertex3D[] into) =>
        MefSkinner.Skin(model, pose, (float)(1.0 / WorldScene.UnitsPerMetre), into);
}

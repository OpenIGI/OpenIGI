using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>What a soldier's model gives its body: its bones' names, its collision levels, each bone's damage zone and the groin's mark.</summary>
public sealed class Igi2SoldierModel
{
    /// <summary>The magic vertex a hit is measured against for the groin, <c>1</c>.</summary>
    public const int GroinMagicVertex = 1;

    /// <summary>
    /// The bones whose branches name the damage zones, in the order <c>HumanAnim_Init</c> (<c>igi2 0x58F650</c>) marks them
    /// (<c>HumanAnim_SetDamagePart</c>, <c>0x58F0A0</c>): each marks its whole branch, a later one over an earlier.
    /// </summary>
    public static IReadOnlyList<(string Bone, Igi2DamagePart Part)> DamageBranches { get; } =
    [
        ("center", Igi2DamagePart.UpperBody),
        ("head", Igi2DamagePart.Head),
        ("upper left arm", Igi2DamagePart.LeftUpperArm),
        ("lower left arm", Igi2DamagePart.LeftLowerArm),
        ("left hand", Igi2DamagePart.LeftHand),
        ("upper right arm", Igi2DamagePart.RightUpperArm),
        ("lower right arm", Igi2DamagePart.RightLowerArm),
        ("right hand", Igi2DamagePart.RightHand),
        ("upper left leg", Igi2DamagePart.LeftUpperLeg),
        ("lower left leg", Igi2DamagePart.LeftLowerLeg),
        ("left foot", Igi2DamagePart.LeftFoot),
        ("upper right leg", Igi2DamagePart.RightUpperLeg),
        ("lower right leg", Igi2DamagePart.RightLowerLeg),
        ("right foot", Igi2DamagePart.RightFoot),
    ];

    private readonly Igi2BoneMesh?[]? _meshes;

    private Igi2SoldierModel(string name, IReadOnlyList<string> boneNames, Igi2BoneMesh?[]? meshes, Igi2DamagePart[] parts, Vector3f? groin, int groinBone)
    {
        Name = name;
        BoneNames = boneNames;
        LowerBodyBone = Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.LowerBodyBone);
        UpperBodyBone = Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.UpperBodyBone);
        ShouldersBone = Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.ShouldersBone);
        HeadBone = Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.HeadBone);
        _meshes = meshes;
        DamageParts = parts;
        Groin = groin;
        GroinBone = groinBone;
    }

    /// <summary>Gets the model's name.</summary>
    public string Name { get; }

    /// <summary>Gets its bones' names, in bone order.</summary>
    public IReadOnlyList<string> BoneNames { get; }

    /// <summary>Gets the <c>lower body</c> bone, the lower back's callback's; <c>-1</c> for none.</summary>
    public int LowerBodyBone { get; }

    /// <summary>Gets the <c>upper body</c> bone, the upper back's callback's.</summary>
    public int UpperBodyBone { get; }

    /// <summary>Gets the <c>shoulders</c> bone.</summary>
    public int ShouldersBone { get; }

    /// <summary>Gets the <c>head</c> bone.</summary>
    public int HeadBone { get; }

    /// <summary>Gets each bone's damage zone, <c>AnimatedInfo_t.anDamagePart</c>; a bone no branch marks is the right foot's, nought.</summary>
    public IReadOnlyList<Igi2DamagePart> DamageParts { get; }

    /// <summary>Gets the groin's mark in its bone's frame, magic vertex 1, or <see langword="null"/> when the model has none.</summary>
    public Vector3f? Groin { get; }

    /// <summary>Gets the bone the groin's mark hangs on.</summary>
    public int GroinBone { get; }

    /// <summary>Gets a collision mesh level.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The level, or <see langword="null"/> for none.</returns>
    public Igi2BoneMesh? Mesh(int level) => _meshes is not null && (uint)level < (uint)_meshes.Length ? _meshes[level] : null;

    /// <summary>Reads what a soldier's body needs from its model.</summary>
    /// <param name="name">The model's name.</param>
    /// <param name="model">The model, or <see langword="null"/> when it is missing.</param>
    /// <returns>The model's data; empty when it is missing.</returns>
    public static Igi2SoldierModel From(string name, MefModel? model)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (model is null)
        {
            return new Igi2SoldierModel(name, [], null, [], null, 0);
        }

        IReadOnlyList<string> names = model.BoneNames;
        Igi2BoneHierarchy? rig = model.BoneHierarchy;
        Igi2DamagePart[] parts = new Igi2DamagePart[rig?.BoneCount ?? 0];
        if (rig is not null)
        {
            foreach ((string bone, Igi2DamagePart part) in DamageBranches)
            {
                bool[] marked = Igi2BoneBranches.Mark(rig, Igi2BoneBranches.IndexOf(names, bone), include: true);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (marked[i])
                    {
                        parts[i] = part;
                    }
                }
            }
        }

        // BoneDynCubeObj_GetMagicVertexLocalPos (igi2 0x5683E0): a mark on a bone past the rig's count is none.
        Vector3f? groin = null;
        int groinBone = 0;
        if (model.TryGetMagicVertex(GroinMagicVertex, out Vector3f mark)
            && model.TryGetMagicVertexBone(GroinMagicVertex, out int markBone)
            && markBone <= parts.Length)
        {
            groin = mark;
            groinBone = markBone;
        }

        return new Igi2SoldierModel(name, names, Igi2BoneMesh.Of(model), parts, groin, groinBone);
    }

    /// <summary>
    /// The groin's mark in the world, <c>BoneDynCubeObj_GetMagicVertexPos</c> (<c>igi2 0x568530</c>): placed by its bone, scaled by
    /// one, turned by the body's orientation rows and added to its position; a model with no mark measures from the position.
    /// </summary>
    /// <param name="pose">The body's bones as they stand.</param>
    /// <param name="orientation">The body's orientation.</param>
    /// <param name="position">The body's position.</param>
    /// <returns>The mark.</returns>
    public Vector3d GroinPosition(BonePose? pose, Matrix3f orientation, Vector3d position)
    {
        float x = 0f, y = 0f, z = 0f;
        if (Groin is { } g && pose is not null && (uint)GroinBone < (uint)pose.BoneCount)
        {
            Matrix3f m = pose.Rotations[GroinBone];
            Vector3f at = pose.Positions[GroinBone];
            x = ((((Fpu)m.M13 * g.Z) + ((Fpu)m.M12 * g.Y)) + ((Fpu)m.M11 * g.X)).F;
            y = ((((Fpu)m.M23 * g.Z) + ((Fpu)m.M22 * g.Y)) + ((Fpu)m.M21 * g.X)).F;
            z = ((((Fpu)m.M33 * g.Z) + ((Fpu)m.M32 * g.Y)) + ((Fpu)m.M31 * g.X)).F;
            x = ((Fpu)at.X + x).F;
            y = ((Fpu)at.Y + y).F;
            z = ((Fpu)at.Z + z).F;
        }

        Fpu wx = (((Fpu)z * orientation.M13) + ((Fpu)y * orientation.M12)) + ((Fpu)x * orientation.M11);
        Fpu wy = (((Fpu)z * orientation.M23) + ((Fpu)y * orientation.M22)) + ((Fpu)x * orientation.M21);
        float wz = ((((Fpu)z * orientation.M33) + ((Fpu)y * orientation.M32)) + ((Fpu)x * orientation.M31)).F;
        return new Vector3d((wx + position.X).Value, (wy + position.Y).Value, ((Fpu)wz + position.Z).Value);
    }
}

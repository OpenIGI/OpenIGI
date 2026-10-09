using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>
/// A body's bones as the animation writes them, <c>Bone_s</c> per bone (<c>BoneDynCubeObj_s.patBone</c>): an orientation with
/// its operation count, a matrix and a translation, and the root translation (<c>tRootTranslation</c>, <c>+0x130</c>).
/// </summary>
/// <remarks>
/// The controllers and blenders write orientations, counts and the root into a buffer; the hierarchy update then turns each
/// orientation into the bone's own in the body's frame, in place, and fills the matrices and translations. A blender's working
/// buffers keep what they last held, as the stack they stand for does from one evaluation to the next.
/// </remarks>
public sealed class Igi2BoneBuffer
{
    /// <summary>Initializes a new instance of the <see cref="Igi2BoneBuffer"/> class, every orientation the identity.</summary>
    /// <param name="boneCount">The number of bones.</param>
    public Igi2BoneBuffer(int boneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(boneCount);
        Orientations = new Quaternion32[boneCount];
        Ops = new int[boneCount];
        Matrices = new Matrix3f[boneCount];
        Translations = new Vector3f[boneCount];
        Array.Fill(Orientations, Quaternion32.Identity);
        Array.Fill(Matrices, Matrix3f.Identity);
    }

    /// <summary>Gets the bone count.</summary>
    public int BoneCount => Orientations.Length;

    /// <summary>Gets each bone's orientation, <c>tOrientation</c> (<c>+0x38</c>).</summary>
    public Quaternion32[] Orientations { get; }

    /// <summary>Gets each orientation's operation count, <c>Quat32_t.nOps</c>: a quaternion is renormalised past a thousand.</summary>
    public int[] Ops { get; }

    /// <summary>Gets each bone's rotation, <c>tMatrix</c> (<c>+0x10</c>): rows as the bone's axes in the body's frame.</summary>
    public Matrix3f[] Matrices { get; }

    /// <summary>Gets each bone's position in the body's frame, <c>tTranslation</c>.</summary>
    public Vector3f[] Translations { get; }

    /// <summary>Gets or sets the root translation the animation writes.</summary>
    public Vector3f Root { get; set; }

    /// <summary>The bones as a pose.</summary>
    /// <param name="parents">Each bone's parent.</param>
    /// <returns>The pose.</returns>
    public BonePose ToPose(IReadOnlyList<int> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);
        int[] p = new int[BoneCount];
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = parents[i];
        }

        return BonePose.FromBones(Matrices, Translations, p);
    }
}

/// <summary>A human body's pose as its clips leave it, and the clip whose attach objects hang on it.</summary>
/// <param name="Pose">The bones as they stand, in the model's frame.</param>
/// <param name="AttachClip">The clip whose attach objects are hung: the upper body's while it plays, else the main one.</param>
/// <param name="AttachTime">That clip's time.</param>
public sealed record Igi2BodyPose(BonePose Pose, Igi2AnimationClip? AttachClip, int AttachTime);

/// <summary>What runs on a bone as the hierarchy reaches it, <c>Bone_s.pfCallback</c>: after its orientation is composed, before its matrix.</summary>
public interface IIgi2BoneCallbacks
{
    /// <summary>Runs a bone's callback, if it has one.</summary>
    /// <param name="bones">The bones.</param>
    /// <param name="bone">The bone.</param>
    /// <param name="parent">Its parent; the root is its own.</param>
    void Run(Igi2BoneBuffer bones, int bone, int parent);
}

/// <summary>
/// The quaternion arithmetic of the bone code, in the FPU's chopped single precision and the order its instructions take.
/// </summary>
public static class Igi2BoneMath
{
    /// <summary>The count past which a quaternion is renormalised, <c>1000</c>.</summary>
    public const int RenormaliseOps = 1000;

    /// <summary>
    /// The product of two orientations, the eight-multiply form <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>igi2 0x564D3B</c>)
    /// composes a bone with its parent by, the first the parent's.
    /// </summary>
    /// <param name="a">The first, the parent's.</param>
    /// <param name="b">The second, the bone's own.</param>
    /// <returns>The product.</returns>
    /// <remarks>The bone callbacks use the same form; their sums take the same operands, so give the same bits.</remarks>
    public static Quaternion32 Multiply(Quaternion32 a, Quaternion32 b)
    {
        Fpu zy = ((Fpu)a.Z - a.Y) * ((Fpu)b.Y - b.Z);
        Fpu yz = ((Fpu)b.Y + b.Z) * ((Fpu)a.W - a.X);
        Fpu wx = ((Fpu)a.Z + a.Y) * ((Fpu)b.W - b.X);
        float yx = (((Fpu)b.Y + b.X) * ((Fpu)a.Z + a.X)).F;
        Fpu wz = ((Fpu)b.W - b.Z) * ((Fpu)a.W + a.Y);
        Fpu wy = ((Fpu)a.W - a.Y) * ((Fpu)b.W + b.Z);
        Fpu sum = (wy + wz) + yx;
        float half = (((((Fpu)a.Z - a.X) * ((Fpu)b.X - b.Y)) + sum) * 0.5f).F;
        return new Quaternion32(
            (((((Fpu)a.X + a.W) * ((Fpu)b.W + b.X)) + half) - sum).F,
            (((Fpu)half + yz) - wy).F,
            (((Fpu)half + wx) - wz).F,
            (((Fpu)half + zy) - yx).F);
    }

    /// <summary>A quaternion scaled to unit length, its squares summed in the order a call site takes them.</summary>
    /// <param name="q">The quaternion.</param>
    /// <param name="wzxy">Whether the squares are summed w, z, x, y; otherwise w, z, y, x.</param>
    /// <returns>The unit quaternion.</returns>
    public static Quaternion32 Normalise(Quaternion32 q, bool wzxy)
    {
        Fpu w2 = (Fpu)q.W * q.W, z2 = (Fpu)q.Z * q.Z, x2 = (Fpu)q.X * q.X, y2 = (Fpu)q.Y * q.Y;
        Fpu sum = wzxy ? ((w2 + z2) + x2) + y2 : ((w2 + z2) + y2) + x2;
        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(sum);
        return new Quaternion32(((Fpu)q.X * inverse).F, ((Fpu)q.Y * inverse).F, ((Fpu)q.Z * inverse).F, ((Fpu)q.W * inverse).F);
    }

    /// <summary>
    /// The bones composed down the hierarchy, <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>igi2 0x564B50</c>), in place: the root
    /// stands at the root translation with its own orientation; every other bone's orientation becomes its parent's times its
    /// own, and its translation the parent's matrix times its rest translation plus the parent's. Each bone's callback runs then,
    /// and its matrix is made from its orientation.
    /// </summary>
    /// <param name="rig">The hierarchy, its bones in breadth-first order.</param>
    /// <param name="bones">The bones, their orientations as the animation wrote them.</param>
    /// <param name="callbacks">The bones' callbacks, or <see langword="null"/> for none.</param>
    public static void UpdateHierarchy(Igi2BoneHierarchy rig, Igi2BoneBuffer bones, IIgi2BoneCallbacks? callbacks)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(bones);
        IReadOnlyList<int> parents = rig.Parents;
        IReadOnlyList<Vector3f> rest = rig.Translations;
        int count = Math.Min(rig.BoneCount, bones.BoneCount);
        for (int bone = 0; bone < count; bone++)
        {
            int parent = parents[bone];
            if (parent < 0)
            {
                bones.Translations[bone] = bones.Root;
                parent = bone;
            }
            else
            {
                bones.Orientations[bone] = Multiply(bones.Orientations[parent], bones.Orientations[bone]);
                Matrix3f m = bones.Matrices[parent];
                Vector3f p = bones.Translations[parent];
                Vector3f h = rest[bone];
                bones.Translations[bone] = new Vector3f(
                    (((((Fpu)m.M12 * h.Y) + ((Fpu)m.M11 * h.X)) + ((Fpu)m.M13 * h.Z)) + p.X).F,
                    (((((Fpu)m.M22 * h.Y) + ((Fpu)m.M21 * h.X)) + ((Fpu)m.M23 * h.Z)) + p.Y).F,
                    (((((Fpu)m.M32 * h.Y) + ((Fpu)m.M31 * h.X)) + ((Fpu)m.M33 * h.Z)) + p.Z).F);
            }

            callbacks?.Run(bones, bone, parent);
            float[] r = Igi2Pose.FromQuaternion(bones.Orientations[bone]);
            bones.Matrices[bone] = new Matrix3f(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8]);
        }
    }
}

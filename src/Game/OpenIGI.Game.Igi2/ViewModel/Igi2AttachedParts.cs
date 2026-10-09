using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>A model a clip hangs on a posed body: where it stands and how it is turned, in the body's frame.</summary>
/// <param name="Model">The model's name.</param>
/// <param name="Position">Where it stands in the body's frame, in world units.</param>
/// <param name="Orientation">How it is turned in the body's frame.</param>
public readonly record struct Igi2AttachedPart(string Model, Vector3f Position, Matrix3f Orientation);

/// <summary>The models a clip's attach objects hang on the bones of a posed body.</summary>
/// <remarks>
/// <para>
/// The clip's attach objects stand on their bones at their relative positions, moved by every re-link the clip has passed. A
/// record's matrix turns its object as <c>ModelObj_CreateAttachObject</c> (<c>igi2 0x56D0B0</c>) reads it, its first two columns
/// as rows and the third row their cross product; a re-link's matrix is taken whole and transposed (<c>AnimController_Step</c>,
/// <c>0x5549A0</c>). The object then stands at the bone's orientation times that (<c>ModelObj_UpdateAttachment</c>, <c>0x56CB40</c>:
/// parent · bone · relative).
/// </para>
/// <para>
/// One named <c>weapon</c> draws the weapon in hand's <c>Fixed Model</c>, as <c>WeaponMagicObj_GetModelName</c> (<c>0x4733B0</c>)
/// finds it through the human that owns the body, and nothing when nothing is held; any other draws the model it names.
/// </para>
/// </remarks>
public static class Igi2AttachedParts
{
    /// <summary>The name of the attach object that draws the weapon in hand.</summary>
    public const string WeaponAttachName = "weapon";

    /// <summary>The parts a clip hangs on a pose at a time.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The clip's time.</param>
    /// <param name="pose">The body's pose.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/> for nothing held.</param>
    /// <returns>The parts, in the clip's order.</returns>
    public static IEnumerable<Igi2AttachedPart> Of(Igi2AnimationClip clip, int time, BonePose pose, string? heldModel)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(pose);
        for (int i = 0; i < clip.AttachObjects.Count; i++)
        {
            Igi2AnimationAttachObject attached = clip.AttachObjects[i];
            string? model = string.Equals(attached.Name, WeaponAttachName, StringComparison.OrdinalIgnoreCase)
                ? heldModel
                : attached.Name;
            if (string.IsNullOrEmpty(model))
            {
                continue;
            }

            (int bone, Vector3f position, Matrix3f orientation) = Resolve(clip, i, time);
            if ((uint)bone >= (uint)pose.BoneCount)
            {
                continue;
            }

            Matrix3f boneRotation = pose.Rotations[bone];
            Vector3f turned = boneRotation.Transposed().Transform(position);
            Vector3f bonePosition = pose.Positions[bone];
            yield return new Igi2AttachedPart(
                model,
                new Vector3f(turned.X + bonePosition.X, turned.Y + bonePosition.Y, turned.Z + bonePosition.Z),
                boneRotation * orientation);
        }
    }

    /// <summary>A clip's attach object by its name: the bone it hangs on and where, after every re-link the clip has passed.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The clip's time.</param>
    /// <param name="name">The attach object's name, compared without case.</param>
    /// <param name="bone">Receives the bone.</param>
    /// <param name="position">Receives its relative position, <c>tRelativePos</c>.</param>
    /// <param name="orientation">Receives its relative orientation, <c>tRelativeOrientation</c>.</param>
    /// <returns>Whether the clip has one of that name.</returns>
    public static bool TryFind(Igi2AnimationClip clip, int time, string name, out int bone, out Vector3f position, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(clip);
        for (int i = 0; i < clip.AttachObjects.Count; i++)
        {
            if (string.Equals(clip.AttachObjects[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                (bone, position, orientation) = Resolve(clip, i, time);
                return true;
            }
        }

        bone = 0;
        position = default;
        orientation = Matrix3f.Identity;
        return false;
    }

    /// <summary>
    /// Where an attachment stands in the world, <c>ModelObj_UpdateAttachment</c> (<c>igi2 0x56CB40</c>) for one on a bone: its relative
    /// position turned by the bone's rows plus the bone's position, turned by the parent's rows and added to the parent's position;
    /// its orientation the parent's times the bone's times its relative one, each sum in the routine's order at 24 bits.
    /// </summary>
    /// <param name="parentPosition">The parent's position.</param>
    /// <param name="parent">The parent's orientation.</param>
    /// <param name="bonePosition">The bone's position in the parent's frame.</param>
    /// <param name="bone">The bone's rotation.</param>
    /// <param name="relative">The relative position.</param>
    /// <param name="relativeOrientation">The relative orientation.</param>
    /// <returns>The attachment's position and orientation.</returns>
    public static (Vector3d Position, Matrix3f Orientation) Place(
        Vector3d parentPosition, Matrix3f parent, Vector3f bonePosition, Matrix3f bone, Vector3f relative, Matrix3f relativeOrientation)
    {
        Matrix3f b = bone, p = parent, r = relativeOrientation;
        Vector3f q = relative;
        Fpu bx = (((Fpu)b.M12 * q.Y) + ((Fpu)q.X * b.M11)) + ((Fpu)q.Z * b.M13);
        Fpu by = (((Fpu)b.M22 * q.Y) + ((Fpu)b.M21 * q.X)) + ((Fpu)b.M23 * q.Z);
        Fpu bz = (((Fpu)q.X * b.M31) + ((Fpu)q.Z * b.M33)) + ((Fpu)b.M32 * q.Y);
        Fpu tx = (Fpu)bx.F + bonePosition.X, ty = (Fpu)by.F + bonePosition.Y, tz = (Fpu)bz.F + bonePosition.Z;
        float px = (((tz * p.M13) + (ty * p.M12)) + (tx * p.M11)).F;
        float py = (((tx * p.M21) + (tz * p.M23)) + (ty * p.M22)).F;
        float pz = (((tx * p.M31) + (tz * p.M33)) + (ty * p.M32)).F;
        Vector3d at = new(((Fpu)px + parentPosition.X).Value, ((Fpu)py + parentPosition.Y).Value, ((Fpu)pz + parentPosition.Z).Value);

        Fpu a11 = (((Fpu)b.M11 * p.M11) + ((Fpu)p.M13 * b.M31)) + ((Fpu)b.M21 * p.M12);
        Fpu a12 = (((Fpu)b.M12 * p.M11) + ((Fpu)p.M13 * b.M32)) + ((Fpu)p.M12 * b.M22);
        Fpu a13 = (((Fpu)p.M13 * b.M33) + ((Fpu)p.M12 * b.M23)) + ((Fpu)p.M11 * b.M13);
        Fpu a21 = (((Fpu)b.M21 * p.M22) + ((Fpu)b.M31 * p.M23)) + ((Fpu)b.M11 * p.M21);
        Fpu a22 = (((Fpu)b.M22 * p.M22) + ((Fpu)b.M12 * p.M21)) + ((Fpu)b.M32 * p.M23);
        Fpu a23 = (((Fpu)b.M23 * p.M22) + ((Fpu)p.M21 * b.M13)) + ((Fpu)b.M33 * p.M23);
        Fpu a31 = (((Fpu)p.M31 * b.M11) + ((Fpu)b.M21 * p.M32)) + ((Fpu)p.M33 * b.M31);
        Fpu a32 = (((Fpu)p.M32 * b.M22) + ((Fpu)p.M33 * b.M32)) + ((Fpu)p.M31 * b.M12);
        Fpu a33 = (((Fpu)p.M31 * b.M13) + ((Fpu)p.M32 * b.M23)) + ((Fpu)p.M33 * b.M33);
        Matrix3f o = new(
            (((a11 * r.M11) + (a13 * r.M31)) + (a12 * r.M21)).F,
            (((a11 * r.M12) + (a13 * r.M32)) + (a12 * r.M22)).F,
            (((a11 * r.M13) + (a13 * r.M33)) + (a12 * r.M23)).F,
            (((a21 * r.M11) + (a23 * r.M31)) + (a22 * r.M21)).F,
            (((a21 * r.M12) + (a23 * r.M32)) + (a22 * r.M22)).F,
            (((a21 * r.M13) + (a23 * r.M33)) + (a22 * r.M23)).F,
            (((a33 * r.M31) + (a32 * r.M21)) + (a31 * r.M11)).F,
            (((a31 * r.M12) + (a33 * r.M32)) + (a32 * r.M22)).F,
            (((a31 * r.M13) + (a33 * r.M33)) + (a32 * r.M23)).F);
        return (at, o);
    }

    /// <summary>An attach object's bone and relative placement after the re-links the clip has passed by a time.</summary>
    private static (int Bone, Vector3f Position, Matrix3f Orientation) Resolve(Igi2AnimationClip clip, int index, int time)
    {
        Igi2AnimationAttachObject attached = clip.AttachObjects[index];
        int bone = attached.Bone;
        Vector3f position = attached.Position;
        Matrix3f orientation = Igi2Pose.RelativeOrientation(attached.Orientation);
        foreach (Igi2AnimationLink link in clip.Links)
        {
            if (link.Time > time)
            {
                break;
            }

            if (link.AttachObject == index)
            {
                bone = link.Bone;
                position = link.Translation;
                orientation = link.Orientation.Transposed();
            }
        }

        return (bone, position, orientation);
    }
}

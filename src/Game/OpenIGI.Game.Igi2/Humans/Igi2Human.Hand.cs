using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>Where a human's body and the weapon in its hand stand, for what it drops.</content>
public abstract partial class Igi2Human
{
    /// <summary>
    /// Where the weapon in hand stands, <c>HumanAnim_GetWeaponPositionAndOrientation</c> (<c>igi2 0x58FCD0</c>): the active clip's
    /// <c>weapon</c> attachment placed on its bone as the bones now stand, or a metre above the human, turned as it is.
    /// </summary>
    /// <returns>Where it stands and how it is turned.</returns>
    public (Vector3d Position, Matrix3f Orientation) WeaponPlacement()
    {
        if (BodyPose() is { AttachClip: { } clip } pose
            && ViewModel.Igi2AttachedParts.TryFind(clip, pose.AttachTime, ViewModel.Igi2AttachedParts.WeaponAttachName, out int bone, out Vector3f relative, out Matrix3f relativeOrientation)
            && (uint)bone < (uint)pose.Pose.BoneCount)
        {
            return ViewModel.Igi2AttachedParts.Place(Position, Orientation, pose.Pose.Positions[bone], pose.Pose.Rotations[bone], relative, relativeOrientation);
        }

        return (new Vector3d(Position.X, Position.Y, ((Fpu)Position.Z + 4096.0).Value), Orientation);
    }

    /// <summary>
    /// Where the body's root stands for what it drops (bonedyncubeobj.c <c>sub_580CA0</c>): its position plus its frame times the root's bind
    /// translation, the height's share kept in a float first.
    /// </summary>
    /// <returns>The root's place.</returns>
    public Vector3d HeldRootPosition()
    {
        Matrix3f o = Orientation;
        Vector3f r = RootTranslation;
        float z = (((Fpu)o.M32 * r.Y) + ((Fpu)o.M31 * r.X) + ((Fpu)o.M33 * r.Z)).F;
        Fpu y = ((Fpu)o.M22 * r.Y) + ((Fpu)o.M21 * r.X) + ((Fpu)o.M23 * r.Z);
        Fpu x = ((Fpu)o.M12 * r.Y) + ((Fpu)o.M11 * r.X) + ((Fpu)o.M13 * r.Z) + Position.X;
        return new Vector3d(x.Value, (y + Position.Y).Value, ((Fpu)z + Position.Z).Value);
    }
}

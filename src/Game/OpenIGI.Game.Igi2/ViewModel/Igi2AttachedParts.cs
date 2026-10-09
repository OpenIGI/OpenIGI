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

            int bone = attached.Bone;
            Vector3f position = attached.Position;
            Matrix3f orientation = Igi2Pose.RelativeOrientation(attached.Orientation);
            foreach (Igi2AnimationLink link in clip.Links)
            {
                if (link.Time > time)
                {
                    break;
                }

                if (link.AttachObject == i)
                {
                    bone = link.Bone;
                    position = link.Translation;
                    orientation = link.Orientation.Transposed();
                }
            }

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
}

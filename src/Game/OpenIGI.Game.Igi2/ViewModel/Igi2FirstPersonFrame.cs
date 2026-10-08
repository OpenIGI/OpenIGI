using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>What the view holds as one tick left it: the first-person model's pose and the models its clip hangs on it.</summary>
/// <remarks>
/// <para>
/// The view's clip is sampled as its controller is made to (<c>HumanView_CreateHandler</c>, <c>igi2 0x4D5A30</c>, sets
/// the spline bit), so rotations are SQUAD-interpolated, and posed on the model's own hierarchy
/// (<c>BoneDynCubeObj_UpdateHierarchy</c>, <c>0x564B50</c>).
/// </para>
/// <para>
/// The clip's attach objects stand on their bones at their relative positions, moved by every re-link the clip has
/// passed. A record's matrix turns its object as <c>ModelObj_CreateAttachObject</c> (<c>0x56D0B0</c>) reads it, its
/// first two columns as rows and the third row their cross product; a re-link's matrix is taken whole and transposed
/// (<c>AnimController_Step</c>, <c>0x5549A0</c>). The object then stands at the bone's orientation times that
/// (<c>ModelObj_UpdateAttachment</c>, <c>0x56CB40</c>: parent · bone · relative). One named <c>weapon</c> draws the weapon in hand's <c>Fixed Model</c>, as <c>WeaponMagicObj_GetModelName</c>
/// (<c>0x4733B0</c>) finds it through the human that owns the view, and nothing when nothing is held; any other draws
/// the model it names.
/// </para>
/// <para>
/// Nothing is drawn while the view is zoomed or its owner is dead, as <c>HumanView_EnterHandler</c> (<c>0x4D5DB0</c>)
/// declines then. Not yet: an attach object's own key track, the grenade pin and the rocket, which have handlers of
/// their own.
/// </para>
/// </remarks>
public sealed class Igi2FirstPersonFrame
{
    /// <summary>The name of the attach object that draws the weapon in hand.</summary>
    public const string WeaponAttachName = "weapon";

    private readonly Dictionary<Igi2BoneHierarchy, BoneSkeleton> _skeletons = new(ReferenceEqualityComparer.Instance);
    private Quaternion32[] _rotations = [];

    /// <summary>Builds the frame for a human's view.</summary>
    /// <param name="human">The human whose view it is.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/> for nothing held.</param>
    /// <param name="tick">The tick the frame is for.</param>
    /// <returns>The frame, or <see langword="null"/> when the view draws nothing.</returns>
    public TitleFirstPersonView? Build(Igi2Human human, string? heldModel, long tick)
    {
        ArgumentNullException.ThrowIfNull(human);
        Igi2HumanView view = human.View;
        if (view.ObjectRig is not { } rig || view.ObjectModel.Length == 0 || view.IsZoomMode || human.IsDead
            || view.ObjectAnimation.Clip is not { } clip || clip.BoneCount != rig.BoneCount)
        {
            return null;
        }

        if (!_skeletons.TryGetValue(rig, out BoneSkeleton? skeleton))
        {
            skeleton = rig.CreateSkeleton();
            _skeletons[rig] = skeleton;
        }

        if (_rotations.Length != clip.BoneCount)
        {
            _rotations = new Quaternion32[clip.BoneCount];
        }

        int time = view.ObjectAnimation.Time;
        Vector3f root = Igi2ClipSampler.Sample(clip, time, squad: true, _rotations);
        BonePose pose = BonePose.Evaluate(skeleton, _rotations, root);

        List<TitleViewModelPart> parts = [new TitleViewModelPart(view.ObjectModel, pose, default, Matrix3f.Identity)];
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
            parts.Add(new TitleViewModelPart(
                model,
                null,
                new Vector3f(turned.X + bonePosition.X, turned.Y + bonePosition.Y, turned.Z + bonePosition.Z),
                boneRotation * orientation));
        }

        return new TitleFirstPersonView(view.ObjectPosition, view.ObjectOrientation, view.ObjectFieldOfView, tick, parts);
    }
}

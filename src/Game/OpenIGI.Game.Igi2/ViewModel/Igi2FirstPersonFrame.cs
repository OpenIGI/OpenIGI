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
/// The clip's attach objects hang their models on the pose (<see cref="Igi2AttachedParts"/>), the <c>weapon</c> one the weapon in
/// hand.
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
    public const string WeaponAttachName = Igi2AttachedParts.WeaponAttachName;

    private readonly Dictionary<Igi2BoneHierarchy, BoneSkeleton> _skeletons = new(ReferenceEqualityComparer.Instance);
    private Quaternion32[] _rotations = [];

    /// <summary>Builds the frame for a human's view.</summary>
    /// <param name="human">The human whose view it is.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/> for nothing held.</param>
    /// <param name="tick">The tick the frame is for.</param>
    /// <param name="attachments">The mission's rigid attachment resolver.</param>
    /// <param name="flashes">The flashes at the weapon objects, or <see langword="null"/>.</param>
    /// <returns>The frame, or <see langword="null"/> when the view draws nothing.</returns>
    public TitleFirstPersonView? Build(
        Igi2Human human,
        string? heldModel,
        long tick,
        Igi2ModelAttachments? attachments = null,
        Weapons.Igi2MuzzleFlashes? flashes = null)
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

        Vector3f root = Igi2ClipSampler.Sample(clip, view.ObjectAnimation.Time, squad: true, _rotations);
        BonePose pose = BonePose.Evaluate(skeleton, _rotations, root);

        List<TitleViewModelPart> parts = [new TitleViewModelPart(view.ObjectModel, pose, default, Matrix3f.Identity)];
        int weaponObject = 0;
        foreach (Igi2AttachedPart part in Igi2AttachedParts.Of(clip, view.ObjectAnimation.Time, pose, heldModel,
            attachments is null ? null : attachments.ModelOf))
        {
            TitleViewModelPart rootPart = new(part.Model, null, part.Position, part.Orientation)
            {
                MuzzleFlashes = Igi2BodyFrame.IsWeaponObject(part, heldModel) ? flashes?.Present(weaponObject++) : null,
            };
            if (attachments is null)
            {
                parts.Add(rootPart);
            }
            else
            {
                parts.AddRange(attachments.Expand(rootPart));
            }
        }

        return new TitleFirstPersonView(view.ObjectPosition, view.ObjectOrientation, view.ObjectFieldOfView, tick, parts);
    }
}

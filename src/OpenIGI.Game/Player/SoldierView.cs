using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>
/// A placed soldier's drawable state: the declared skeleton posed by the declared stand animation,
/// facing the declared heading.
/// </summary>
/// <remarks>
/// <para>
/// This is the soldier init handler (<c>0x456E50</c>) and the tail of <c>Human</c>'s (<c>0x45F890</c>)
/// without the scene graph — see the original. The original builds the bone
/// object from the task's <c>Bone Heirachy</c> id (<c>0x4D9620</c>), plays its <c>Stand Animation</c>
/// on bone 0 of the rig, resolves the ordinary <c>Model</c> parameter by name, and writes the facing
/// as a pure Z rotation of <c>Gamma</c> into the object's own 3×3 (<c>0x4B3BE0</c>).
/// </para>
/// <para>
/// The pose is in the object's space with the facing baked in (<c>0x4D38C0</c> builds the palette with
/// a zero root position under the object's 3×3), so the world position is added by whoever draws it —
/// the same convention as <see cref="HumanView"/>.
/// </para>
/// </remarks>
public sealed class SoldierView
{
    /// <summary>
    /// The animation a soldier stands in when its task declares none (<c>-1</c>) or declares an id the
    /// set does not hold — the literal <c>2</c> in <c>0x456E50</c>.
    /// </summary>
    /// <remarks>Every shipped set starts its ids at 2, so the fallback resolves in all six.</remarks>
    public const int FallbackStandAnimation = 2;

    private readonly BoneObject _bones;
    private BonePose? _pose;

    /// <summary>Initializes a new instance of the <see cref="SoldierView"/> class.</summary>
    /// <param name="soldier">The placed task, already bound from the level script.</param>
    /// <param name="set">The animation set whose skeleton the task's <c>Bone Heirachy</c> names.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The set is not the declared skeleton.</exception>
    public SoldierView(HumanSoldierTask soldier, BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        ArgumentNullException.ThrowIfNull(set);
        if (set.Skeleton.Id != soldier.BoneHierarchy)
        {
            throw new ArgumentException(
                $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, but this set is skeleton {set.Skeleton.Id}.",
                nameof(set));
        }

        Soldier = soldier;
        _bones = new BoneObject(set);

        // 0x456E50: -1 means "no stand declared" and plays 2; a declared id that is not in the set
        // prints "Animation #%d not found" and also plays 2. Both go to bone 0 with flags 0 - the
        // immediate arm, loop bit clear.
        int declared = soldier.StandAnimation;
        DeclaredStandAnimationFound = declared >= 0 && set.TryGet(declared, out _);
        StandAnimation = declared >= 0 && DeclaredStandAnimationFound ? declared : FallbackStandAnimation;
        _bones.Play(0, StandAnimation, loops: false);

        // 0x45F890's last line: 0x4B3BE0(human + 120, gamma), the plain Z rotation - which is
        // Z(gamma)*Y(0)*X(0) of the same composition 0x4B38E0 builds. The declared facing, no mirror.
        Orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma);
    }

    /// <summary>Gets the placed task this view draws.</summary>
    public HumanSoldierTask Soldier { get; }

    /// <summary>Gets the bone object the stand animation plays on.</summary>
    public BoneObject Bones => _bones;

    /// <summary>Gets the animation actually standing in — the declared id, or the fallback.</summary>
    public int StandAnimation { get; }

    /// <summary>
    /// Gets a value indicating whether the declared <c>Stand Animation</c> resolved in the set. When the
    /// task declared one and this is <see langword="false"/>, the original prints
    /// <c>"Animation #%d not found"</c> — a caller that wants that diagnostic checks here.
    /// </summary>
    public bool DeclaredStandAnimationFound { get; }

    /// <summary>Gets the world position, in engine units — the task's declared <c>Position</c>.</summary>
    public Vector3d Position => Soldier.Position;

    /// <summary>Gets the world 3×3 — the original's <c>Human+120</c>, a pure Z rotation of <c>Gamma</c>.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the current pose, in the object's own space with the facing baked in.</summary>
    /// <returns>The posed rig.</returns>
    public BonePose Pose() => _pose ??= _bones.Pose(Orientation);

    /// <summary>Gets the pose built with an identity root, for a renderer that applies the facing itself.</summary>
    /// <returns>The posed rig, unrotated.</returns>
    /// <remarks>
    /// Exactly equivalent to <see cref="Pose"/> under <see cref="Orientation"/>, because the root
    /// rotation only ever left-multiplies every bone — the trade the port's skinned draw already makes
    /// so that a shared pose skins once and turns per soldier (see <c>ViewModelRenderer</c>).
    /// </remarks>
    public BonePose UnrotatedPose() => _bones.Pose();
}

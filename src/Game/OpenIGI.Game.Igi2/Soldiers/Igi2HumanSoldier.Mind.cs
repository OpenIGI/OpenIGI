using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai.Runtime;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>What a soldier's mind reads of its body and asks it to do.</content>
public sealed partial class Igi2HumanSoldier
{
    private ulong _reportedSquads;

    /// <summary>Gets or sets a value indicating whether the soldier takes part, <c>Human.isActive</c>: the script switches it.</summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc/>
    protected override float DetectionVisibility => VisibilityFactor;

    /// <inheritdoc/>
    protected override bool IsDetectionActive => IsActive;

    /// <summary>Gets or sets the gun's hit chance a thrown weapon starts with, <c>Human.vWeaponHitProbability</c>.</summary>
    public float WeaponHitProbability { get; set; } = 1f;

    /// <summary>Gets or sets the mounted gun it sits at, or <see langword="null"/>.</summary>
    public IIgi2AiMountedGun? MountedGun { get; set; }

    /// <summary>Gets the seat it sees from when on a gun, or <see langword="null"/>.</summary>
    public Igi2SeatView? SeatView => MountedGun?.Seat;

    /// <summary>Gets its frame, row by row, as the mind's maths reads <c>tOrientation</c>.</summary>
    public float[] OrientationRows
    {
        get
        {
            Matrix3f o = Orientation;
            return [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
        }
    }

    /// <summary>Gets a value indicating whether every blend of its body has finished, <c>HumanAnim_IsBlendFinished</c>.</summary>
    public bool IsBlendFinished => Animation.IsBlendFinished;

    /// <summary>
    /// Where its head is without reading the skeleton (<c>HumanSoldier_GetCheapHeadBonePosition</c>): kneeling, a little back and
    /// 1.2 metres up; lying, two metres ahead and 0.3 up; otherwise 1.65 metres straight up.
    /// </summary>
    /// <returns>The head's world position.</returns>
    public Vector3d CheapHeadBonePosition()
    {
        float[] o = OrientationRows;
        int state = Locomotion.CurrentState;
        Fpu x, y, z;
        if (state is 27 or 32 or 33 or 34 or 35 or 20)
        {
            x = (((Fpu)o[2] * 4915.2001953125) + ((Fpu)o[1] * 0.0)) - ((Fpu)o[0] * 409.6000061035156);
            y = (((Fpu)o[5] * 4915.2001953125) + ((Fpu)o[4] * 0.0)) - ((Fpu)o[3] * 409.6000061035156);
            z = (((Fpu)o[8] * 4915.2001953125) + ((Fpu)o[7] * 0.0)) - ((Fpu)o[6] * 409.6000061035156);
        }
        else if (state is 36 or 37 or 38 or 22)
        {
            x = (((Fpu)o[2] * 1228.800048828125) + ((Fpu)o[1] * 8192.0)) + ((Fpu)o[0] * 0.0);
            y = (((Fpu)o[5] * 1228.800048828125) + ((Fpu)o[4] * 8192.0)) + ((Fpu)o[3] * 0.0);
            z = (((Fpu)o[8] * 1228.800048828125) + ((Fpu)o[7] * 8192.0)) + ((Fpu)o[6] * 0.0);
        }
        else
        {
            x = (((Fpu)o[1] + o[0]) * 0.0) + ((Fpu)o[2] * 6758.39990234375);
            y = (((Fpu)o[4] + o[3]) * 0.0) + ((Fpu)o[5] * 6758.39990234375);
            z = (((Fpu)o[7] + o[6]) * 0.0) + ((Fpu)o[8] * 6758.39990234375);
        }

        Vector3d at = Position;
        return new Vector3d(x.Value + at.X, y.Value + at.Y, z.F + at.Z);
    }

    /// <summary>Where the animated head bone is from the body's origin, in world axes.</summary>
    /// <returns>The offset.</returns>
    public Vector3f HeadBoneLocalPosition()
    {
        if (Model is not { } model)
        {
            return new Vector3f(0f, 0f, 6758.4f);
        }

        Vector3d head = BonePosition(model.HeadBone), at = Position;
        return new Vector3f((float)(head.X - at.X), (float)(head.Y - at.Y), (float)(head.Z - at.Z));
    }

    /// <summary>
    /// Runs its clips faster or slower for a tick and moves as much faster (<c>HumanAnim_ScaleAnimationTimeStep</c> with the speed
    /// scale the mind sets beside it).
    /// </summary>
    /// <param name="factor">The factor.</param>
    public void ScaleAnimationTimeStep(float factor)
    {
        Animation.MainTimeStep = Fpu.ToInt32((Fpu)(double)AnimationTicksPerFrame * factor);
        MovementSpeedScale = factor;
    }

    /// <summary>Plays the upper-body animation of a state, <c>HumanAnim_SetUpperBodyAnimationAndTimeFromState</c>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="blendSeconds">The blend.</param>
    public void SetUpperBodyAnimationFromState(int state, float blendSeconds) => Animation.PlayUpperBodyState(state, 0, blendSeconds);

    /// <summary>Whether its weapon has an upper-body clip for a state other than the lower-body stand.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    public bool HasWeaponAnimationForState(int state) => Animation.HasUpperBodyClipForState(state);

    /// <summary>Plays a named upper-body animation, <c>HumanAnim_SetUpperBodyAnimationAndTime</c>.</summary>
    /// <param name="name">The clip.</param>
    /// <param name="blendSeconds">The blend.</param>
    public void SetUpperBodyAnimation(string name, float blendSeconds) => Animation.PlayUpperBodyNamed(name, 0, blendSeconds);

    /// <summary>Lets the upper-body animation go, <c>HumanAnim_ClearUpperBodyAnimation</c>.</summary>
    /// <param name="blendSeconds">The blend.</param>
    public void ClearUpperBodyAnimation(float blendSeconds) => Animation.ClearUpperBody(blendSeconds);

    /// <summary>Reloads when less than a share of a clip is left, as detection does with no target.</summary>
    /// <param name="share">The share.</param>
    public void ReloadIfBelow(double share)
    {
        if (Weapon.Inventory is not { Current: { } item } inventory || Weapon.Firing is not { } firing)
        {
            return;
        }

        int clip = item.WeaponType.AmmoInfo(0, inventory.Tables).RoundsPerClip;
        if (item.Loaded(0) < Fpu.ToInt32((Fpu)(double)clip * share))
        {
            firing.Reload();
        }
    }

    /// <summary>Lets the weapon's trigger go.</summary>
    public void ReleaseTrigger() => Weapon.Firing?.ReleaseTriggers();

    /// <summary>Kills it where it stands, as a mind with no path to its node does: damage past its maximum.</summary>
    public void KillOutright()
    {
        Damage.Damage = Damage.MaxDamage + 1f;
        UpdateDie();
    }

    /// <summary>Whether its death or hit was already reported to a squad, by the squad's place in the level.</summary>
    /// <param name="squad">The squad.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool HasReportedTo(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        return (uint)squad.Index < 64u && ((_reportedSquads >> squad.Index) & 1ul) != 0;
    }

    /// <summary>Marks its death or hit as reported to a squad.</summary>
    /// <param name="squad">The squad.</param>
    public void ReportTo(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if ((uint)squad.Index < 64u)
        {
            _reportedSquads |= 1ul << squad.Index;
        }
    }
}

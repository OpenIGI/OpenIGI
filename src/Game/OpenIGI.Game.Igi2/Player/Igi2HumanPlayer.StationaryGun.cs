using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Working a mounted gun.</content>
public sealed partial class Igi2HumanPlayer : IIgi2GunOperator
{
    /// <summary>The state clip the player works a mounted gun with.</summary>
    public const int StationaryGunClip = 74;

    /// <summary>How far behind and below the gun's first magic vertex the player stands to work it, across the gun's frame.</summary>
    public static readonly (double Back, double Down) GunStance = (204.8, 6348.8);

    /// <inheritdoc/>
    float IIgi2GunOperator.LookAcross => Channels[Igi2HumanChannel.LookX];

    /// <inheritdoc/>
    float IIgi2GunOperator.LookUp => Channels[Igi2HumanChannel.LookY];

    /// <inheritdoc/>
    float IIgi2GunOperator.FireChannel => Channels[Igi2HumanChannel.Fire];

    /// <inheritdoc/>
    float IIgi2GunOperator.LastFireChannel => Channels.Last[(int)Igi2HumanChannel.Fire];

    /// <inheritdoc/>
    IReadOnlyList<float>? IIgi2GunOperator.AimFrame => null;

    /// <inheritdoc/>
    void IIgi2GunOperator.CountShot() => CountShots(1);

    // State 41 -- HumanPlayer_InitStateActivateStationaryGun: the stance kept and standing, a twentieth of a metre behind and 1.55 m
    // below the gun's first magic vertex, facing as the gun faces, the upper body cleared, the gun clip played, the view and the body
    // held still, the weapon put away.
    private void EnterActivateStationaryGun()
    {
        if (ActivatingTarget is not Igi2StationaryGunTask gun)
        {
            return;
        }

        StanceBeforeClimb = Pose;
        Pose = Igi2HumanPose.Standing;
        Vector3d at = gun.ActivatePosition(out IReadOnlyList<float> f);
        PopPosition(new Vector3d(
            ((((Fpu)at.X + ((Fpu)f[2] * -GunStance.Down)) - ((Fpu)f[1] * GunStance.Back)) + ((Fpu)f[0] * 0.0)).Value,
            ((((Fpu)at.Y + ((Fpu)f[5] * -GunStance.Down)) - ((Fpu)f[4] * GunStance.Back)) + ((Fpu)f[3] * 0.0)).Value,
            (((((Fpu)f[8] * -GunStance.Down) - ((Fpu)f[7] * GunStance.Back)) + ((Fpu)f[6] * 0.0)) + at.Z).Value));
        GroundMovementSpeed = default;
        PopOrientation(Igi2Pose.Angles(f).Gamma);
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing);
        IsLockAction = true;
        Animation.PlayState(StationaryGunClip, 0, 0f);
        View.LookAhead();
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        IsDisableDeltaTranslationScale = true;
        IsWeaponReady = false;
        AttachTo(gun, Position, f);
        Locomotion.Current = (_, _) => RunActivateStationaryGun(gun);
    }

    // HumanPlayer_RunStateActivateStationaryGun: the player stands at the gun until the activate key is pressed afresh, or death; then
    // the weapon is ready again, the view and body free, the gun let go and the activation over, and the player goes back to the stance
    // kept.
    private void RunActivateStationaryGun(Igi2StationaryGunTask gun)
    {
        MoveOnGround(0f, 118.76579284667969f, 0f, 118.76579284667969f);
        bool leave = (Signal & Igi2PlayerSignal.Activate) != 0 && Channels.Last[(int)Igi2HumanChannel.Activate] == 0f;
        leave |= (Signal & Igi2PlayerSignal.Dead) != 0;
        if (!leave)
        {
            return;
        }

        IsWeaponReady = true;
        IsLockAction = false;
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        gun.Deactivate();
        Detach();
        EndActivate();
        IsDisableDeltaTranslationScale = false;
        GoBackToStance();
    }
}

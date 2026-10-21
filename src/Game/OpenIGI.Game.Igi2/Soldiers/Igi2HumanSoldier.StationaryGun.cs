using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Manning a mounted gun.</content>
public sealed partial class Igi2HumanSoldier : IIgi2GunOperator
{
    /// <inheritdoc/>
    float IIgi2GunOperator.LookAcross => Channels[Igi2HumanChannel.LookX];

    /// <inheritdoc/>
    float IIgi2GunOperator.LookUp => Channels[Igi2HumanChannel.LookY];

    /// <inheritdoc/>
    float IIgi2GunOperator.FireChannel => Channels[Igi2HumanChannel.Fire];

    /// <inheritdoc/>
    float IIgi2GunOperator.LastFireChannel => Channels.Last[(int)Igi2HumanChannel.Fire];

    /// <inheritdoc/>
    /// <remarks>Its AI's view, <c>HumanAI_s.tView.tViewOrientation</c>, which a manned gun's seat sets.</remarks>
    IReadOnlyList<float>? IIgi2GunOperator.AimFrame => (Mind as Ai.Runtime.Igi2HumanAi)?.View.ViewOrientation;

    /// <inheritdoc/>
    void IIgi2GunOperator.CountShot() => CountShots(1);

    // State 85 -- HumanSoldier_InitStateStationaryGun: a twentieth of a metre behind and 1.55 m below the gun's first magic vertex,
    // facing as the gun faces, the upper body cleared and held, the gun row played, the view looking ahead, its heading and its movement
    // held still. A gun on a helicopter's door is manned seated (rows 28 and 85 above): no helicopter is ported.
    private void EnterStationaryGun()
    {
        if (ActivateTarget is not Igi2StationaryGunTask gun)
        {
            return;
        }

        MountedGun = gun;
        Vector3d at = gun.ActivatePosition(out IReadOnlyList<float> f);
        const double Back = -204.8, Down = -6348.8;
        PopPosition(new Vector3d(
            ((Fpu)at.X + ((((Fpu)f[2] * Down) + ((Fpu)f[1] * Back)) + ((Fpu)f[0] * 0.0))).Value,
            ((Fpu)at.Y + ((((Fpu)f[5] * Down) + ((Fpu)f[4] * Back)) + ((Fpu)f[3] * 0.0))).Value,
            ((((Fpu)at.Z + ((Fpu)f[8] * Down)) + ((Fpu)f[7] * Back)) + ((Fpu)f[6] * 0.0)).Value));
        PopOrientation(Igi2Pose.Angles(f).Gamma);
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        PlayRow(Igi2SoldierState.StationaryGun, 0f);
        View.LookAhead();
        IsLockOrientation = true;
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
        AttachTo(gun, Position, f);
        Locomotion.Current = (_, _) => RunStationaryGun(gun);
    }

    // HumanSoldier_RunStateStationaryGun: dead, or told to stand, the soldier leaves the gun and stands.
    private void RunStationaryGun(Igi2StationaryGunTask gun)
    {
        if ((Signal & (Igi2SoldierSignal.Dead | Igi2SoldierSignal.StandAction)) == 0)
        {
            return;
        }

        DeactivateStationaryGun(gun);
        IsDisableDeltaTranslation = false;
        if ((UpperBodyFlags & 1) != 0)
        {
            UpperBodyFlags &= ~1;
            Animation.ClearUpperBody(0f);
        }

        Go(Igi2SoldierState.Stand);
    }

    // HumanSoldier_DeactivateStationaryGun: the view and the heading freed, the gun let go, the activation over.
    private void DeactivateStationaryGun(Igi2StationaryGunTask gun)
    {
        View.IsLockOrientation = false;
        IsLockOrientation = false;
        gun.Deactivate();
        Detach();
        MountedGun = null;
        ActivateTarget = null;
    }
}

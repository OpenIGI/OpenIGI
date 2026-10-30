using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>A co-op session's partners as the mission shows them: their shots' lines.</summary>
public sealed partial class Igi1MissionTick
{
    // How far a partner's line is cast before it is let go of: three hundred metres.
    private const double PartnerShotReach = 300 * 4096.0;

    // A partner that fired this tick leaves a line from its eye along its aim to what stops the round, drawn as a guard's
    // tracer is, so everyone else can see where it is shooting. Not the original's: its player's rounds draw no line.
    private void ShowPartnerShot(in PartnerBody partner)
    {
        if (!partner.Fired
            || partner.Weapon is not { } weapon
            || weapon.Category is WeaponCategory.Knife or WeaponCategory.Grenade
            || mission.Soldiers is not { } soldiers
            || mission.AmmoTypes is not { } ammunition
            || !ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo))
        {
            return;
        }

        double yaw = -partner.Facing;
        double pitch = partner.Pose.AimPitch;
        Vector3d aim = new(Math.Sin(yaw) * Math.Cos(pitch), Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch));
        Vector3d eye = partner.Feet + new Vector3d(0, 0, PlayerBody.StandingEyeHeight);
        Vector3d end = eye + (aim * PartnerShotReach);
        if (mission.Player?.GrenadeWalls is { } walls && walls(eye, end, out WorldRayHit hit))
        {
            end = hit.Position;
        }

        float length = (float)(end - eye).Length;
        if (length <= 0f)
        {
            return;
        }

        soldiers.FileTracer(new GunShotTracer
        {
            Start = eye,
            End = end,
            EndDistance = length,
            Range = length,
            Ammunition = ammo,
            Shooter = partner.Key,
            Life = GunShotBallistics.TracerLifetimeTicks,
        });
    }
}

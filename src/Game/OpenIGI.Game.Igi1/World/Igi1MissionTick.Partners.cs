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
        if (partner.Fired)
        {
            ShowShot(partner.Key, partner.Weapon, partner.Feet, -partner.Facing, partner.Pose.AimPitch);
        }
    }

    // A client's soldiers' shots: each a line through the air, as the server's rounds were.
    private void ShowPuppetShots()
    {
        if (mission.Soldiers is not { } soldiers)
        {
            return;
        }

        foreach ((Player.SoldierView view, WeaponType? weapon, float facing, float pitch) in soldiers.TakePuppetShots())
        {
            // A soldier's facing is the angle its body is turned by, as a partner's is; the aim's yaw is that negated.
            ShowShot(view, weapon, view.Position, -facing, pitch);
        }
    }

    // One shot's tracer from the eye along the aim to whatever it meets.
    private void ShowShot(object shooter, WeaponType? held, Vector3d feet, double yaw, double pitch)
    {
        if (held is not { } weapon
            || weapon.Category is WeaponCategory.Knife or WeaponCategory.Grenade
            || mission.Soldiers is not { } soldiers
            || mission.AmmoTypes is not { } ammunition
            || !ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo))
        {
            return;
        }

        Vector3d aim = new(Math.Sin(yaw) * Math.Cos(pitch), Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch));
        Vector3d eye = feet + new Vector3d(0, 0, PlayerBody.StandingEyeHeight);
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
            Shooter = shooter,
            Life = GunShotBallistics.TracerLifetimeTicks,
        });
    }
}

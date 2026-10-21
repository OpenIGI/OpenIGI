using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A soldier's weapon as its body's trigger drives it: the cadence and the clip, where a round really goes, the kick it
/// hands back, and the grenade a throw lets go of.
/// </summary>
/// <remarks>
/// The AI never fires: it holds slot 6 down and the weapon decides when a round comes out, which is the split the
/// original has between <c>0x48D8E0</c> and the human's weapon tick <c>0x462E50</c>.
/// </remarks>
/// <param name="context">The soldiers and the level's stream.</param>
/// <param name="rounds">The soldiers' guns' <c>GunShot</c> tasks, which a marched round is queued on.</param>
/// <param name="noise">What the soldiers hear, told of every shot.</param>
internal sealed class SoldierWeaponFire(SoldierContext context, SoldierGunShotRounds rounds, SoldierNoiseBroadcaster noise)
{
    private readonly SoldierContext _context = context;

    private readonly SoldierGunShotRounds _rounds = rounds;

    private readonly SoldierNoiseBroadcaster _noise = noise;

    /// <summary>Gets or sets the kick the firing soldier's weapon returned this tick, pitch and yaw in radians.</summary>
    public (float Pitch, float Yaw) KickThisTick { get; set; }

    /// <summary>Gets or sets how many rounds the level's soldiers have fired.</summary>
    public int RoundsFired { get; set; }

    /// <summary>Gets or sets where a soldier's rounds go, or <see langword="null"/> to let rounds vanish.</summary>
    public SoldierSimulation.SoldierRound? OnRound { get; set; }

    /// <summary>The weapon state a brain's fight reads: the mounted gun's when it has one, else the carried one's.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="held">The roster entry carrying its weapon.</param>
    /// <returns>The clip, its size, the burst and the category.</returns>
    public static AiCombatWeaponState CombatWeaponState(Igi1HumanAi brain, SoldierAgent held)
    {
        if (brain.MountedGun is { Weapon: { } mounted } gun)
        {
            return new AiCombatWeaponState(
                gun.AmmunitionSample,
                mounted.RoundsPerClip,
                mounted.MaximumRoundsPerBurst,
                mounted.Category);
        }

        return new AiCombatWeaponState(
            held.AmmoInClip,
            held.WeaponType?.RoundsPerClip ?? 0,
            held.WeaponType?.MaximumRoundsPerBurst ?? 0,
            held.WeaponType?.Category ?? WeaponCategory.Knife);
    }

    /// <summary>Where a follow-firing patrol's round goes: along its own facing, at the aim point's height.</summary>
    /// <param name="brain">The shooter.</param>
    /// <param name="enemyFeet">The target's feet, in world units.</param>
    /// <param name="enemyCrouching">Whether the target is crouching.</param>
    /// <remarks>
    /// The follow fire has no burst and no aim solve: the trigger is pressed whenever the
    /// target is inside a 16.3° cone about the shooter's forward axis, and the round flies where the BODY
    /// points. The port's facing has no pitch, so the ray borrows the aim point's height and keeps the
    /// facing's horizontal error — which is the part that decides the spray's geometric misses.
    /// </remarks>
    public static Vector3d SprayTarget(Igi1HumanAi brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Vector3d aimPoint = Igi1HumanAi.AimPointOf(enemyFeet, enemyCrouching);
        Vector3d eye = brain.EyePosition();
        double dx = aimPoint.X - eye.X;
        double dy = aimPoint.Y - eye.Y;
        double reach = Math.Sqrt((dx * dx) + (dy * dy));
        if (reach < 1.0)
        {
            return aimPoint;
        }

        return new Vector3d(
            eye.X + (-Math.Sin(brain.Facing) * reach),
            eye.Y + (Math.Cos(brain.Facing) * reach),
            aimPoint.Z);
    }

    /// <summary>Turns a raised trigger into a round, at the weapon's own cadence.</summary>
    /// <remarks>
    /// <para>
    /// The AI never fires: it holds slot 6 down and the weapon decides when a round comes out, which is
    /// the split the original has between <c>0x48D8E0</c> and the human's weapon tick <c>0x462E50</c>.
    /// The clip is what the AI watches to count its burst, so it has to drain here and nowhere else.
    /// </para>
    /// <para>
    /// The reload takes the weapon's own <c>Reload Time</c>; both of the AI's two reload routes reach
    /// the same place, so they are not told apart here.
    /// </para>
    /// </remarks>
    public bool FireIfAsked(SoldierView view, Igi1HumanAi brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        SoldierAgent? member = _context.FindAgent(view);
        AiStationaryGun? mounted = brain.MountedGun;
        WeaponType? weapon = mounted?.Weapon ?? member?.WeaponType;
        if (weapon is null)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (mounted.FireCooldownTicks > 0)
            {
                mounted.FireCooldownTicks--;
            }
        }
        else if (member is not null)
        {
            if (member.FireCooldown > 0)
            {
                member.FireCooldown--;
            }

            if (member.ReloadTicks > 0 && --member.ReloadTicks == 0)
            {
                member.AmmoInClip = weapon.RoundsPerClip;
            }
        }

        AiCombatCommand command = brain.Combat;

        // The reload child's direct reload (0x48DB10 -> 0x4F2350(gun, 1) -> 0x4798C0) sets the clip to a full one
        // there and then; only the displayed reload takes the weapon's time.
        if (mounted is null && member is not null
            && command.ReloadWeaponDirectly
            && member.AmmoInClip < weapon.RoundsPerClip)
        {
            member.AmmoInClip = weapon.RoundsPerClip;
            member.ReloadTicks = 0;
            return false;
        }

        if (mounted is null && member is not null
            && command.PressReload
            && member.ReloadTicks == 0
            && member.AmmoInClip < weapon.RoundsPerClip)
        {
            member.ReloadTicks = Math.Max(1, (int)(weapon.ReloadTimeSeconds * 30f));
            return false;
        }

        // IGI 1 does not hand slot 6 straight to the gun. The HumanSoldier body forwards it only from
        // states 6, 18, 22 and 27 (standing, running, kneeling and prone fire respectively). Previously
        // this method ran before the body state tick, so rounds left during rack/ready/recover clips.
        bool bodyForwardsTrigger = mounted is not null
            || (!brain.CombatStateChangedThisTick && brain.Body.IsFiring);

        // 0x478BA0 zeroes the gun's rounds-this-pull count whenever the fire message's trigger byte is clear. That byte
        // is Human+1233, which only the firing state's per-tick sets, so a reload or a recovery clears the count even
        // while the AI holds the fire channel: the first round after a reload is a minimum-spread round again.
        if (!(command.PullTrigger && bodyForwardsTrigger) && member is not null)
        {
            member.RoundsThisPull = 0;
        }

        bool blocked = mounted is not null
            ? mounted.FireCooldownTicks > 0 || mounted.AmmunitionSample <= 0
            : member is null || member.FireCooldown > 0 || member.AmmoInClip <= 0 || member.ReloadTicks > 0;
        if (!command.PullTrigger || !bodyForwardsTrigger || blocked)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (!mounted.ConsumeRound())
            {
                return false;
            }
            mounted.FireCooldownTicks = GunTiming.TicksPerRound(weapon.RoundsPerMinute);

            // The round is the stand's: 0x474360 fires the gun object and keeps what it answers, so the stand's
            // barrel flames, not the gunner's own weapon.
            mounted.RecordShot(fired: true, kickPitch: 0f, kickYaw: 0f);
        }
        else
        {
            member!.AmmoInClip--;
            member.FireCooldown = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        }
        RoundsFired++;
        if (member is not null && mounted is null)
        {
            member.FiredThisTick = true;
            member.ShotSequence++;
        }

        // The round leaves along where the GUN is pointed, not at the target's true position. Retail's
        // round is a hit-scan ray resolved in the fire tick (GunShot per-tick 0x47B750): the guard's body
        // was turned onto the TRACKED aim point (0x48D8E0 holds fire until the facing is within 2 degrees
        // of it), the ray flies that way, and the hit probability rides the cast's own context (slot+68,
        // loaded from Human+1236 at spawn, sub_474360) to be rolled only when the trace actually meets a
        // human. So the tracked aim's lag against a MOVING target is a real, geometric source of misses,
        // and a round that fails the roll flies on to whatever is behind.
        //
        // A burst-firing guard carries its aim in Combat.TurnTo (the tracked point, started on the target
        // at burst setup, 0x48B9C0). A follow-firing patrol has no aim solve at all -- it
        // sprays along its own facing whenever the target is inside a 16.3-degree cone, so its ray runs
        // along the body's heading at the aim point's height.
        Vector3d target = brain.Combat.TurnTo ?? SprayTarget(brain, enemyFeet, enemyCrouching);
        Vector3d origin = mounted?.MuzzlePosition ?? SoldierContext.ViewPosition(view, brain);
        Vector3d aim = SpreadRound(brain, mounted, weapon, member, origin, target);

        // 0x479780, the carried gun's fire handler, after the cone: the kick, drawn from a disc (pairs until one
        // lands inside), which 0x462E50 hands back to the Human's view.
        if (mounted is null)
        {
            (float pitchKick, float yawKick) = HeldGun.SampleRecoil(weapon, _context.AiRandom);
            KickThisTick = (pitchKick, yawKick);
        }

        if (_rounds.MarchesRounds && _rounds.GunShots.TryGetValue(view, out GunShotTask? gunShot))
        {
            Vector3d line = aim - origin;
            double length = line.Length;
            Vector3f direction = length > 0.0
                ? new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                : default;
            gunShot.Queue(weapon, origin, direction, brain.HitProbability, view);
        }

        OnRound?.Invoke(origin, aim, brain.HitProbability, weapon, view);

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises.
        _noise.NotifyGunshot(origin, weapon.AiDetectionRange, except: view);
        return true;
    }

    /// <summary>
    /// Where a guard's round really goes: along the gun, with the weapon's cone about it — <c>0x478BA0</c>,
    /// the same Gun_Fire the player's rounds come out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retail's round leaves along the firer's own orientation (the matrix its fire message carries, or the
    /// gun's), not along a line to the target, and is then turned by rand³ × the weapon's spread about a
    /// random bearing -- the minimum spread for the first four rounds of a pull, the maximum after. A guard
    /// only aims by turning its body and view onto the point (to within 2 degrees before it opens fire), so
    /// those two errors are all the accuracy it has.
    /// </para>
    /// <para>
    /// Measured on the retail game with the probe (mission 1, Hard, a stationary guard at 15-22 m): 242
    /// rounds passed 0-3.5 m from the player and 7 hit -- about 3%. Casting every round exactly at the aim
    /// point and leaving only the probability roll made 107 of 181 hit.
    /// </para>
    /// </remarks>
    public Vector3d SpreadRound(
        Igi1HumanAi brain,
        AiStationaryGun? mounted,
        WeaponType weapon,
        SoldierAgent? member,
        Vector3d origin,
        Vector3d target)
    {
        double reach = Math.Max(Vector3d.Distance(origin, target), WorldScene.UnitsPerMetre);

        // The emplacement's barrel is aimed by its own task; a carried gun points where the soldier looks.
        Vector3f forward;
        Vector3f right;
        Vector3f up;
        if (mounted is not null)
        {
            Vector3d line = target - origin;
            forward = new Vector3f((float)(line.X / reach), (float)(line.Y / reach), (float)(line.Z / reach)).Normalized();
            right = Vector3f.Cross(forward, new Vector3f(0f, 0f, 1f)).Normalized();
            up = Vector3f.Cross(right, forward).Normalized();
        }
        else
        {
            Matrix3f view = brain.ViewOrientation.Transposed();
            forward = ToFloat(view.Transform(new Vector3d(0.0, 1.0, 0.0)));
            right = ToFloat(view.Transform(new Vector3d(1.0, 0.0, 0.0)));
            up = ToFloat(view.Transform(new Vector3d(0.0, 0.0, 1.0)));
        }

        int roundsThisPull = member?.RoundsThisPull ?? 0;
        if (member is not null)
        {
            member.RoundsThisPull++;
        }

        SoldierSimulation.RoundSpreadTraced?.Invoke(brain, roundsThisPull, forward);

        float cone = GunFiring.ConeAngle(weapon, roundsThisPull);
        Vector3f direction = forward;
        if (cone != 0f)
        {
            Span<float> samples =
            [
                _context.AiRandom.NextSingle(),
                _context.AiRandom.NextSingle(),
                _context.AiRandom.NextSingle(),
                _context.AiRandom.NextSingle(),
            ];
            (float radius, float bearing) = GunFiring.SampleCone(cone, samples);
            direction = GunFiring.RetailConeDirection(forward, radius, bearing);
        }

        return new Vector3d(
            origin.X + (direction.X * reach),
            origin.Y + (direction.Y * reach),
            origin.Z + (direction.Z * reach));

        static Vector3f ToFloat(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }

    /// <summary>Lets a soldier's grenade go as its throw state ends, at the settings' own fly and fuse times.</summary>
    /// <param name="brain">The thrower.</param>
    /// <param name="target">Where it is thrown, the target's feet.</param>
    /// <param name="grenades">The pool it flies in.</param>
    // The lob: reach the target's feet in the settings' own fly time, and cut the fuse to the
    // settings' explode time. The velocity is the closed-form ballistic answer for the port's
    // per-tick integrator: v = d/T + g(T+1)/2 upward, which lands EXACTLY on the target after T
    // ticks of `v.z -= g; p += v`.
    public void ThrowAiGrenade(Igi1HumanAi brain, Vector3d target, GrenadeSimulation grenades)
    {
        AiCombatParameters? parameters = brain.CombatParameters;
        if (parameters is null)
        {
            return;
        }

        float flySeconds = parameters.GrenadeMinimumFlyTimeSeconds
            + ((float)_context.AiRandom.NextDouble() * parameters.GrenadeRandomFlyTimeSeconds);
        int flyTicks = Math.Max(1, (int)(flySeconds * 30f));

        Vector3d origin = brain.EyePosition();
        Vector3f velocity = new(
            (float)((target.X - origin.X) / flyTicks),
            (float)((target.Y - origin.Y) / flyTicks),
            (float)((target.Z - origin.Z) / flyTicks) + (GrenadePhysics.GravityPerTick * (flyTicks + 1) * 0.5f));

        float fuseSeconds = parameters.GrenadeMinimumExplodeTimeSeconds
            + ((float)_context.AiRandom.NextDouble() * parameters.GrenadeRandomExplodeTimeSeconds);
        int fuseTicks = fuseSeconds > 0f ? Math.Max(1, (int)(fuseSeconds * 30f)) : GrenadePhysics.FuseTicks;

        grenades.SpawnWithVelocity(origin, velocity, GrenadeExplosionType.Normal, fuseTicks);
    }
}

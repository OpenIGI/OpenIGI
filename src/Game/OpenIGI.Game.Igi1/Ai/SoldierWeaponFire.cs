using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

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

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises -- and another game's
        // humans which way it went.
        _noise.NotifyGunshot(origin, weapon.AiDetectionRange, except: view, line: Line(aim - origin, weapon.EffectiveRange * WorldScene.UnitsPerMetre));
        return true;
    }

    // A shot's way, as long as the gun reaches: what another game's humans read the line of fire by.
    private static Vector3f Line(Vector3d way, double reach)
    {
        double length = way.Length;
        return length > 0.0
            ? new Vector3f((float)(way.X / length * reach), (float)(way.Y / length * reach), (float)(way.Z / length * reach))
            : default;
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

    /// <summary>
    /// Lets a soldier's grenade go at its throw clip's release frame — <c>Soldier_ThrowUpdate</c> (<c>0x45B2F0</c>) into
    /// <c>AI_AimThrow</c> (<c>0x452930</c>) and <c>Grenade_Throw</c> (<c>0x477040</c>).
    /// </summary>
    /// <param name="brain">The thrower.</param>
    /// <param name="hand">Where it leaves the hand: the origin plus bone 16's palette offset (<c>motion+0x858</c>).</param>
    /// <param name="grenades">The pool it flies in.</param>
    /// <param name="orientation">The thrower's frame, which <c>AI_AimThrow</c> tips up to the target for the grenade's own.</param>
    /// <remarks>
    /// Two draws from the level's stream, in this order: the flight time in seconds (<c>+15784</c> plus a share of
    /// <c>+15788</c>) and the cook (<c>fistp(rand * +15796 * 30 + +15792)</c> -- the minimum not made ticks). The throw
    /// aims at the target's origin raised (<c>0x451380</c>), a tenth past it: <c>delta * 1.1 / (time * 30)</c> a tick,
    /// with <c>time * 30 * 22.30044</c> -- half the fall a tick, times the ticks -- added upward. The cook comes off the
    /// grenade's own fuse.
    /// </remarks>
    public void ThrowAiGrenade(Igi1HumanAi brain, Vector3d hand, GrenadeSimulation grenades, Matrix3f orientation)
    {
        AiCombatParameters? parameters = brain.CombatParameters;
        if (parameters is null)
        {
            return;
        }

        float flySeconds = (float)(_context.AiRandom.NextDouble() * parameters.GrenadeRandomFlyTimeSeconds)
            + parameters.GrenadeMinimumFlyTimeSeconds;
        int cooked = (int)((_context.AiRandom.NextDouble() * parameters.GrenadeRandomExplodeTimeSeconds * 30.0)
            + parameters.GrenadeMinimumExplodeTimeSeconds);

        Vector3d target = brain.TargetAimPoint;
        float frames = flySeconds * 30f;
        Vector3f velocity = new(
            (float)((target.X - hand.X) * ThrowAhead / frames),
            (float)((target.Y - hand.Y) * ThrowAhead / frames),
            (float)(((target.Z - hand.Z) * ThrowAhead / frames) + (frames * ThrowLift)));

        int fuseTicks = Math.Max(0, GrenadePhysics.FuseTicks - cooked);
        grenades.SpawnWithVelocity(
            hand, velocity, GrenadeExplosionType.Normal, fuseTicks, orientation: AimThrowFrame(orientation, target - hand));
    }

    /// <summary>
    /// <c>AI_AimThrow</c>'s matrix (<c>0x452930</c>): the thrower's frame times a turn about its x axis by the pitch of the
    /// target in that frame (<c>0x4148F0</c>: the y/z part's angle from +y, negative below).
    /// </summary>
    /// <param name="m">The thrower's frame, row by row.</param>
    /// <param name="delta">The target less the hand.</param>
    /// <returns>The grenade's frame.</returns>
    internal static Matrix3f AimThrowFrame(Matrix3f m, Vector3d delta)
    {
        double vy = (m.M22 * delta.Y) + (m.M32 * delta.Z) + (m.M12 * delta.X);
        double vz = (m.M23 * delta.Y) + (m.M33 * delta.Z) + (m.M13 * delta.X);
        float pitch = 0f;
        double length = Math.Sqrt((vy * vy) + (vz * vz));
        if (length != 0.0)
        {
            double angle = Math.Acos(vy / length);
            pitch = (float)(vz / length >= 0.0 ? angle : -angle);
        }

        float c = (float)Math.Cos(pitch);
        float s = (float)Math.Sin(pitch);

        // Matrix_SetRotX (0x4B3B60): rows (1,0,0), (0,c,-s), (0,s,c); the product m x r, row by row.
        return new Matrix3f(
            m.M11, (m.M12 * c) + (m.M13 * s), (m.M13 * c) - (m.M12 * s),
            m.M21, (m.M22 * c) + (m.M23 * s), (m.M23 * c) - (m.M22 * s),
            m.M31, (m.M32 * c) + (m.M33 * s), (m.M33 * c) - (m.M32 * s));
    }

    // AI_AimThrow's 1.1: the throw is aimed a tenth past the target.
    private const double ThrowAhead = 1.1;

    // AI_AimThrow's lift a frame of flight, 0x4036_4CE9_ED57_275E: half the grenade's fall a tick.
    private const double ThrowLift = 22.300444444444445;
}

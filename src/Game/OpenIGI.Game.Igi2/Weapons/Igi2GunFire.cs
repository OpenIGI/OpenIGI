using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a <c>Gun</c> does when fired and let go: <c>Gun_WeaponFireHandler</c> (<c>igi2 0x482740</c>) and <c>Gun_WeaponReleaseHandler</c> (<c>0x483490</c>).</summary>
/// <remarks>
/// <para>
/// A round is the gun's bullets per round, or, counting bullets as rounds, no more than are loaded. Each bullet is a
/// projectile of the gun's projectile type; when a round has more than one, each is turned off the line of fire by two
/// draws from the run stream, an angle round the line and a distance out to the gun's spread. The gun reports one
/// round fired, or every bullet when bullets count as rounds, and a delay of its rounds per minute in ticks.
/// </para>
/// <para>
/// The kick is two more draws, up and sideways, scaled by how far into the pull the gun is. A loud gun (an AI detection
/// range past 20480) is heard far and keeps its holder visible for fifteen ticks; every shot tells the AI. Held by a
/// drawn weapon object and not through a scope, it flashes, lights its holder, and, with a casing model, throws out a
/// casing at a drawn angle with a drawn spin. The fire sound plays at the start of a pull: looped for full automatic, as a
/// sequence ending in the tail for a burst.
/// </para>
/// <para>
/// The kick's attack curve is the C runtime's <c>pow</c>, which the FPU's precision reaches inside; it is taken here in
/// double precision and truncated after. Nothing else in the handler depends on it.
/// </para>
/// </remarks>
public sealed class Igi2GunFire : IIgi2FireHandler
{
    /// <summary>The AI detection range past which a shot is heard far and lights its holder, <c>0x46A00000</c>.</summary>
    public const float LoudRange = 20480f;

    /// <summary>How far a loud shot is heard, <c>0x49160000</c>.</summary>
    public const float LoudEventRadius = 614400f;

    /// <summary>The ticks a loud shot keeps its holder visible whatever the light.</summary>
    public const int LoudVisibilityTicks = 15;

    /// <summary>The event a loud shot raises, heard far, <c>39</c>.</summary>
    public const int LoudShotEvent = 39;

    /// <summary>The event every shot raises, <c>15</c>.</summary>
    public const int ShotEvent = 15;

    /// <summary>The second event every shot raises, <c>23</c>.</summary>
    public const int ShotLineEvent = 23;

    private const float DegreesToRadians = 0.017453292f;

    /// <summary>The flash a shot lights its holder with, red to blue.</summary>
    private static readonly Vector3f HolderFlash = new(1f, 0.8f, 0.5f);

    private readonly Igi2WeaponTables _tables;

    /// <summary>Initializes a new instance of the <see cref="Igi2GunFire"/> class.</summary>
    /// <param name="tables">The session's ammunition, by number.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tables"/> is <see langword="null"/>.</exception>
    public Igi2GunFire(Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        _tables = tables;
    }

    /// <inheritdoc/>
    public bool HasRelease => true;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2GunTask gun = (Igi2GunTask)request.FireTypeParameters;
        if (gun.ProjectileTaskType == Igi2WeaponConstants.NoTaskType || request.Operator is not { } owner
            || request.Weapon.Inventory.Current is not { } active)
        {
            return Igi2FireResult.Nothing;
        }

        IgiRandom random = owner.Random;
        IIgi2WeaponEffects effects = request.Weapon.Effects;
        int mode = active.ActiveFireMode;
        int bullets = gun.CountBulletsPerRoundAsRounds ? Math.Min(LoadedForMode(active, mode, gun.BulletsPerRound), gun.BulletsPerRound) : gun.BulletsPerRound;

        float[] fired = request.Orientation;
        float[] original = [.. fired];
        Igi2AmmoTypeTask? ammo = _tables.Ammo(gun.AmmoTypeId);
        for (int i = 0; i < bullets; i++)
        {
            if (bullets > 1)
            {
                Spread(random, gun.BulletsPerRoundSpreadDegrees, original, fired);
            }

            request.Weapon.CreateProjectile(
                new Igi2ProjectileLaunch(
                    gun.ProjectileTaskType, request.Position, [.. fired], request.ParentSpeed, gun.MuzzleVelocityMps,
                    gun.EffectiveRangeMeters, request.HeldTime, request.WeaponType, ammo),
                gun);
        }

        original.CopyTo(fired, 0);
        int reported = gun.CountBulletsPerRoundAsRounds ? bullets : 1;
        int delay = Fpu.ToInt32(((((Fpu)60f / (double)gun.RoundsPerMinute) * 30f) + 0.5f));
        (float alpha, float gamma) = Kickback(random, gun, request.RoundsFired);

        RaiseEvents(gun, request, owner, effects);
        ShowShot(gun, request, owner, effects, random);
        PlayFireSound(gun, request.WeaponType, mode, request.HeldTime, effects);
        return new Igi2FireResult(reported, delay, gun.KickbackDampingFactor, gun.CrosshairDampingFactor, alpha, gamma);
    }

    /// <inheritdoc/>
    /// <remarks>A full-automatic loop is cut off by the tail sound; nothing is fired.</remarks>
    public Igi2FireResult Release(in Igi2FireRequest request)
    {
        Igi2GunTask gun = (Igi2GunTask)request.FireTypeParameters;
        int mode = request.Weapon.Inventory.Current?.ActiveFireMode ?? 0;
        if (gun.IsFireSoundLooped(mode) && request.WeaponType.RoundsInSuccession(mode) == gun.RoundsPerClip)
        {
            request.Weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, gun.FireSound2, looped: false);
        }

        return default;
    }

    /// <summary>
    /// The rounds loaded the gun counts against, <c>anFireTypeAmmoCount[iActiveFireMode]</c>: the array is indexed by the
    /// fire mode, not the fire type, and the third mode reads past it into the item's weapon pointer, which no round
    /// count reaches.
    /// </summary>
    private static int LoadedForMode(Igi2WeaponItem item, int mode, int bulletsPerRound) =>
        mode < Igi2WeaponTypeTask.FireTypeSlots ? item.Loaded(mode) : bulletsPerRound;

    /// <summary>
    /// Turns one bullet off the line of fire: an angle round it of a whole turn by one draw, out to the tangent of the
    /// spread by another. The new forward axis is the original orientation's rows dotted with that direction, and the
    /// other two axes are rebuilt by cross products against the original up axis, each normalised unless zero.
    /// </summary>
    private static void Spread(IgiRandom random, float spreadDegrees, float[] o, float[] into)
    {
        Fpu theta = (Fpu)random.NextDouble() * 6.283185307179589;
        Fpu tangent = Math.Tan(((Fpu)spreadDegrees * DegreesToRadians).Value);
        Fpu radius = tangent * random.NextDouble();
        float across = (Fpu.Sin(theta) * radius).F;
        Fpu up = Fpu.Cos(theta) * radius;
        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)across * across) + (up * up)) + 1.0);
        float vx = ((Fpu)across * inverse).F;
        float vy = inverse.F;
        Fpu vz = up * inverse;

        float fx = ((((Fpu)o[1] * vy) + ((Fpu)o[0] * vx)) + ((Fpu)o[2] * vz)).F;
        float fy = ((((Fpu)o[4] * vy) + ((Fpu)o[3] * vx)) + ((Fpu)o[5] * vz)).F;
        float fz = ((((Fpu)o[7] * vy) + ((Fpu)o[6] * vx)) + ((Fpu)o[8] * vz)).F;

        float xx = (((Fpu)fy * o[8]) - ((Fpu)fz * o[5])).F;
        float xy = (-(((Fpu)fx * o[8]) - ((Fpu)fz * o[2]))).F;
        float xz = (((Fpu)fx * o[5]) - ((Fpu)fy * o[2])).F;

        float zx = (((Fpu)xy * fz) - ((Fpu)xz * fy)).F;
        float zy = (-(((Fpu)xx * fz) - ((Fpu)xz * fx))).F;
        float zz = (((Fpu)xx * fy) - ((Fpu)xy * fx)).F;

        (float X, float Y, float Z) right = Normalised(xx, xy, xz, static (x, y, z) => ((((Fpu)y * y) + ((Fpu)z * z)) + ((Fpu)x * x)));
        (float X, float Y, float Z) forward = Normalised(fx, fy, fz, static (x, y, z) => ((((Fpu)x * x) + ((Fpu)y * y)) + ((Fpu)z * z)));
        (float X, float Y, float Z) upward = Normalised(zx, zy, zz, static (x, y, z) => ((((Fpu)x * x) + ((Fpu)y * y)) + ((Fpu)z * z)));

        into[0] = right.X;
        into[1] = forward.X;
        into[2] = upward.X;
        into[3] = right.Y;
        into[4] = forward.Y;
        into[5] = upward.Y;
        into[6] = right.Z;
        into[7] = forward.Z;
        into[8] = upward.Z;
    }

    /// <summary>A vector over its length, left alone when all zero: each component times one over the root, stored.</summary>
    private static (float X, float Y, float Z) Normalised(float x, float y, float z, Func<float, float, float, Fpu> squares)
    {
        if (x == 0f && y == 0f && z == 0f)
        {
            return (x, y, z);
        }

        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(squares(x, y, z));
        return ((inverse * x).F, (inverse * y).F, (inverse * z).F);
    }

    /// <summary>
    /// The kick, up and then sideways: a draw across each range less the size of its minimum, held within the range and
    /// scaled by the attack: the rounds fired over the attack time, at most one, to the acceleration, plus the floor,
    /// held within nought and one.
    /// </summary>
    private static (float Alpha, float Gamma) Kickback(IgiRandom random, Igi2GunTask gun, int roundsFired)
    {
        Fpu alphaRange = (Fpu)gun.MaxAlphaKickbackDegrees - gun.MinAlphaKickbackDegrees;
        float gammaRange = ((Fpu)gun.MaxGammaKickbackDegrees - gun.MinGammaKickbackDegrees).F;
        float alpha = (((Fpu)random.NextDouble() * alphaRange) - Math.Abs(gun.MinAlphaKickbackDegrees)).F;
        float gamma = (((Fpu)random.NextDouble() * gammaRange) - Math.Abs(gun.MinGammaKickbackDegrees)).F;

        Fpu attack = (Fpu)(double)roundsFired / gun.KickbackFactorAttackTime;
        float progress = 1.0 < attack.Value ? 1f : attack.F;
        Fpu factor = (Fpu)Math.Pow(progress, gun.KickbackFactorAcceleration) + gun.KickbackFactorFloor;
        if (factor.Value < 0.0)
        {
            factor = 0.0;
        }
        else if (factor.Value > 1.0)
        {
            factor = 1.0;
        }

        return (((Fpu)Clamp(alpha, gun.MinAlphaKickbackDegrees, gun.MaxAlphaKickbackDegrees) * factor).F,
            ((Fpu)Clamp(gamma, gun.MinGammaKickbackDegrees, gun.MaxGammaKickbackDegrees) * factor).F);

        static float Clamp(float value, float min, float max) => !(min <= value) ? min : max < value ? max : value;
    }

    /// <summary>
    /// Tells the AI: a loud shot is heard far, and every shot raises two events at the gun's own range, all pointing down
    /// the line of fire to the gun's effective range.
    /// </summary>
    private static void RaiseEvents(Igi2GunTask gun, in Igi2FireRequest request, IIgi2WeaponOwner owner, IIgi2WeaponEffects effects)
    {
        float[] o = request.Orientation;
        Fpu reach = (Fpu)gun.EffectiveRangeMeters * 4096f;
        Vector3f line = new(
            (((((Fpu)o[2] + o[0]) * 0f) + (reach * o[1]))).F,
            (((((Fpu)o[5] + o[3]) * 0f) + (reach * o[4]))).F,
            (((((Fpu)o[8] + o[6]) * 0f) + (reach * o[7]))).F);
        float range = gun.AIdetectionRange;
        if (range > LoudRange)
        {
            effects.SendAiEvent(new Igi2WeaponAiEvent(LoudShotEvent, 0x2C2, LoudEventRadius, request.Position, line, owner.Team, owner, owner));
        }

        effects.SendAiEvent(new Igi2WeaponAiEvent(ShotEvent, 0x442, range, request.Position, line, owner.Team, owner, owner));
        effects.SendAiEvent(new Igi2WeaponAiEvent(ShotLineEvent, 0x522, range, request.Position, line, owner.Team, owner, owner));
        if (range > LoudRange)
        {
            owner.IgnoreLightOnVisibility(LoudVisibilityTicks);
        }
    }

    /// <summary>
    /// The flash and the casing, from the holder's weapon object the bullets fired pick, both only when the holder has
    /// one and is not looking through a scope. The casing is three run-stream draws more, made when it is made: one
    /// bounded draw for its angle and three raw draws for its spin.
    /// </summary>
    private static void ShowShot(Igi2GunTask gun, in Igi2FireRequest request, IIgi2WeaponOwner owner, IIgi2WeaponEffects effects, IgiRandom random)
    {
        int objects = owner.WeaponObjectCount();
        if (objects == 0 || owner.IsZoomMode)
        {
            return;
        }

        int weaponObject = Math.Min(request.Weapon.Inventory.TotalBulletsFired % 2, objects - 1);
        owner.Flash(HolderFlash);
        effects.MuzzleFlash(weaponObject, gun.MuzzleFlashFrontSprite.Length == 0 ? null : gun.MuzzleFlashFrontSprite, gun.MuzzleFlashSize);
        if (gun.BulletCasingModel.Length == 0)
        {
            return;
        }

        bool isGun = request.WeaponType.WeaponType == Igi2WeaponTypeKind.Gun;
        int degrees = (int)random.NextBounded(20) - 10 + (isGun ? 30 : 60);
        float angle = ((Fpu)(double)degrees * DegreesToRadians).F;
        uint first = random.NextRaw();
        uint second = random.NextRaw();
        uint third = random.NextRaw();
        effects.EjectCasing(new Igi2CasingEjection(
            weaponObject, gun.BulletCasingModel, angle, isGun, ((int)(first & 1), (int)(second % 5), (int)(third % 5))));
    }

    /// <summary>The fire sound, only at the start of a pull: a loop for full automatic, a sequence for a burst, once otherwise.</summary>
    private static void PlayFireSound(Igi2GunTask gun, Igi2WeaponTypeTask weaponType, int mode, int heldTime, IIgi2WeaponEffects effects)
    {
        if (heldTime != 0)
        {
            return;
        }

        string sound = gun.FireSound(mode);
        if (!gun.IsFireSoundLooped(mode))
        {
            effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, sound, looped: false);
        }
        else if (weaponType.RoundsInSuccession(mode) == gun.RoundsPerClip)
        {
            effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, sound, looped: true);
        }
        else
        {
            effects.ClearSoundSequence();
            effects.AddSoundToSequence(sound, weaponType.FireSoundStopTime(mode));
            effects.AddSoundToSequence(gun.FireSound2, -1f);
        }
    }
}

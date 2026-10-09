using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Weapons;

/// <summary>One projectile a round produced: where it starts and which way it goes.</summary>
/// <param name="Origin">The muzzle, in engine units — the eye, as <c>0x462E50</c> supplies it.</param>
/// <param name="Direction">A unit vector, the barrel rotated by this projectile's cone sample.</param>
/// <param name="RangeInUnits">How far it may travel, from <see cref="WeaponType.EffectiveRange"/>.</param>
/// <param name="SpeedMetresPerSecond">The launch speed in metres per second.</param>
public readonly record struct GunProjectile(
    Vector3d Origin,
    Vector3f Direction,
    float RangeInUnits,
    float SpeedMetresPerSecond = 0f)
{
    /// <summary>Gets the launch velocity in engine units per simulation tick.</summary>
    public Vector3f VelocityUnitsPerTick =>
        Direction * (SpeedMetresPerSecond * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond);
}

/// <summary>What one trigger message did.</summary>
/// <param name="Fired">Whether a round went off.</param>
/// <param name="Block">What stopped it, when it did not.</param>
/// <param name="RecoilX">The view kick in pitch, in radians, for this round.</param>
/// <param name="RecoilZ">The view kick in yaw, in radians, for this round.</param>
/// <param name="Projectiles">
/// One per <see cref="WeaponType.BulletsPerRound"/>, empty when nothing was fired.
/// </param>
public readonly record struct GunFireResult(
    bool Fired,
    GunFireBlock Block,
    float RecoilX,
    float RecoilZ,
    IReadOnlyList<GunProjectile> Projectiles);

/// <summary>
/// A weapon in someone's hands: the roster record, the magazine, and the clock between rounds.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's 432-byte <c>Gun</c> task, reduced to the fields the firing path touches. Its
/// two entry points are the per-tick <c>0x478640</c> and the message the human sends every update,
/// which the class binds to <c>0x479780</c> — <b>not</b> to <c>Gun_Fire</c> directly. <c>0x479780</c>
/// calls <c>Gun_Fire</c> (<c>0x478BA0</c>) and then, only if a round went off, samples the view recoil.
/// </para>
/// <para>
/// <b>The human never decides when a round goes off.</b> The weapon state machine raises a byte every
/// tick the trigger is down and this class answers on the ticks
/// its own cooldown allows. Rate of fire, burst length and the magazine all live here.
/// </para>
/// </remarks>
public sealed class HeldGun
{
    private static readonly IReadOnlyList<GunProjectile> NoProjectiles = [];

    private readonly List<GunProjectile> _projectiles = [];

    /// <summary>Initializes a new instance of the <see cref="HeldGun"/> class.</summary>
    /// <param name="weapon">The roster record this gun is.</param>
    /// <param name="ammo">
    /// The ammunition record the weapon's calibre resolves to, or <see langword="null"/> when it names
    /// none. The knife and the emplacement guns name none.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    public HeldGun(WeaponType weapon, AmmoType? ammo = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        Weapon = weapon;
        Ammo = ammo;
    }

    /// <summary>Gets the roster record.</summary>
    public WeaponType Weapon { get; }

    /// <summary>Gets the ammunition record, or <see langword="null"/> when the weapon names none.</summary>
    /// <remarks>
    /// The original caches this at <c>+300</c> and guards three separate things on it: the magazine is
    /// only tested when it is present, only decremented when it is present, and the reserve is only
    /// looked up when it is present. So a weapon with no record — the knife, whose <c>Calibre ID</c> is
    /// <c>-1</c> — never runs out and never spends anything.
    /// </remarks>
    public AmmoType? Ammo { get; }

    /// <summary>Gets whether this weapon draws from a magazine at all.</summary>
    public bool ConsumesAmmunition => Ammo is not null;

    /// <summary>Gets or sets whether a fired round is left in the magazine — the infinite-ammunition cheat.</summary>
    public bool PreserveMagazine { get; set; }

    /// <summary>Gets whether a fired round actually leaves the magazine.</summary>
    public bool DrawsFromMagazine => ConsumesAmmunition && !PreserveMagazine;

    /// <summary>Gets the mutable half — the magazine, the cooldown and the burst counter.</summary>
    public GunState State { get; } = new();

    /// <summary>Gets how many rounds are in the magazine.</summary>
    public int RoundsInClip => State.RoundsInClip;

    /// <summary>Gets how many rounds are loose, out of a pool.</summary>
    /// <param name="pool">What the owner is carrying.</param>
    /// <returns>The reserve, or zero when this weapon draws from no pool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <remarks><c>0x479AA0</c>'s second output.</remarks>
    public int Reserve(AmmoPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (Ammo is not null)
        {
            return pool.Count(Ammo.AmmoId);
        }

        // Medipack is the one special weapon whose native ammo slot is fixed even when a
        // partially-loaded roster leaves the cached binding unavailable.
        return Weapon.Category == WeaponCategory.Medipack
            ? pool.Count(PlayerWeapons.MedipackAmmoId)
            : 0;
    }

    /// <summary>Advances the gun's clock by one tick.</summary>
    /// <param name="triggerHeld">Whether the trigger is down this tick.</param>
    /// <remarks>
    /// <c>0x478640</c>. Its first act is to load the magazine if it never has been, which is what turns
    /// <see cref="GunTiming.UnloadedClipSentinel"/> into the weapon's capacity.
    /// </remarks>
    public void Tick(bool triggerHeld)
    {
        // Medipacks are inventory charges, not firearms. Their first charge is written by PickUp;
        // treating the unloaded sentinel as a magazine here would manufacture a usable pack for a
        // merely equipped weapon and would make the reserve pool off by one.
        if (Weapon.Category != WeaponCategory.Medipack)
        {
            State.Tick(Weapon, triggerHeld);
        }
    }

    /// <summary>Refills the magazine from a pool.</summary>
    /// <param name="pool">What the owner is carrying.</param>
    /// <returns>How many rounds moved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>0x4798C0</c>. <b>Rounds already in the magazine are kept</b> — the transfer is
    /// <c>Rounds/Clip − clip</c>, capped at what the pool holds, so reloading with eight of seventeen
    /// left takes nine. There is no lost-partial-magazine rule anywhere in this game.
    /// </remarks>
    public int Reload(AmmoPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (Ammo is null)
        {
            return 0;
        }

        State.EnsureLoaded(Weapon);

        int need = Weapon.RoundsPerClip - State.RoundsInClip;
        if (need <= 0)
        {
            return 0;
        }

        int taken = pool.Take(Ammo.AmmoId, need);
        State.RoundsInClip += taken;
        return taken;
    }

    /// <summary>Sends the gun one tick's trigger message.</summary>
    /// <param name="triggerHeld">Whether the human asked for a round this tick.</param>
    /// <param name="origin">The muzzle, in engine units.</param>
    /// <param name="forward">The barrel's direction — the view's <c>+Y</c> axis, already unit length.</param>
    /// <param name="right">The view's <c>+X</c> axis, which the cone is opened across.</param>
    /// <param name="up">The view's <c>+Z</c> axis, the cone's other axis.</param>
    /// <param name="random">The stream every sample is drawn from, in order.</param>
    /// <param name="speedOverrideMetresPerSecond">Optional charged-throw speed; otherwise the weapon speed is used.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This is <c>0x479780</c> around <c>0x478BA0</c>, in the original's order: the gate, then one cone
    /// sample per <see cref="WeaponType.BulletsPerRound"/>, then the round's bookkeeping, then — only
    /// when a round actually went off — the recoil.
    /// </para>
    /// <para>
    /// <b>The draw order is part of the behaviour.</b> A projectile takes four draws (three multiplied
    /// into the radius, one for the bearing) and the recoil takes an unbounded even number, because it
    /// rejection-samples a unit disc two draws at a time. Reproducing a shot means reproducing that.
    /// </para>
    /// </remarks>
    public GunFireResult Fire(
        bool triggerHeld,
        Vector3d origin,
        Vector3f forward,
        Vector3f right,
        Vector3f up,
        IgiRandom random,
        float? speedOverrideMetresPerSecond = null)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Weapon.Category != WeaponCategory.Medipack)
        {
            State.EnsureLoaded(Weapon);
        }

        GunFireBlock block = GunFiring.Evaluate(Weapon, State, triggerHeld, ConsumesAmmunition);
        if (!triggerHeld)
        {
            // The release is what resets the burst, and it happens inside the same call in the
            // original -- before the burst test, which is why letting go and pulling again fires.
            State.RoundsThisBurst = 0;
        }

        if (block != GunFireBlock.None)
        {
            return new GunFireResult(false, block, 0f, 0f, NoProjectiles);
        }

        _projectiles.Clear();

        float cone = GunFiring.ConeAngle(Weapon, State.RoundsThisBurst);
        float range = GunShotBallistics.RangeInUnits(Weapon);

        for (int i = 0; i < Weapon.BulletsPerRound; i++)
        {
            // A cone of exactly zero skips the sampling entirely and fires down the barrel -- and it
            // draws nothing, which is why the knife and every emplacement gun consume no randomness.
            Vector3f direction = forward;
            if (cone != 0f)
            {
                Span<float> samples =
                [
                    random.NextSingle(),
                    random.NextSingle(),
                    random.NextSingle(),
                    random.NextSingle(),
                ];

                (float radius, float bearing) = GunFiring.SampleCone(cone, samples);
                direction = GunFiring.RetailConeDirection(forward, radius, bearing);
            }

            _projectiles.Add(new GunProjectile(
                origin,
                direction,
                range,
                speedOverrideMetresPerSecond ?? Weapon.MuzzleVelocity));
        }

        State.OnRoundFired(Weapon, DrawsFromMagazine);

        (float recoilX, float recoilZ) = SampleRecoil(Weapon, random);
        return new GunFireResult(true, GunFireBlock.None, recoilX, recoilZ, _projectiles.ToArray());
    }

    /// <summary>Samples one round's view kick.</summary>
    /// <param name="weapon">The weapon fired.</param>
    /// <param name="random">The stream, which this consumes an unbounded even number of draws from.</param>
    /// <returns>The pitch and yaw kick, in radians.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>0x479780</c>'s second half. The random part is drawn from a <b>disc</b>, by rejecting pairs
    /// outside the unit circle, so the two components are correlated: a shot that kicks hard upward
    /// cannot also kick hard sideways. Two independent axis samples would be a squarer, and wrong,
    /// distribution.
    /// </para>
    /// <para>
    /// The fixed part is added on top, so a weapon with no random terms — the Glock — kicks by exactly
    /// its <see cref="WeaponType.FixedRecoilX"/> every time.
    /// </para>
    /// </remarks>
    public static (float X, float Z) SampleRecoil(WeaponType weapon, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(random);

        float x;
        float y;
        do
        {
            // Spelled as the original does -- (r - 0.5) + (r - 0.5), not 2 * (r - 0.5).
            float first = random.NextSingle();
            x = (first - 0.5f) + (first - 0.5f);
            float second = random.NextSingle();
            y = (second - 0.5f) + (second - 0.5f);
        }
        while (MathF.Sqrt((y * y) + (x * x)) > 1f);

        const float d2r = GunFiring.DegreesToRadians;
        return (
            (weapon.RandomRecoilX * d2r * x) + (weapon.FixedRecoilX * d2r),
            (weapon.RandomRecoilZ * d2r * y) + (weapon.FixedRecoilZ * d2r));
    }

}

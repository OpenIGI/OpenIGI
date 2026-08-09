namespace OpenIGI.Game.Weapons.Ballistics;

/// <summary>
/// How one weapon's rounds fly when the tactical-ballistics option (<c>g_ballistics</c>) is on.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is remaster behaviour, not the original's.</b> Retail resolves every round as a hit-scan
/// ray in the tick it is fired (<see cref="GunShotBallistics"/>); this profile is what turns the same
/// weapon record into a projectile that takes time to arrive, drops under gravity and slows in air.
/// Nothing here is read when the option is off.
/// </para>
/// <para>
/// Every number starts from data the retail tables already carry and the bullet never used:
/// <see cref="WeaponType.MuzzleVelocity"/> (metres per second, which <c>GunShotBallistics</c> notes
/// is "carried for the HUD and the AI, not for the bullet"), <see cref="AmmoType.MassInGrams"/>
/// (whose original consumer was never found), and the calibre's own diameter. That is what makes the
/// flight per-gun rather than per-setting: a 7.62 round is heavy for its face and carries; a shotgun
/// pellet is all face and falls off.
/// </para>
/// <para>
/// <b>The table lies in three places, and each has a named correction here.</b> Read against the
/// real roster (the port's own dump of a retail installation): the three rocket carriers are
/// category <c>Gun</c> with a 5 m/s muzzle velocity — their real projectile is the missile task —
/// so <see cref="MinimumMetresPerSecond"/> keeps them hit-scan. The Dragunov says 6000 m/s, a
/// hit-scan era convenience no rifle reaches, so <see cref="MaximumMetresPerSecond"/> caps it. And
/// most ammunition masses are placeholders (9×19 at 3.5 g, the 12.7 at 2 g — only the 7.62's 12 g
/// is a measurement), so the drag mass is floored at a lead slug of the calibre's own diameter
/// rather than trusted raw; see <see cref="MassForDragInGrams"/>.
/// </para>
/// </remarks>
/// <param name="UnitsPerSecond">The muzzle speed, in world units per second.</param>
/// <param name="DragPerUnit">
/// The quadratic drag constant <c>k</c> in reciprocal world units, so the deceleration in units/s² is
/// <c>k · v²</c> with <c>v</c> in units/s.
/// </param>
public readonly record struct BallisticProfile(float UnitsPerSecond, float DragPerUnit)
{
    /// <summary>Air density at sea level, in kg/m³.</summary>
    public const float AirDensity = 1.225f;

    /// <summary>The drag coefficient every round shares.</summary>
    /// <remarks>
    /// A supersonic spitzer bullet sits near 0.29 against the G1 reference shape. Per-round shape is
    /// deliberately <i>not</i> modelled — the per-gun character comes from diameter against mass,
    /// which the retail tables actually supply, rather than from coefficients that would have to be
    /// invented per weapon.
    /// </remarks>
    public const float DragCoefficient = 0.29f;

    /// <summary>The slowest muzzle velocity that still builds a profile, in m/s.</summary>
    /// <remarks>
    /// The rocket carriers (RPG-18, APC, T80) are category <c>Gun</c> in the table but carry 5 m/s —
    /// their round is really the missile task, which flies its own way. Below this they stay
    /// hit-scan; every genuine gun in the roster ships at 280 or above.
    /// </remarks>
    public const float MinimumMetresPerSecond = 100f;

    /// <summary>The fastest muzzle velocity a profile flies at, in m/s.</summary>
    /// <remarks>
    /// Only the Dragunov's 6000 hits this — a number that made sense when the round was a ray. 1500
    /// leaves every other roster value untouched (the Minimi's 1200 is the next fastest) and keeps
    /// the sniper's round supersonic without being a laser.
    /// </remarks>
    public const float MaximumMetresPerSecond = 1500f;

    /// <summary>The density of lead, in kg/m³, for the mass floor.</summary>
    public const float LeadDensity = 11340f;

    /// <summary>Builds the profile for a weapon, or refuses.</summary>
    /// <param name="weapon">The roster record.</param>
    /// <param name="ammo">The ammunition record its calibre resolves to, when it names one.</param>
    /// <returns>
    /// The profile, or <see langword="null"/> when this weapon stays hit-scan even with the option
    /// on: anything that is not a gun (the knife, thrown and placed weapons, the medipack — grenades
    /// and rockets already fly through <see cref="MissileFlight"/>), and any gun below
    /// <see cref="MinimumMetresPerSecond"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    public static BallisticProfile? TryCreate(WeaponType weapon, AmmoType? ammo)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        bool fires = weapon.Category is WeaponCategory.Gun or WeaponCategory.Pistol or WeaponCategory.Shotgun;
        if (!fires || weapon.MuzzleVelocity < MinimumMetresPerSecond)
        {
            return null;
        }

        float metresPerSecond = Math.Min(weapon.MuzzleVelocity, MaximumMetresPerSecond);
        return new BallisticProfile(
            metresPerSecond * GunShotBallistics.UnitsPerMetre,
            DragConstantPerUnit(weapon.CalibreId, ammo));
    }

    /// <summary>The diameter of what actually flies, in millimetres.</summary>
    /// <param name="calibreId">A <see cref="AmmoType.CalibreId"/> value.</param>
    /// <returns>The diameter; 9 mm for a calibre the table does not name.</returns>
    /// <remarks>
    /// The ids are the retail numbering (see <see cref="AmmoType.CalibreId"/>). Two are not simply
    /// the name read as millimetres: calibre <c>12</c> is a gauge, and what flies is a buckshot
    /// pellet of about 8.4 mm, not the 18.5 mm bore; and <c>10004</c> is the Dragunov's special
    /// type, whose real cartridge is the 7.62×54R.
    /// </remarks>
    public static float DiameterInMillimetres(int calibreId) => calibreId switch
    {
        12 => 8.4f,
        44 => 10.9f,
        127 => 12.7f,
        357 => 9.1f,
        556 => 5.7f,
        762 => 7.85f,
        919 => 9.0f,
        1000 => 9.0f,
        10004 => 7.85f,
        _ => 9.0f,
    };

    /// <summary>The mass the drag divides by, in grams.</summary>
    /// <param name="calibreId">The calibre.</param>
    /// <param name="tableMassInGrams">What the ammunition table says one round weighs.</param>
    /// <returns>The table's mass, floored at a lead slug of the calibre's own diameter.</returns>
    /// <remarks>
    /// The table's masses are mostly placeholders — 3.5 g for every pistol calibre, 2 g for the
    /// 12.7 — and dividing a .50's face by two grams would strip its speed in a hundred metres. No
    /// bullet is lighter than lead of its own calibre: the floor is a lead sphere of the flying
    /// diameter, doubled for everything but shot because a bullet is elongated where a pellet really
    /// is a sphere. The floor only ever raises a placeholder — the one measured mass in the table,
    /// the 7.62's 12 g, sits above its own floor and is used as shipped.
    /// </remarks>
    public static float MassForDragInGrams(int calibreId, float tableMassInGrams)
    {
        float radiusMetres = DiameterInMillimetres(calibreId) * 0.001f * 0.5f;
        float sphereGrams = LeadDensity * (4f / 3f) * MathF.PI * radiusMetres * radiusMetres * radiusMetres * 1000f;
        float floorGrams = calibreId == 12 ? sphereGrams : sphereGrams * 2f;
        return Math.Max(tableMassInGrams, floorGrams);
    }

    // k = ½·ρ·C_d·A / m per metre, then divided by units-per-metre so it applies to a speed measured
    // in world units. A weapon naming no ammunition record still gets the calibre floor.
    private static float DragConstantPerUnit(int calibreId, AmmoType? ammo)
    {
        float radiusMetres = DiameterInMillimetres(calibreId) * 0.001f * 0.5f;
        float areaSquareMetres = MathF.PI * radiusMetres * radiusMetres;
        float massKilograms = MassForDragInGrams(calibreId, ammo?.MassInGrams ?? 0f) * 0.001f;

        float perMetre = 0.5f * AirDensity * DragCoefficient * areaSquareMetres / massKilograms;
        return perMetre / GunShotBallistics.UnitsPerMetre;
    }
}

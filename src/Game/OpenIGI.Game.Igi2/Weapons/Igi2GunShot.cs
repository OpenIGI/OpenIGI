using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// What a gun's bullets carry, <c>GunShotParameters</c> as <c>GunShot_CreateWeaponTypeParametersHandler</c>
/// (<c>igi2 0x4C77F0</c>) reads it from the gun's tagged values.
/// </summary>
/// <param name="DamageFactor">The damage a bullet does at its muzzle, the gun's <c>Damage Factor</c>; 1 when it names none.</param>
/// <param name="PenetrationPower">How much material it goes through, the gun's <c>Penetration Power</c>; 1 when it names none.</param>
public readonly record struct Igi2GunShotParameters(float DamageFactor, float PenetrationPower)
{
    /// <summary>The tag a bullet's damage is named by.</summary>
    public const string DamageFactorTag = "Damage Factor";

    /// <summary>The tag a bullet's penetration is named by.</summary>
    public const string PenetrationPowerTag = "Penetration Power";

    /// <summary>Reads a fire type's bullet parameters from its tagged values, <c>TagItem_Find</c> by name.</summary>
    /// <param name="fireType">The fire type, whose children carry the tags.</param>
    /// <returns>The parameters, with 1 for a tag it does not carry.</returns>
    public static Igi2GunShotParameters Of(GameTask fireType)
    {
        ArgumentNullException.ThrowIfNull(fireType);
        return new Igi2GunShotParameters(Tag(fireType, DamageFactorTag), Tag(fireType, PenetrationPowerTag));
    }

    private static float Tag(GameTask fireType, string name)
    {
        foreach (GameTask child in fireType.Children)
        {
            if (child is Igi2TagItemReal32Task tag && string.Equals(tag.TagName, name, StringComparison.Ordinal))
            {
                return tag.Value;
            }
        }

        return 1f;
    }
}

/// <summary>The ways a weapon's <c>GunShot</c> traces its bullets, its flags (<c>GunShot+0xAC</c>).</summary>
[Flags]
public enum Igi2GunShotOptions
{
    /// <summary>None: the gun's own range, through the ground, straight.</summary>
    None = 0,

    /// <summary>The gun's range is cut to the shot's range limit.</summary>
    LimitRange = 1,

    /// <summary>The ground does not stop the bullets.</summary>
    IgnoreTerrain = 2,

    /// <summary>A bullet falls a fifty-thousandth of its range over it.</summary>
    Drop = 4,
}

/// <summary>One bullet in flight, a <c>GunShot</c> instant (<c>GunShot_AddInstant</c>, <c>igi2 0x4C7980</c>): 72 bytes of the shot's fifteen.</summary>
public sealed class Igi2GunShotInstant
{
    /// <summary>Gets where the bullet leaves from.</summary>
    public required Vector3d Position { get; init; }

    /// <summary>Gets which way it flies, a unit vector.</summary>
    public required Vector3f Direction { get; init; }

    /// <summary>Gets how far it reaches, in world units: the gun's range in metres times 4096.</summary>
    public required float Range { get; init; }

    /// <summary>Gets the damage it does at its muzzle.</summary>
    public required float DamageFactor { get; init; }

    /// <summary>Gets or sets how much material it may still go through: each face it crosses takes its material's share.</summary>
    public required float Penetration { get; set; }

    /// <summary>Gets the weapon that fired it.</summary>
    public required Igi2WeaponTypeTask WeaponType { get; init; }

    /// <summary>Gets the ammunition, or <see langword="null"/>.</summary>
    public Igi2AmmoTypeTask? AmmoType { get; init; }
}

/// <summary>
/// One face a bullet crossed that leaves an effect, as <c>GunShot_RunInstant</c> (<c>igi2 0x4C6B50</c>) records it in
/// 152 bytes: the material's effect, a copy of the hit, the direction the effect is thrown in, the bullet's penetration
/// after the face, and whether it went on.
/// </summary>
/// <param name="Effect">The material's effect: its bullet-enter effect for a front face, its bullet-leave effect for a back face.</param>
/// <param name="Intersection">The hit, as the trace left it.</param>
/// <param name="Direction">The bullet's reflection off a front face; its own direction out of a back face.</param>
/// <param name="Penetration">What was left of its penetration.</param>
/// <param name="Continues">Whether it went on through the face.</param>
public sealed record Igi2GunShotHitRecord(Igi2MaterialEffect Effect, Igi2Intersection Intersection, Vector3f Direction, float Penetration, bool Continues);

/// <summary>
/// The shots a weapon fires as bullets, its <c>GunShot</c> task: fired this tick, traced this tick, gone by the next.
/// </summary>
/// <remarks>
/// <para>
/// <c>Weapon_CreateHandler</c> (<c>igi2 0x51C320</c>) makes one under every weapon. The gun's fire handler hands each
/// bullet to <see cref="CreateProjectile"/> (<c>GunShot_CreateProjectileHandler</c>, <c>0x4C78A0</c>), and the shot's
/// run handler, which the scheduler calls straight after the human's because it is the weapon's child, traces them all
/// in <see cref="Run"/> (<c>GunShot_RunHandler</c>, <c>0x4C76E0</c>; <c>GunShot_RunInstant</c>, <c>0x4C6B50</c>).
/// </para>
/// <para>
/// A bullet is traced from its muzzle to the end of its range against the forests, the solid models, the splines, the
/// humans and the ground, one face at a time. At a face it goes into it does its damage, falling off by up to 30% over
/// its range and in proportion to the penetration it has left, and hands the hit to what it struck; the ground stops it.
/// The first face that makes a sound for the whole tick also makes the AI hear the strike. Every face it crosses takes
/// the material's penetration power from it, and it stops when it has none left. A face it comes out of only costs it
/// penetration. Up to ten faces leave an effect, all made once the bullet is done.
/// </para>
/// <para>
/// Not yet: <c>Human_eEvaluateGunShotQTaskEvent</c>, which tells every human with an AI of a bullet passing within its
/// hearing before the trace, arrives with the AI.
/// </para>
/// </remarks>
public sealed class Igi2GunShot
{
    /// <summary>The bullets a shot holds at once, <c>15</c>; one fired past them is counted and lost.</summary>
    public const int MaxInstants = 15;

    /// <summary>The faces a bullet leaves an effect on by default, <c>GunShot+0x9C</c> (<c>GunShot_CreateHandler</c>, <c>igi2 0x4C7650</c>).</summary>
    public const int DefaultMaxEffects = 10;

    /// <summary>The bullets between two tracers, <c>5</c>: a weapon with tracers draws one bullet in six.</summary>
    public const int TracerInterval = 5;

    /// <summary>How far past a face the next leg of the trace starts, a centimetre in world units: the float <c>40.96</c>.</summary>
    private const float StepLength = 40.96f;

    /// <summary>The AI event a bullet's strike raises, <c>42</c>.</summary>
    private const int StrikeEvent = 42;

    /// <summary>The strike event's flags, <c>0x402</c>.</summary>
    private const int StrikeFlags = 0x402;

    /// <summary>How far the strike is heard, <c>40960</c> units: ten metres.</summary>
    private const float StrikeRadius = 40960f;

    /// <summary>The groups a bullet is traced against, <c>Forest_bQTaskGroup | 0x58000001</c>; the ground's is added unless the shot ignores it.</summary>
    private const uint ObjectGroups = Igi2CollisionWorld.ForestGroup | 0x58000001u;

    private readonly List<Igi2GunShotInstant> _instants = new(MaxInstants);
    private readonly Igi2LineTrace _trace = new(1);
    private readonly List<Igi2GunShotHitRecord> _hits = [];

    /// <summary>Gets or sets what the shot passes through as though it were not there, <c>ptGunShotIgnoreQTaskRef</c>.</summary>
    public object? Ignore { get; set; }

    /// <summary>Gets or sets the faces a bullet leaves an effect on, <c>GunShot+0x9C</c>; a face past them stops it.</summary>
    public int MaxEffects { get; set; } = DefaultMaxEffects;

    /// <summary>Gets or sets the range, in metres, <see cref="Igi2GunShotOptions.LimitRange"/> cuts a gun's to, <c>GunShot+0xA0</c>.</summary>
    public float RangeLimit { get; set; }

    /// <summary>Gets or sets how the shot traces its bullets.</summary>
    public Igi2GunShotOptions Options { get; set; }

    /// <summary>Gets the bullets until the next tracer, <c>GunShot+0xA8</c>: drawn at zero, then five more without.</summary>
    public int TracerCountdown { get; private set; } = TracerInterval;

    /// <summary>Gets every bullet ever fired, held or lost, <c>GunShot+0x98</c>.</summary>
    public int BulletsFired { get; private set; }

    /// <summary>Gets the bullets fired this tick and not yet traced.</summary>
    public IReadOnlyList<Igi2GunShotInstant> Instants => _instants;

    /// <summary>
    /// Adds a fired bullet, <c>GunShot_CreateProjectileHandler</c> (<c>igi2 0x4C78A0</c>): it flies along the launch's
    /// forward axis for the gun's range, cut to the shot's limit when the shot limits it.
    /// </summary>
    /// <param name="launch">The launch the gun made.</param>
    /// <param name="parameters">The gun's bullet parameters.</param>
    public void CreateProjectile(in Igi2ProjectileLaunch launch, Igi2GunShotParameters parameters)
    {
        float[] m = launch.Orientation;
        float range = launch.EffectiveRange;
        if ((Options & Igi2GunShotOptions.LimitRange) != 0 && RangeLimit < range)
        {
            range = RangeLimit;
        }

        AddInstant(launch.Position, new Vector3f(m[1], m[4], m[7]), range, parameters.DamageFactor, parameters.PenetrationPower, launch.WeaponType, launch.AmmoType);
    }

    /// <summary>Adds a bullet, <c>GunShot_AddInstant</c> (<c>igi2 0x4C7980</c>); one past the fifteen is counted and lost.</summary>
    /// <param name="position">Where it leaves from.</param>
    /// <param name="direction">Which way it flies.</param>
    /// <param name="effectiveRange">How far it reaches, in metres.</param>
    /// <param name="damageFactor">The damage it does at its muzzle.</param>
    /// <param name="penetrationPower">How much material it goes through.</param>
    /// <param name="weaponType">The weapon that fired it.</param>
    /// <param name="ammoType">The ammunition, or <see langword="null"/>.</param>
    public void AddInstant(
        Vector3d position,
        Vector3f direction,
        float effectiveRange,
        float damageFactor,
        float penetrationPower,
        Igi2WeaponTypeTask weaponType,
        Igi2AmmoTypeTask? ammoType)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        if (_instants.Count < MaxInstants)
        {
            _instants.Add(new Igi2GunShotInstant
            {
                Position = position,
                Direction = direction,
                Range = (new Fpu(effectiveRange) * 4096.0).F,
                DamageFactor = damageFactor,
                Penetration = penetrationPower,
                WeaponType = weaponType,
                AmmoType = ammoType,
            });
        }

        BulletsFired++;
    }

    /// <summary>
    /// Traces every bullet fired this tick and forgets them, <c>GunShot_RunHandler</c> (<c>igi2 0x4C76E0</c>) and
    /// <c>GunShot_RunInstant</c> (<c>0x4C6B50</c>). With no weapon holding the shot its bullets are forgotten untraced.
    /// </summary>
    /// <param name="shooter">The human whose weapon holds the shot, the weapon's parent; <see langword="null"/> for none.</param>
    /// <param name="hitProbability">How likely the weapon's bullets are to strike the player, its own (<c>Weapon+0x64</c>).</param>
    /// <param name="world">The level the bullets fly through.</param>
    /// <param name="effects">What the bullets show and sound.</param>
    public void Run(IIgi2WeaponOwner? shooter, float hitProbability, IIgi2GunShotWorld world, IIgi2GunShotEffects effects)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(effects);
        if (shooter is null || _instants.Count == 0)
        {
            _instants.Clear();
            return;
        }

        uint groups = ObjectGroups | ((Options & Igi2GunShotOptions.IgnoreTerrain) == 0 ? Igi2CollisionWorld.TerrainGroup : 0u);
        Igi2WeaponCollisionQuery query = new(shooter, Ignore, hitProbability);
        _trace.Mesh = 0;
        _trace.TracePortals = false;
        _trace.IgnoreMaterial = Igi2LineTrace.AirMaterial;
        _trace.Includes = owner => owner is not IIgi2WeaponCollisionTarget target || target.EvaluateWeaponCollision(query);

        bool rung = false;
        foreach (Igi2GunShotInstant bullet in _instants)
        {
            Trace(bullet, shooter, groups, world, effects, ref rung);
        }

        _instants.Clear();
    }

    /// <summary>One bullet's flight, from its muzzle to where it stops, and the effects of the faces it crossed.</summary>
    private void Trace(Igi2GunShotInstant bullet, IIgi2WeaponOwner shooter, uint groups, IIgi2GunShotWorld world, IIgi2GunShotEffects effects, ref bool rung)
    {
        Vector3f dir = bullet.Direction;
        float stepX = (new Fpu(dir.X) * StepLength).F;
        float stepY = (new Fpu(dir.Y) * StepLength).F;
        float stepZ = (new Fpu(dir.Z) * StepLength).F;
        Vector3d start = bullet.Position;
        Vector3d end = new(
            (new Fpu(start.X) + (new Fpu(bullet.Range) * dir.X)).Value,
            (new Fpu(start.Y) + (new Fpu(bullet.Range) * dir.Y)).Value,
            (new Fpu(start.Z) + (new Fpu(bullet.Range) * dir.Z)).Value);
        if ((Options & Igi2GunShotOptions.Drop) != 0)
        {
            end = new Vector3d(end.X, end.Y, (new Fpu(end.Z) - (new Fpu(bullet.Range) * 0.00020000001f)).Value);
        }

        Vector3d tracerStart = start;
        Vector3d tracerEnd = start;
        float penetrationAtMuzzle = bullet.Penetration;
        float travelled = 0f;
        bool stopped = bullet.Penetration == 0f;
        _hits.Clear();

        while (!stopped)
        {
            _trace.Starts[0] = start;
            _trace.Ends[0] = end;
            Igi2Intersection hit = _trace.Intersections[0];
            hit.EnterMaterial = null;
            hit.LeaveMaterial = null;
            world.Collision.TraceLines(_trace, groups);
            if (!hit.IsHit)
            {
                tracerEnd = end;
                break;
            }

            Fpu dx = new Fpu(hit.Position.X) - start.X;
            Fpu dy = new Fpu(hit.Position.Y) - start.Y;
            Fpu dz = new Fpu(hit.Position.Z) - start.Z;
            travelled = (Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz)) + travelled).F;
            bool terrain = hit.HitObject is Igi2TerrainCollider;
            if (hit.EnterMaterial is { } enterRow)
            {
                stopped = Enter(bullet, hit, enterRow, start, end, travelled, penetrationAtMuzzle, shooter, world, effects, ref rung) || terrain;
            }
            else
            {
                stopped = Leave(bullet, hit, world) || terrain;
            }

            start = new Vector3d((new Fpu(stepX) + hit.Position.X).Value, (new Fpu(stepY) + hit.Position.Y).Value, (new Fpu(stepZ) + hit.Position.Z).Value);
            tracerEnd = hit.Position;
        }

        DrawTracer(bullet, tracerStart, tracerEnd, shooter, effects);
        foreach (Igi2GunShotHitRecord record in _hits)
        {
            Igi2GunShotEffect.Create(record, bullet.WeaponType, world, shooter.Random, effects);
        }
    }

    /// <summary>
    /// A face the bullet goes into: its damage to what it struck, the strike's sound and the AI's hearing of it once a
    /// tick, the face's toll on its penetration and the face's effect.
    /// </summary>
    /// <returns>Whether the bullet stops here: no penetration left, or no room for the face's effect.</returns>
    private bool Enter(
        Igi2GunShotInstant bullet,
        Igi2Intersection hit,
        int row,
        Vector3d start,
        Vector3d end,
        float travelled,
        float penetrationAtMuzzle,
        IIgi2WeaponOwner shooter,
        IIgi2GunShotWorld world,
        IIgi2GunShotEffects effects,
        ref bool rung)
    {
        Fpu left = new Fpu(bullet.Penetration) / penetrationAtMuzzle;
        Fpu falloff = 1.0f - ((new Fpu(travelled) / bullet.Range) * 0.30000001f);
        float damage = ((falloff * bullet.DamageFactor) * left).F;
        Vector3f shot = new((new Fpu(end.X) - start.X).F, (new Fpu(end.Y) - start.Y).F, (new Fpu(end.Z) - start.Z).F);
        if ((hit.HitObject?.Handler ?? hit.HitObject?.Owner) is IIgi2WeaponHitTarget target)
        {
            target.WeaponHit(new Igi2WeaponHit(hit, damage, shot, shooter, bullet.WeaponType, bullet.AmmoType));
        }

        Igi2GameMaterial? material = world.Materials[row];
        if (!rung)
        {
            uint draw = shooter.Random.NextRaw();
            string sound = material?.BulletHitSounds[(int)(draw % 6)] ?? string.Empty;
            rung = effects.NewSound(sound, hit.Position);
            Vector3f back = new((new Fpu(bullet.Direction.X) * -1.0f).F, (new Fpu(bullet.Direction.Y) * -1.0f).F, (new Fpu(bullet.Direction.Z) * -1.0f).F);
            effects.SendAiEvent(new Igi2WeaponAiEvent(StrikeEvent, StrikeFlags, StrikeRadius, hit.Position, back, shooter.Team, shooter));
        }

        float penetration = (new Fpu(bullet.Penetration) - (material?.PenetrationPower ?? 0f)).F;
        bullet.Penetration = penetration;
        if (_hits.Count >= MaxEffects)
        {
            return true;
        }

        bool continues = penetration > 0f;
        _hits.Add(new Igi2GunShotHitRecord(
            material?.BulletEnterEffect ?? NoEffect,
            Copy(hit),
            Reflect(bullet.Direction, hit.Normal),
            penetration,
            continues));
        return !continues;
    }

    /// <summary>
    /// A face the bullet comes out of: with a material behind it and room for the effect, the bullet goes on if it has
    /// more penetration than the material takes, paying it; otherwise it stops.
    /// </summary>
    /// <returns>Whether the bullet stops here.</returns>
    private bool Leave(Igi2GunShotInstant bullet, Igi2Intersection hit, IIgi2GunShotWorld world)
    {
        if (hit.LeaveMaterial is not { } row)
        {
            return true;
        }

        Igi2GameMaterial? material = world.Materials[row];
        float toll = material?.PenetrationPower ?? 0f;
        if (_hits.Count >= MaxEffects || bullet.Penetration <= toll)
        {
            return true;
        }

        bullet.Penetration = (new Fpu(bullet.Penetration) - toll).F;
        _hits.Add(new Igi2GunShotHitRecord(material?.BulletLeaveEffect ?? NoEffect, Copy(hit), bullet.Direction, bullet.Penetration, Continues: true));
        return false;
    }

    /// <summary>
    /// A tracer, when the weapon names one: the countdown runs down a bullet at a time and at zero the bullet is drawn and
    /// the countdown starts again from five.
    /// </summary>
    private void DrawTracer(Igi2GunShotInstant bullet, Vector3d start, Vector3d end, IIgi2WeaponOwner shooter, IIgi2GunShotEffects effects)
    {
        if (bullet.WeaponType.TracerTexture.Length == 0)
        {
            return;
        }

        if (TracerCountdown != 0)
        {
            TracerCountdown--;
            return;
        }

        effects.Tracer(new Igi2Tracer(start, end, shooter, $"{bullet.WeaponType.TracerTexture}.tex", bullet.WeaponType.TracerVelocityMps));
        TracerCountdown = TracerInterval;
    }

    /// <summary>
    /// The bullet's reflection off a face: the face's normal made unit length unless it is zero, and the reversed
    /// direction mirrored about it, <c>2(n·d)n − d</c>, in the handler's order.
    /// </summary>
    private static Vector3f Reflect(Vector3f direction, Vector3f normal)
    {
        float nx = normal.X, ny = normal.Y, nz = normal.Z;
        if (nx != 0f || ny != 0f || nz != 0f)
        {
            Fpu x = nx, y = ny, z = nz;
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((x * x) + (y * y)) + (z * z));
            nx = (x * inverse).F;
            ny = (y * inverse).F;
            nz = (z * inverse).F;
        }

        Fpu a = new Fpu(direction.X) * -1.0f;
        Fpu b = new Fpu(direction.Y) * -1.0f;
        Fpu c = new Fpu(direction.Z) * -1.0f;
        float cs = c.F;
        Fpu dot = (((c * nz) + (b * ny)) + (a * nx));
        Fpu twiceX = (new Fpu(nx) * dot) + (new Fpu(nx) * dot);
        float twiceY = ((dot * ny) + (dot * ny)).F;
        float twiceZ = ((dot * nz) + (dot * nz)).F;
        return new Vector3f((twiceX - a).F, (new Fpu(twiceY) - b).F, (new Fpu(twiceZ) - cs).F);
    }

    private static Igi2Intersection Copy(Igi2Intersection hit)
    {
        Igi2Intersection copy = new();
        copy.CopyFrom(hit);
        return copy;
    }

    private static readonly Igi2MaterialEffect NoEffect = new(Igi2WeaponConstants.NoTaskType, null);
}

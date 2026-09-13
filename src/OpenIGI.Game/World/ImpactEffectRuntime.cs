using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One body's pair of blood systems, as a <c>Human</c> holds them at <c>+2104</c>/<c>+2108</c>.</summary>
/// <param name="Mist">The <c>blood1.spr</c> quads at the wound.</param>
/// <param name="Droplets">The <c>blood2.spr</c> droplets in the air.</param>
public readonly record struct BloodSystems(RetailParticleSystem Mist, RetailParticleSystem Droplets);

/// <summary>What one wound's blood did, for diagnosis.</summary>
/// <param name="Sprayed">Whether the mist and droplets were thrown at all.</param>
/// <param name="BackstopHit">Whether the trace behind the body met anything inside three metres.</param>
/// <param name="BackstopMaterial">The struck row, or <c>-1</c> when nothing was met.</param>
/// <param name="BackstopMarked">Whether that row took the splash.</param>
/// <param name="BackstopDistanceUnits">How far behind the wound it was, in world units.</param>
public readonly record struct BloodReport(
    bool Sprayed,
    bool BackstopHit,
    int BackstopMaterial,
    bool BackstopMarked,
    double BackstopDistanceUnits);

/// <summary>
/// Every particle effect a round produces, owned in one place and stepped on the fixed tick.
/// </summary>
/// <remarks>
/// <para>
/// This is where the two dispatchers land. A surface hit runs the struck material's
/// <c>GunShotEffect</c> block (<see cref="GunShotImpactEffects"/>) — a puff of dust and a handful of
/// chips; a body hit runs <c>Human</c>'s blood (<see cref="BloodEffects"/>) — a mist, two sprays of
/// droplets, and the marks the spray leaves on whatever it reaches. The marks all go into the same
/// <see cref="BulletMarks"/> the bullet holes do, because in the original they are the same kind of
/// thing: a texture projected onto the object that was hit.
/// </para>
/// <para>
/// <b>Blood systems belong to the body, not to the shot.</b> The original stores them on the human, so
/// a guard caught in a burst shows two mist quads and ten droplets no matter how many rounds land.
/// That cap is the difference between a wounded guard and a fountain, so the systems are kept per
/// owner here too and only released once both have gone quiet.
/// </para>
/// </remarks>
public sealed class ImpactEffectRuntime
{
    /// <summary>How many wall-puff quads may be alive across the whole world.</summary>
    /// <remarks>
    /// <b>A port-level bound, not the original's.</b> The original gives every puffing impact its own
    /// two-particle system and lets the task system carry them, so the real limit is however many
    /// impacts are in flight. Two hundred and fifty-six is a hundred and twenty-eight simultaneous
    /// impacts, which no weapon in the game can produce.
    /// </remarks>
    public const int PuffCapacity = 256;

    /// <summary>How many chips may be in the air across the whole world.</summary>
    /// <remarks>The same kind of bound, for the same reason.</remarks>
    public const int FragmentCapacity = 256;

    /// <summary>How many bodies may hold blood systems at once.</summary>
    public const int BloodBodyCapacity = 32;

    private readonly IgiRandom _random;
    private readonly Dictionary<object, BloodSystems> _blood = [];
    private readonly List<object> _idleBodies = [];
    private readonly List<ImpactFragment> _fragments = [];
    private int _mistFrameCount = 1;

    /// <summary>Initializes a new instance of the <see cref="ImpactEffectRuntime"/> class.</summary>
    /// <param name="random">The draw source; a fresh sequence when omitted.</param>
    public ImpactEffectRuntime(IgiRandom? random = null)
    {
        _random = random ?? new IgiRandom();
        Puffs = new RetailParticleSystem(
            acceleration: Vector3f.Zero,
            sizeInUnits: GunShotImpactEffects.PuffSizeInUnits,
            growthPerTick: GunShotImpactEffects.PuffGrowthPerTick,
            frameCount: 1,
            ticksPerFrame: 0,
            capacity: PuffCapacity);
    }

    /// <summary>Gets the dust the world's impacts have kicked up.</summary>
    public RetailParticleSystem Puffs { get; }

    /// <summary>Gets the chips in the air.</summary>
    public IReadOnlyList<ImpactFragment> Fragments => _fragments;

    /// <summary>Gets the bodies currently bleeding, and their systems.</summary>
    public IReadOnlyDictionary<object, BloodSystems> Blood => _blood;

    /// <summary>Gets or sets whether blood is drawn at all — the <c>GOIsBlood</c> game option.</summary>
    /// <remarks>
    /// <c>0x463AD9</c> reads this before it does anything else, so with it clear a body throws no mist,
    /// no droplets and no marks. Everything else about the hit is unchanged.
    /// </remarks>
    public bool BloodEnabled { get; set; } = true;

    /// <summary>Tells the runtime how many frames <c>blood1.spr</c> turned out to have.</summary>
    /// <param name="frameCount">The frame count the renderer loaded; retail ships seven.</param>
    /// <remarks>
    /// The mist's frame rate is <c>15 / frameCount</c> whole ticks, so it cannot be fixed until the
    /// sprite has actually been read. Systems already built keep the rate they were built with.
    /// </remarks>
    public void SetMistFrameCount(int frameCount) => _mistFrameCount = Math.Max(1, frameCount);

    /// <summary>Drops every live effect, as changing level does.</summary>
    public void Clear()
    {
        Puffs.Clear();
        _fragments.Clear();
        _blood.Clear();
    }

    /// <summary>Runs one surface hit's <c>GunShotEffect</c>.</summary>
    /// <param name="position">The contact point, in world units.</param>
    /// <param name="normal">The struck face's normal.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <param name="effect">The struck material's effect block.</param>
    /// <param name="struckHuman">Whether the object hit was a human.</param>
    /// <remarks>
    /// The mark is deliberately not this method's business: <see cref="BulletMarks.TryAdd"/> already
    /// does that part of <c>0x438440</c>, and it is the part with no flag on it.
    /// </remarks>
    public void SpawnSurfaceImpact(
        Vector3d position,
        Vector3f normal,
        Vector3f direction,
        in GunShotEffect effect,
        bool struckHuman)
    {
        // 0x4384C9 — the chips, before the mark and the puff.
        if (GunShotImpactEffects.ThrowsFragments(effect, struckHuman))
        {
            SpawnFragments(position, direction, effect.Model);
        }

        // 0x43856B — one coin, then two quads.
        if (!GunShotImpactEffects.SpawnsPuff(effect, struckHuman, (int)_random.NextBounded(2)))
        {
            return;
        }

        Vector3f lift = Normalize(normal) * GunShotImpactEffects.PuffLiftInUnits;
        Vector3d first = new(position.X + lift.X, position.Y + lift.Y, position.Z + lift.Z);
        Vector3d second = new(
            first.X,
            first.Y,
            first.Z + GunShotImpactEffects.PuffSecondRiseInUnits);

        float rise = GunShotImpactEffects.PuffRiseSpeed(_random.NextSingle());
        float alpha = (_random.NextSingle() * GunShotImpactEffects.PuffAlphaRange)
            + GunShotImpactEffects.PuffAlphaMinimum;

        Puffs.Emit(
            first,
            new Vector3f(0f, 0f, rise),
            alpha,
            GunShotImpactEffects.Spin(_random.NextSingle()),
            angle: 0f,
            growth: 1f,
            GunShotImpactEffects.PuffHoldTicks,
            GunShotImpactEffects.PuffFadeTicks);

        Puffs.Emit(
            second,
            new Vector3f(0f, 0f, rise * GunShotImpactEffects.PuffSecondSpeedScale),
            alpha,
            GunShotImpactEffects.Spin(_random.NextSingle()),
            angle: 0f,
            growth: 1f,
            GunShotImpactEffects.PuffHoldTicks,
            GunShotImpactEffects.PuffFadeTicks);
    }

    /// <summary>Runs one wound's blood, as <c>0x463AD0</c> does.</summary>
    /// <param name="owner">Whatever identifies the body — its systems are kept against this.</param>
    /// <param name="position">Where the round entered, in world units.</param>
    /// <param name="direction">The round's travel direction.</param>
    /// <param name="walls">The world cast, for the backstop splash; may be <see langword="null"/>.</param>
    /// <param name="marks">Where the backstop splash is recorded.</param>
    /// <param name="materials">The material table, which decides whether the backstop marks at all.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public BloodReport SpawnBlood(
        object owner,
        Vector3d position,
        Vector3f direction,
        WorldRayCast? walls,
        BulletMarks? marks,
        GameMaterialRegistry? materials)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!BloodEnabled)
        {
            return new BloodReport(false, false, -1, false, 0.0);
        }

        Vector3f line = Normalize(direction);

        // 0x463B9C: the splash on whatever stood behind the target, three metres on.
        BloodReport report = StampBackstop(position, line, walls, marks, materials);

        BloodSystems systems = SystemsFor(owner);
        BloodEffects.Spray(systems.Mist, systems.Droplets, position, line, _random);
        return report with { Sprayed = true };
    }

    /// <summary>Steps every live effect one tick.</summary>
    /// <param name="walls">The world cast the droplets are traced against.</param>
    /// <param name="marks">Where a landing droplet's mark is recorded.</param>
    /// <param name="materials">The material table, which decides whether a struck face marks.</param>
    public void Tick(WorldRayCast? walls, BulletMarks? marks, GameMaterialRegistry? materials)
    {
        Puffs.Tick();
        TickFragments();

        if (_blood.Count == 0)
        {
            return;
        }

        _idleBodies.Clear();
        foreach ((object owner, BloodSystems systems) in _blood)
        {
            systems.Mist.Tick();
            systems.Droplets.Tick(
                walls,
                contact => StampDroplet(contact, marks, materials));

            if (systems.Mist.Count == 0 && systems.Droplets.Count == 0)
            {
                _idleBodies.Add(owner);
            }
        }

        foreach (object owner in _idleBodies)
        {
            _blood.Remove(owner);
        }

        _idleBodies.Clear();
    }

    private void SpawnFragments(Vector3d position, Vector3f direction, string model)
    {
        int count = GunShotImpactEffects.FragmentCount(
            (int)_random.NextBounded(GunShotImpactEffects.FragmentCountRange));

        // 0x4810A0 draws the container's tumble once, before any of its children, so every chip from
        // one impact turns together.
        Vector3f tumble = new(
            _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians,
            _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians,
            _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians);

        for (int i = 0; i < count; i++)
        {
            if (_fragments.Count >= FragmentCapacity)
            {
                _fragments.RemoveAt(0);
            }

            Vector3f velocity = GunShotImpactEffects.FragmentLaunch(direction, _random);

            // 0x47F0D5: each chip is placed with its own small turn.
            Matrix3f orientation = Matrix3f.FromEngineEulerAngles(
                _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians,
                _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians,
                _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians);

            _fragments.Add(new ImpactFragment
            {
                Position = position,
                PreviousPosition = position,
                Velocity = velocity,
                Orientation = orientation,
                PreviousOrientation = orientation,
                AngularVelocity = tumble,
                LifeTicks = GunShotImpactEffects.FragmentLifeTicks,
                Model = model,
            });
        }
    }

    // 0x5035B0, for the one container shape the impact effect builds: drag on the velocity, a fraction
    // of gravity, and an angular velocity that damps by a hundredth every tick.
    private void TickFragments()
    {
        for (int i = 0; i < _fragments.Count;)
        {
            ImpactFragment fragment = _fragments[i];
            fragment.LifeTicks--;
            if (fragment.LifeTicks <= 0)
            {
                _fragments.RemoveAt(i);
                continue;
            }

            fragment.PreviousPosition = fragment.Position;
            fragment.PreviousOrientation = fragment.Orientation;

            fragment.Velocity = new Vector3f(
                fragment.Velocity.X * GunShotImpactEffects.FragmentDrag,
                fragment.Velocity.Y * GunShotImpactEffects.FragmentDrag,
                (fragment.Velocity.Z * GunShotImpactEffects.FragmentDrag)
                    - GunShotImpactEffects.FragmentGravityPerTick);
            fragment.Position = new Vector3d(
                fragment.Position.X + fragment.Velocity.X,
                fragment.Position.Y + fragment.Velocity.Y,
                fragment.Position.Z + fragment.Velocity.Z);

            fragment.AngularVelocity *= GunShotImpactEffects.FragmentAngularDrag;
            fragment.Orientation = Matrix3f.FromEngineEulerAngles(
                fragment.AngularVelocity.X,
                fragment.AngularVelocity.Y,
                fragment.AngularVelocity.Z) * fragment.Orientation;

            _fragments[i] = fragment;
            i++;
        }
    }

    private BloodSystems SystemsFor(object owner)
    {
        if (_blood.TryGetValue(owner, out BloodSystems existing))
        {
            return existing;
        }

        if (_blood.Count >= BloodBodyCapacity)
        {
            // Every slot is bleeding: give this wound the least recently added body's systems rather
            // than showing nothing, which is the only place this bound can be felt at all.
            foreach (object first in _blood.Keys)
            {
                _blood.Remove(first);
                break;
            }
        }

        BloodSystems systems = new(
            BloodEffects.CreateMistSystem(_mistFrameCount),
            BloodEffects.CreateDropletSystem());
        _blood[owner] = systems;
        return systems;
    }

    private BloodReport StampBackstop(
        Vector3d position,
        Vector3f line,
        WorldRayCast? walls,
        BulletMarks? marks,
        GameMaterialRegistry? materials)
    {
        if (walls is null || marks is null)
        {
            return new BloodReport(false, false, -1, false, 0.0);
        }

        Vector3d end = new(
            position.X + (line.X * BloodEffects.BackstopRangeInUnits),
            position.Y + (line.Y * BloodEffects.BackstopRangeInUnits),
            position.Z + (line.Z * BloodEffects.BackstopRangeInUnits));

        if (!walls(position, end, out WorldRayHit hit))
        {
            return new BloodReport(false, false, -1, false, 0.0);
        }

        // 0x463C6C: the splash only lands where the struck material's own effect names a texture --
        // the same guard the bullet hole passes. A body's own collision and the see-through rows
        // (Fence, MetalFence, MetalLadder) therefore take no blood.
        if (materials is null
            || !materials.TryGet(hit.Material, out GameMaterial? material)
            || material is null
            || !material.RecordsImpacts)
        {
            return new BloodReport(false, true, hit.Material, false, hit.Distance);
        }

        float radius = BloodEffects.BackstopRadius(_random.NextSingle());
        string texture = BloodEffects.BackstopTextures[(int)_random.NextBounded(2)];
        marks.AddMark(Lift(hit), hit.Normal, texture, radius);
        return new BloodReport(false, true, hit.Material, true, hit.Distance);
    }

    private void StampDroplet(
        in RetailParticleContact contact,
        BulletMarks? marks,
        GameMaterialRegistry? materials)
    {
        if (marks is null
            || !BloodEffects.MarksSurface(contact.From, contact.To, contact.Hit))
        {
            return;
        }

        if (materials is null
            || !materials.TryGet(contact.Hit.Material, out GameMaterial? material)
            || material is null
            || !material.RecordsImpacts)
        {
            return;
        }

        float radius = BloodEffects.DropletMarkRadius(_random.NextSingle());
        string texture = BloodEffects.DropletTextures[(int)_random.NextBounded(2)];
        marks.AddMark(Lift(contact.Hit), contact.Hit.Normal, texture, radius);
    }

    // The same five millimetres off the face a bullet hole takes, so a splash does not fight the
    // surface it is on for depth.
    private static Vector3d Lift(in WorldRayHit hit) => new(
        hit.Position.X + (hit.Normal.X * Weapons.GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Y + (hit.Normal.Y * Weapons.GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Z + (hit.Normal.Z * Weapons.GunShotBallistics.PenetrationStepOffUnits));

    private static Vector3f Normalize(Vector3f value)
    {
        float lengthSquared = value.LengthSquared;
        if (!(lengthSquared > 0f))
        {
            return new Vector3f(0f, 0f, 1f);
        }

        float inverse = 1f / MathF.Sqrt(lengthSquared);
        return new Vector3f(value.X * inverse, value.Y * inverse, value.Z * inverse);
    }
}

using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What one droplet's collision segment met.</summary>
/// <param name="From">Where the segment started, in world units.</param>
/// <param name="To">Where it ended — the particle's position this tick.</param>
/// <param name="Hit">The surface it met.</param>
public readonly record struct RetailParticleContact(Vector3d From, Vector3d To, WorldRayHit Hit);

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
    /// <summary>How many chips may be in the air across the whole world.</summary>
    /// <remarks>The same kind of bound, for the same reason.</remarks>
    public const int FragmentCapacity = 256;

    /// <summary>How many bodies may hold blood systems at once.</summary>
    public const int BloodBodyCapacity = 32;

    private IgiRandom _random;

    private readonly DropletLanding _landing;
    private WorldRayCast? _walls;
    private BulletMarks? _marks;
    private GameMaterialRegistry? _materials;
    private readonly Dictionary<object, Particle2DBleeding> _blood = [];
    private readonly List<object> _idleBodies = [];
    private readonly List<ImpactFragment> _fragments = [];
    private readonly List<PendingChips> _pendingChips = [];
    private int _mistFrameCount = 1;

    /// <summary>Initializes a new instance of the <see cref="ImpactEffectRuntime"/> class.</summary>
    /// <param name="random">The draw source; a fresh sequence when omitted.</param>
    public ImpactEffectRuntime(IgiRandom? random = null)
    {
        _random = random ?? new IgiRandom();
        _landing = new DropletLanding(this);
    }

    /// <summary>
    /// Gets the 2-D particle engine the dust and blood run in, the one both titles share. Its stream is the emitter's own,
    /// <c>dword_A7A4C0</c>: the droplets' cone and speed, which the level stream never sees.
    /// </summary>
    public Particle2DModule Particles { get; } = new(EmitterStream());

    // Particle2DDynCubeObjTask's registration (0x4FCF50) resets the emitter's stream once for the whole run and mixes in
    // 0x12332D53.
    private static IgiRandom EmitterStream()
    {
        IgiRandom stream = new();
        stream.Seed(0xC8FB1A07u, 0x39EA7756u, 0x7FF13CCDu);
        stream.Mix(0x12332D53u);
        return stream;
    }

    /// <summary>Draws from the level's one stream from now on, as every retail effect does.</summary>
    /// <param name="random">The level stream.</param>
    public void UseRandom(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <summary>Gets how many dust particles the world's impacts have in the air.</summary>
    public int PuffCount
    {
        get
        {
            int count = 0;
            foreach (Particle2DSystem system in Particles.Systems)
            {
                count += system.Look.Sprite == GunShotImpactEffects.PuffSprite ? system.Count : 0;
            }

            return count;
        }
    }

    /// <summary>Gets the chips in the air.</summary>
    public IReadOnlyList<ImpactFragment> Fragments => _fragments;

    /// <summary>Gets the bodies currently bleeding, and their systems.</summary>
    public IReadOnlyDictionary<object, Particle2DBleeding> Blood => _blood;

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
        Particles.Clear();
        _fragments.Clear();
        _pendingChips.Clear();
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

        Vector3f lift = normal.ReciprocalNormalizedOr(Vector3f.UnitZ) * GunShotImpactEffects.PuffLiftInUnits;
        Vector3d first = new(position.X + lift.X, position.Y + lift.Y, position.Z + lift.Z);
        Vector3d second = new(
            first.X,
            first.Y,
            first.Z + GunShotImpactEffects.PuffSecondRiseInUnits);

        float rise = GunShotImpactEffects.PuffRiseSpeed(_random.NextSingle());
        float alpha = (_random.NextSingle() * GunShotImpactEffects.PuffAlphaRange)
            + GunShotImpactEffects.PuffAlphaMinimum;

        float firstSpin = GunShotImpactEffects.Spin(_random.NextSingle());
        float secondSpin = GunShotImpactEffects.Spin(_random.NextSingle());
        Puff(first, second, rise, alpha, firstSpin, secondSpin);
    }

    /// <summary>Runs one recorded impact's <c>GunShotEffect</c> in the original's order (<c>0x438440</c>).</summary>
    /// <param name="position">The contact point, in world units.</param>
    /// <param name="normal">The struck face's normal.</param>
    /// <param name="vector">The record's vector, which the chips are thrown along.</param>
    /// <param name="effect">The block the struck material names for the side the round crossed.</param>
    /// <param name="struckHuman">Whether the struck object is a human, which throws neither.</param>
    /// <param name="visible">Whether to emit anything; the draws are made either way.</param>
    /// <remarks>
    /// <para>
    /// The chips are counted and their container's tumble drawn here (<c>0x43848C</c>, <c>0x481250</c>), but the
    /// <c>Explosion0</c> that throws them is a task of its own, appended to the level's dynamic container: it
    /// detonates later the same tick, after every gun's march (<see cref="DetonatePendingChips"/>).
    /// </para>
    /// <para>
    /// The dust is a raw coin and then four draws, all here (<c>0x438576</c> to <c>0x438766</c>).
    /// </para>
    /// </remarks>
    public void RunGunShotEffect(
        Vector3d position,
        Vector3f normal,
        Vector3f vector,
        in GunShotEffect effect,
        bool struckHuman,
        bool visible)
    {
        if (effect.LeavesMark && !struckHuman)
        {
            int count = GunShotImpactEffects.FragmentCount(
                (int)_random.NextBounded(GunShotImpactEffects.FragmentCountRange));
            Vector3f tumble = new(
                _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians,
                _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians,
                _random.NextSingle() * GunShotImpactEffects.FragmentTumbleRadians);
            _pendingChips.Add(new PendingChips(
                position, vector, effect.Model, count, tumble, visible && effect.Model.Length > 0));
        }

        if (!effect.SpawnsParticles || struckHuman || (_random.NextRaw() & 1) == 0)
        {
            return;
        }

        Vector3f lift = normal.ReciprocalNormalizedOr(new Vector3f(0f, 0f, 1f)) * GunShotImpactEffects.PuffLiftInUnits;
        Vector3d first = new(position.X + lift.X, position.Y + lift.Y, position.Z + lift.Z);
        Vector3d second = new(first.X, first.Y, first.Z + GunShotImpactEffects.PuffSecondRiseInUnits);
        float rise = GunShotImpactEffects.PuffRiseSpeed(_random.NextSingle());
        float alpha = (_random.NextSingle() * GunShotImpactEffects.PuffAlphaRange)
            + GunShotImpactEffects.PuffAlphaMinimum;
        float firstSpin = GunShotImpactEffects.Spin(_random.NextSingle());
        float secondSpin = GunShotImpactEffects.Spin(_random.NextSingle());
        if (!visible)
        {
            return;
        }

        Puff(first, second, rise, alpha, firstSpin, secondSpin);
    }

    /// <summary>Detonates every chip container placed since the last call, in the order they were placed.</summary>
    /// <remarks>
    /// <c>Explosion0</c>'s first tick (<c>0x480F40</c> to <c>0x47F200</c>): each chip draws its speed, its roll,
    /// its spread, one draw nothing reads, and then its own placement turn (<c>0x47F0D0</c>).
    /// </remarks>
    public void DetonatePendingChips()
    {
        foreach (PendingChips chips in _pendingChips)
        {
            for (int i = 0; i < chips.Count; i++)
            {
                Vector3f velocity = GunShotImpactEffects.FragmentLaunch(chips.Direction, _random);
                _ = _random.NextDouble();

                // 0x47F0D5: three draws, handed to the Euler builder last first.
                float first = _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians;
                float second = _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians;
                float third = _random.NextSingle() * GunShotImpactEffects.FragmentPlacementRadians;
                if (!chips.Visible)
                {
                    continue;
                }

                if (_fragments.Count >= FragmentCapacity)
                {
                    _fragments.RemoveAt(0);
                }

                Matrix3f orientation = Matrix3f.FromEngineEulerAngles(third, second, first);
                _fragments.Add(new ImpactFragment
                {
                    Position = chips.Position,
                    PreviousPosition = chips.Position,
                    Velocity = velocity,
                    Orientation = orientation,
                    PreviousOrientation = orientation,
                    AngularVelocity = chips.Tumble,
                    LifeTicks = GunShotImpactEffects.FragmentLifeTicks,
                    Model = chips.Model,
                });
            }
        }

        _pendingChips.Clear();
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

        Vector3f line = direction.ReciprocalNormalizedOr(Vector3f.UnitZ);

        // 0x463B9C: the splash on whatever stood behind the target, three metres on.
        BloodReport report = StampBackstop(position, line, walls, marks, materials);

        BleedingOf(owner).Spray(Particles, BloodEffects.Tuning, position, line, _random, BloodTuning.Unlit, _mistFrameCount, _landing);
        return report with { Sprayed = true };
    }

    /// <summary>Steps every live effect one tick.</summary>
    /// <param name="walls">The world cast the droplets are traced against.</param>
    /// <param name="marks">Where a landing droplet's mark is recorded.</param>
    /// <param name="materials">The material table, which decides whether a struck face marks.</param>
    public void Tick(WorldRayCast? walls, BulletMarks? marks, GameMaterialRegistry? materials)
    {
        // Every system a tick, in the order they were made; a landing droplet stamps where it lands.
        _walls = walls;
        _marks = marks;
        _materials = materials;
        Particles.RunAll();
        TickFragments();

        if (_blood.Count == 0)
        {
            return;
        }

        // A body whose two systems have both burnt out holds none.
        _idleBodies.Clear();
        foreach ((object owner, Particle2DBleeding bleeding) in _blood)
        {
            if (bleeding.IsQuiet)
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

    // 0x438440: the two puffs of one impact, in a two-particle system of their own.
    private void Puff(Vector3d first, Vector3d second, float rise, float alpha, float firstSpin, float secondSpin)
    {
        Particle2DSystem puff = Particles.New(GunShotImpactEffects.PuffLook, Vector3f.Zero, GunShotImpactEffects.PuffGrowthPerTick, GunShotImpactEffects.PuffCapacity, collider: null);
        puff.AddParticle(first, new Vector3f(0f, 0f, rise), alpha, firstSpin, 0f, 1f, GunShotImpactEffects.PuffHoldTicks, GunShotImpactEffects.PuffFadeTicks);
        puff.AddParticle(
            second, new Vector3f(0f, 0f, rise * GunShotImpactEffects.PuffSecondSpeedScale), alpha, secondSpin, 0f, 1f,
            GunShotImpactEffects.PuffHoldTicks, GunShotImpactEffects.PuffFadeTicks);
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

    // A body's blood, kept across its wounds; the shared routine makes each system anew once the last has burnt out.
    private Particle2DBleeding BleedingOf(object owner)
    {
        if (_blood.TryGetValue(owner, out Particle2DBleeding? existing))
        {
            return existing;
        }

        if (_blood.Count >= BloodBodyCapacity)
        {
            // Every slot is bleeding: give this wound the least recently added body's place rather
            // than showing nothing, which is the only place this bound can be felt at all.
            foreach (object first in _blood.Keys)
            {
                _blood.Remove(first);
                break;
            }
        }

        Particle2DBleeding bleeding = new();
        _blood[owner] = bleeding;
        return bleeding;
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
        // 0x4640B0 asks the struck object's type for its decal handler first: the terrain is no object, so a droplet
        // landing on open ground does nothing at all.
        if (marks is null
            || contact.Hit.OnTerrain
            || !BloodEffects.MarksSurface(contact.From, contact.To, contact.Hit))
        {
            return;
        }

        // 0x4640B0 draws the size and the texture before the struck object's decal handler decides on its material.
        float radius = BloodEffects.DropletMarkRadius(_random.NextSingle());
        string texture = BloodEffects.DropletTextures[(int)_random.NextBounded(2)];
        if (materials is null
            || !materials.TryGet(contact.Hit.Material, out GameMaterial? material)
            || material is null
            || !material.RecordsImpacts)
        {
            return;
        }

        marks.AddMark(Lift(contact.Hit), contact.Hit.Normal, texture, radius);
    }

    // A droplet's line, traced against the world: one that meets a face stamps it and dies.
    private sealed class DropletLanding(ImpactEffectRuntime runtime) : IParticleCollider
    {
        public void Collide(ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<bool> struck)
        {
            if (runtime._walls is not { } walls)
            {
                return;
            }

            for (int k = 0; k < starts.Length; k++)
            {
                if (walls(starts[k], ends[k], out WorldRayHit hit))
                {
                    runtime.StampDroplet(new RetailParticleContact(starts[k], ends[k], hit), runtime._marks, runtime._materials);
                    struck[k] = true;
                }
            }
        }
    }

    // The same five millimetres off the face a bullet hole takes, so a splash does not fight the
    // surface it is on for depth.
    private static Vector3d Lift(in WorldRayHit hit) => new(
        hit.Position.X + (hit.Normal.X * OpenIGI.Game.Weapons.GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Y + (hit.Normal.Y * OpenIGI.Game.Weapons.GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Z + (hit.Normal.Z * OpenIGI.Game.Weapons.GunShotBallistics.PenetrationStepOffUnits));

}

/// <summary>An <c>Explosion0</c> placed by an impact, waiting for its first tick.</summary>
internal readonly record struct PendingChips(
    Vector3d Position,
    Vector3f Direction,
    string Model,
    int Count,
    Vector3f Tumble,
    bool Visible);

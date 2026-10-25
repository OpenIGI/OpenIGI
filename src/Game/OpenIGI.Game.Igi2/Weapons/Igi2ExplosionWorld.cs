using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// Where explosions go off: the run stream they draw from, the collision their blast is traced through, the targets they
/// reach (<c>GameFunctions_ptExplosionQTaskList</c>) and the level's dynamic tasks they and what they leave run in.
/// </summary>
/// <param name="random">The run stream, <c>Game.tRunRandomState</c>.</param>
/// <param name="collision">The collision a blast is traced through to a target.</param>
/// <param name="materials">The level's materials, whose penetration a blast loses through each face.</param>
/// <param name="dynamic">The level's dynamic tasks.</param>
public sealed partial class Igi2ExplosionWorld(IgiRandom random, IIgi2CollisionQuery collision, Igi2GameMaterials materials, DynamicTasks dynamic)
    : IIgi2GrenadeEffects
{
    /// <summary>The query groups a blast is traced against: the ground, the splines, the solid objects and the forests.</summary>
    public const uint BlastGroups = 0x98000000 | Igi2CollisionWorld.ForestGroup;

    /// <summary>How far above the blast and the target the line between them runs: 409.6 units.</summary>
    public const double BlastLift = 409.6000061035156;

    /// <summary>The ticks a blast's shake runs over, <c>nExplosionTime</c> in the hit context.</summary>
    public const int ShakeTime = 90;

    private readonly List<IExplosionHitTarget> _targets = [];
    private readonly WorldLineTrace _blast = new(1);

    /// <summary>Gets the run stream.</summary>
    public IgiRandom Random { get; } = random;

    /// <summary>Gets the level's dynamic tasks.</summary>
    public DynamicTasks Dynamic { get; } = dynamic;

    /// <summary>Gets or sets every human registered in the level, <c>Human_ptQTaskList</c>.</summary>
    public IReadOnlyList<Humans.Igi2Human> Humans { get; set; } = [];

    /// <summary>Gets or sets the particle module used by flash and smoke grenades.</summary>
    public Igi2Particles? Particles { get; set; }

    /// <summary>Gets the shared smoke stream, <c>smoke.c Smoke_CreateHandler</c>.</summary>
    public Igi2SmokeRandom SmokeRandom { get; } = new();

    /// <inheritdoc/>
    public Igi2SmokeTask Smoke(Vector3d position)
    {
        // grenade.c Grenade_RunHandler / smoke.c Smoke_New: the packed double supplies two half-intensity RGB components.
        Igi2SmokeTask smoke = new()
        {
            Position = position, Alpha = 0.5f, Gamma = 0f, MaxParticles = 30,
            Radius = 1f, MaxRandomAngle = 3.1415927f, MinVelocity = 0.1f, MaxVelocity = 0.5f,
            Colour = new Vector3f(0.5f, 0.5f, 0.5f), LifeTime = 4f, FadeTime = 2f,
            FadeMode = 0, Sprite = 3, Size = 1f, SizeDelta = 0.6f,
            MinRotation = 0f, MaxRotation = 0f, Intensity = 1f, GenerateFactor = 6.0,
            Particles = Particles, Stream = SmokeRandom,
        };
        smoke.Prepare();
        return smoke;
    }

    /// <inheritdoc/>
    public void SmokeArea(Vector3d position, float range)
    {
        foreach (Humans.Igi2Human human in Humans)
        {
            human.SmokeArea(position, range);
        }
    }

    /// <summary>Gets or sets what explosions show and sound.</summary>
    public IIgi2ExplosionEffects Effects { get; set; } = new Igi2NoExplosionEffects();

    /// <summary>Gets or sets the level's water, which a grenade's blast under it splashes, or <see langword="null"/>.</summary>
    public World.Igi2Water? Water { get; set; }

    /// <summary>The AI event a grenade's blast raises, <c>20</c>, which alone is looked for under water.</summary>
    public const int GrenadeBlastEvent = 20;

    /// <summary>The sound a blast under water makes instead of its own, <c>"explo_water"</c>.</summary>
    public const string UnderwaterSound = "explo_water";

    /// <summary>Gets the targets explosions reach, in the order they joined.</summary>
    public IReadOnlyList<IExplosionHitTarget> Targets => _targets;

    /// <summary>Adds a target, <c>QTaskList_Register(GameFunctions_ptExplosionQTaskList)</c> in its create handler.</summary>
    /// <param name="target">The target.</param>
    public void AddTarget(IExplosionHitTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _targets.Add(target);
    }

    /// <summary>Removes a target, <c>QTaskList_Remove(GameFunctions_ptExplosionQTaskList)</c>.</summary>
    /// <param name="target">The target.</param>
    public void RemoveTarget(IExplosionHitTarget target) => _targets.Remove(target);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Grenade_RunHandler</c> (<c>igi2 0x484260</c>): once, at once, with three fragments and a fireball, the grenade's
    /// burn of 6144, its model and sound, AI event 20 (flags <c>0x402</c>, range 204800) and an exponential falloff.
    /// </remarks>
    public void Explode(in Igi2GrenadeExplosion explosion) =>
        New(
            explosion.Position,
            1,
            1,
            explosion.DamageFactor,
            explosion.FullRange,
            explosion.FalloffRange,
            6144f,
            3,
            1,
            explosion.Ground,
            Igi2GrenadeExplosion.Model,
            Igi2GrenadeExplosion.Sound,
            20,
            0x402,
            204800f,
            null,
            explosion.Thrower,
            Igi2ExplosionFalloff.Exponential);

    /// <summary>
    /// Starts an explosion with fragments thrown about a random direction, <c>Explosion0_New</c> (<c>igi2 0x50A4E0</c>): mostly
    /// up, in a quarter turn either side, at 272 to 1360 units a tick.
    /// </summary>
    /// <param name="position">Where it goes off.</param>
    /// <param name="count">How many times.</param>
    /// <param name="period">The ticks before it first goes off.</param>
    /// <param name="damage">The damage at its heart.</param>
    /// <param name="fullRange">How far the full damage reaches.</param>
    /// <param name="falloffRange">How far past that it falls away.</param>
    /// <param name="burnRange">The size of its burn.</param>
    /// <param name="fragmentCount">How many fragments.</param>
    /// <param name="fireballCount">How many fireballs.</param>
    /// <param name="ground">What it struck.</param>
    /// <param name="fragmentModel">The fragments' model.</param>
    /// <param name="sound">Its sound.</param>
    /// <param name="aiEvent">Its AI event, or -1.</param>
    /// <param name="aiEventFlags">The event's flags.</param>
    /// <param name="aiEventRange">How far the event carries.</param>
    /// <param name="weaponType">The weapon it came from.</param>
    /// <param name="owner">Who set it off.</param>
    /// <param name="falloff">How its damage falls away.</param>
    /// <returns>The explosion.</returns>
    public Igi2Explosion New(
        Vector3d position,
        int count,
        int period,
        float damage,
        float fullRange,
        float falloffRange,
        float burnRange,
        int fragmentCount,
        int fireballCount,
        WorldIntersection? ground,
        string fragmentModel,
        string sound,
        int aiEvent,
        int aiEventFlags,
        float aiEventRange,
        Igi2WeaponTypeTask? weaponType,
        IIgi2WeaponOwner? owner,
        Igi2ExplosionFalloff falloff)
    {
        float x = (Draw() - 0.5).F;
        float y = (Draw() - 0.5).F;
        float z = (Draw() - 0.30000001f).F;
        return Start(new Igi2ExplosionRequest(
            position,
            count,
            period,
            damage,
            fullRange,
            falloffRange,
            burnRange,
            fragmentCount,
            fireballCount,
            ground,
            fragmentModel,
            sound,
            aiEvent,
            aiEventFlags,
            aiEventRange,
            new Vector3f(x, y, z),
            1.5707964f,
            272f,
            1360f,
            weaponType,
            owner,
            falloff));
    }

    /// <summary>
    /// Starts an explosion, <c>Explosion0_NewEx</c> (<c>igi2 0x50A650</c>): the task, a burn on what it struck, the fragments'
    /// container turning at a random spin, and its AI event.
    /// </summary>
    /// <param name="request">The explosion.</param>
    /// <returns>The explosion.</returns>
    /// <remarks>A blast under water would make a water splash instead of fire and sound; the water is not in the collision yet.</remarks>
    public Igi2Explosion Start(Igi2ExplosionRequest request) => Start(request, null);

    /// <summary>
    /// Starts an explosion whose fragments' container spins at angles already drawn, as a bullet's impact effect draws them before
    /// it calls <c>Explosion0_NewEx</c>.
    /// </summary>
    /// <param name="request">The explosion.</param>
    /// <param name="spin">The container's three angles, or <see langword="null"/> to draw them.</param>
    /// <returns>The explosion.</returns>
    public Igi2Explosion Start(Igi2ExplosionRequest request, (float A, float B, float C)? spin)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Explosion0_NewEx: a grenade's blast half a metre under the surface splashes the water, sounds "explo_water" and
        // throws no fireballs.
        if (request.AiEvent == GrenadeBlastEvent && Water is { } water
            && water.IsUnderWater(new Vector3d(request.Position.X, request.Position.Y, ((Fpu)request.Position.Z - 2048.0).Value), out _, out int body))
        {
            water.ExplosionEffect(body, request.Position);
            request = request with { Sound = UnderwaterSound, FireballCount = 0 };
        }

        Vector3f direction = OpenIGI.Game.Physics.RigidBodyKernels.Normalise<Fpu>(request.Direction);
        if (request.Ground is { HitObject: Igi2RigidObject or Igi2SplineSegment } struck)
        {
            Effects.BurnMark(struck, request.BurnRange);
        }

        (float a, float b, float c) = spin ?? ((Draw() * 0.6283185f).F, (Draw() * 0.6283185f).F, (Draw() * 0.6283185f).F);
        ExplosionFragments fragments = new(
            request.FragmentModel,
            15,
            ((Fpu)request.FullRange * 0.00021972656f).F,
            new Vector3f(a, b, c));
        Igi2Explosion explosion = new(this, request, direction, fragments);
        Dynamic.Add(explosion);
        Dynamic.Add(fragments);
        if (request.AiEvent != -1)
        {
            Effects.AiEvent(request.AiEvent, request.AiEventFlags, request.Position, request.AiEventRange);
        }

        return explosion;
    }

    /// <summary>
    /// The damage a blast does a target, <c>GameFunctions_ResolveExplosionHit</c> (<c>igi2 0x48D1F0</c>): full within the full
    /// range of the target's edge, falling away over the falloff past it, and lessened by what lies between.
    /// </summary>
    /// <param name="hit">The blast.</param>
    /// <param name="target">Where the target is.</param>
    /// <param name="radius">How big the target is.</param>
    /// <param name="self">The target, which the line passes, or <see langword="null"/>.</param>
    /// <returns>The damage.</returns>
    /// <remarks>
    /// A timed action's object is not traced. The line, a weapon's, passes the target and the blast's owner.
    /// The line between runs 409.6 units above both. The blast starts with the full range over 4096 of penetration and
    /// loses each face's material's penetration power; stopped short, it does nothing. A target of no size or a blast of no
    /// range is not traced.
    /// </remarks>
    public Fpu Resolve(in Igi2ExplosionHit hit, Vector3d target, float radius, object? self = null)
    {
        Fpu damage = Reach(hit, target, radius);
        return damage.Value == 0.0 ? damage : Shield(hit, hit.Position, target, damage, self);
    }

    /// <summary>The head of <see cref="Resolve"/>: the damage by the target's distance alone, before what lies between.</summary>
    /// <param name="hit">The blast.</param>
    /// <param name="target">Where the target is.</param>
    /// <param name="radius">How big the target is.</param>
    /// <returns>The damage.</returns>
    public static Fpu Reach(in Igi2ExplosionHit hit, Vector3d target, float radius)
    {
        Vector3d e = hit.Position;
        Fpu dx = (Fpu)e.X - target.X, dy = (Fpu)e.Y - target.Y;
        float dz = ((Fpu)e.Z - target.Z).F;
        Fpu distance = Fpu.Sqrt(((dx * dx) + (dy * dy)) + ((Fpu)dz * dz));
        float reach = ((distance - radius) - hit.FullRange).F;
        Fpu damage;
        if (reach < 0f)
        {
            damage = hit.Damage;
        }
        else
        {
            if (!(reach < hit.FalloffRange))
            {
                return 0.0;
            }

            Fpu share = (Fpu)1f - ((Fpu)reach / hit.FalloffRange);
            if (hit.Falloff == Igi2ExplosionFalloff.Exponential)
            {
                share *= share;
            }

            damage = share * hit.Damage;
        }

        return damage;
    }

    // The tail of Resolve: the line between, which each face it crosses takes penetration from.
    private Fpu Shield(in Igi2ExplosionHit hit, Vector3d e, Vector3d target, Fpu damage, object? self)
    {
        float initial = ((Fpu)hit.FullRange * 0.00024414062f).F;
        float penetration = initial;
        Vector3d start = new(e.X, e.Y, ((Fpu)e.Z + BlastLift).Value);
        Vector3d end = new(target.X, target.Y, ((Fpu)target.Z + BlastLift).Value);
        float tx = ((Fpu)end.X - start.X).F, ty = ((Fpu)end.Y - start.Y).F, tz = ((Fpu)end.Z - start.Z).F;
        float length = Fpu.Sqrt((((Fpu)tx * tx) + ((Fpu)ty * ty)) + ((Fpu)tz * tz)).F;
        // A timed action's object is not shielded by what stands between (QTask_IsQTaskTypeDerived(..., GenericTBA_eQTaskType)).
        if (length == 0f || penetration == 0f || self is Interaction.Igi2GenericTbaTask)
        {
            return damage;
        }

        // The line is a weapon's: the target and the explosion's owner never stop it, other objects as they answer a bullet.
        _blast.Includes = Igi2GunShot.WeaponCollisionIncludes(new WeaponCollisionQuery(hit.Owner, self, 1f));

        Fpu step = (Fpu)StillStep / length;
        tx = ((Fpu)tx * step).F;
        ty = ((Fpu)ty * step).F;
        tz = (step * tz).F;
        while (true)
        {
            _blast.Starts[0] = start;
            _blast.Ends[0] = end;
            _blast.Count = 1;
            collision.TraceLines(_blast, BlastGroups);
            WorldIntersection crossed = _blast.Intersections[0];
            if (!crossed.IsHit || (crossed.EnterMaterial ?? crossed.LeaveMaterial) is not int row)
            {
                break;
            }

            float power = materials[row]?.PenetrationPower ?? 0f;
            penetration = ((Fpu)penetration - power).F;
            if (penetration <= 0f)
            {
                penetration = 0f;
                break;
            }

            Vector3d p = crossed.Position;
            start = new Vector3d(((Fpu)tx + p.X).Value, ((Fpu)ty + p.Y).Value, ((Fpu)tz + p.Z).Value);
        }

        return initial == 0f ? damage : ((Fpu)penetration / initial) * damage;
    }

    /// <summary>
    /// An explosion goes off, <c>Explosion0_CreateExplosion</c> (<c>igi2 0x508F10</c>): its fireballs, its smoke, its fragments,
    /// its sound, and, if it does damage, every target told.
    /// </summary>
    internal void GoOff(Igi2Explosion explosion)
    {
        Igi2ExplosionRequest r = explosion.Request;
        Fpu reach = (Fpu)r.FalloffRange + r.FullRange;
        if (reach.Value == 0.0)
        {
            return;
        }

        float radius = reach.F;
        Vector3d at = r.Position;
        ExplosionFireBall? first = null;
        if (r.FireballCount != 0)
        {
            first = new ExplosionFireBall(at, radius, 30, 24, 0, 30);
            Dynamic.Add(first);
        }

        for (int i = 0; i < r.FireballCount - 1; i++)
        {
            Fpu r1 = Draw(), r2 = Draw(), r3 = Draw(), r4 = Draw(), r5 = Draw(), r6 = Draw();
            int life = Fpu.ToInt32(((r5 + 1f) * (double)first!.TotalLifetime) * 0.5f);
            Vector3d p = first.Position;
            Vector3d placed = new(
                (((r2 - 0.5) * reach) + p.X).Value,
                (((r3 - 0.5) * reach) + p.Y).Value,
                (((r4 - 0.5) * reach) + p.Z).Value);
            int delay = Fpu.ToInt32((r6 * 6f) + (((Fpu)(double)(i + 1) / (double)r.FireballCount) * 30f));
            int fade = Fpu.ToInt32((Fpu)0.9f * (double)life);
            float size = (((r1 * 0.25f) + 0.5f) * radius).F;
            Dynamic.Add(new ExplosionFireBall(placed, size, life, fade, delay, life));
        }

        int smokeLife = r.FireballCount > 1
            ? Fpu.ToInt32(((((Fpu)60f - ((Fpu)60f / (double)r.FireballCount)) - 1f) + 60f))
            : 60;
        if (r.FireballCount != 0)
        {
            Dynamic.Add(new ExplosionSmoke(Random, at, (reach * 0.8999999761581421).F, smokeLife));
        }

        if (r.FragmentCount != 0)
        {
            for (int i = 0; i < r.FragmentCount; i++)
            {
                float speed = ((Draw() * ((Fpu)r.MaxSpeed - r.MinSpeed)) + r.MinSpeed).F;
                Vector3f cone = RandomUnitVectorInCone(OpenIGI.Game.Physics.RigidBodyKernels.Normalise<Fpu>(explosion.Direction), r.MaxAlpha);
                Vector3f velocity = new(((Fpu)cone.X * speed).F, ((Fpu)cone.Y * speed).F, ((Fpu)cone.Z * speed).F);
                _ = Random.NextRaw();
                explosion.Fragments.Add(NewFragment(at, velocity, 204.8f));
            }

            explosion.Fragments.IsStarted = true;
        }

        Effects.Sound(r.Sound, at);
        if (r.Damage == 0f)
        {
            return;
        }

        Igi2ExplosionHit hit = new(
            this,
            at,
            r.Owner is Humans.Igi2Human human ? human.Team : -1,
            r.Damage,
            r.FullRange,
            r.FalloffRange,
            ShakeTime,
            r.WeaponType,
            r.Owner,
            r.Falloff);
        foreach (IExplosionHitTarget target in _targets.ToArray())
        {
            if (target is IIgi2ExplosionTarget same)
            {
                same.ExplosionHit(hit);
            }
            else
            {
                target.ExplosionHit(hit.ToShared());
            }
        }
    }

    /// <inheritdoc/>
    public void Sound(string sound, Vector3d position) => Effects.Sound(sound, position);

    /// <inheritdoc/>
    /// <remarks>A grenade's events go to every team (<c>Grenade_SendDetectionEvent</c>).</remarks>
    public void AiEvent(int aiEvent, int flags, Vector3d position, float range) => Effects.AiEvent(aiEvent, flags, position, range, -1);

    /// <summary>The fixed 40.96-unit step past each face the blast's trace makes.</summary>
    private const float StillStep = 40.96f;

    private Fpu Draw() => Random.NextDouble();

    /// <summary>
    /// A fragment, <c>Explosion0_FragmentNew</c> (<c>igi2 0x508D40</c>): turned at random by up to a hundredth of a turn about each
    /// axis, scaled by its radius over 2048. This build throws every fragment without a trail.
    /// </summary>
    private ExplosionFragment NewFragment(Vector3d position, Vector3f velocity, float radius)
    {
        float gamma = (Draw() * 0.031415928f).F;
        float beta = (Draw() * 0.031415928f).F;
        float alpha = (Draw() * 0.031415928f).F;
        return new ExplosionFragment
        {
            Position = position,
            Velocity = velocity,
            Orientation = EngineEuler.Rotation(alpha, beta, gamma),
            Scale = ((Fpu)radius * 0.00048828125f).F,
        };
    }

    /// <summary>
    /// A random unit vector within a cone about a direction, <c>GameFunctions_GetRandomUnitVectorInCone</c>
    /// (<c>igi2 0x48D510</c>): a random turn about the axis and a random share of the cone's tangent out from it.
    /// </summary>
    private Vector3f RandomUnitVectorInCone(Vector3f axis, float cone)
    {
        Fpu turn = Draw() * 6.283185307179589;
        Fpu tilt = new Fpu(Math.Tan(cone)) * Draw();
        float sx = (Fpu.Sin(turn) * tilt).F;
        Fpu cy = tilt * Fpu.Cos(turn);
        Fpu norm = (Fpu)1.0 / Fpu.Sqrt((((Fpu)sx * sx) + (cy * cy)) + 1.0);
        float lx = ((Fpu)sx * norm).F;
        float ly = norm.F;
        Fpu lz = cy * norm;

        Vector3f d = axis;
        Fpu z0 = (Fpu)d.Z * 0f;
        Vector3f across = new((((Fpu)d.Y - z0)).F, (-((Fpu)d.X - z0)).F, (((Fpu)d.X * 0f) - ((Fpu)d.Y * 0f)).F);
        Vector3f up;
        float length = Fpu.Sqrt((((Fpu)across.X * across.X) + ((Fpu)across.Y * across.Y)) + ((Fpu)across.Z * across.Z)).F;
        if (length <= 0.1f)
        {
            Fpu x0 = (Fpu)d.X * 0f, y0 = (Fpu)d.Y * 0f;
            Vector3f side = OpenIGI.Game.Physics.RigidBodyKernels.Normalise<Fpu>(new Vector3f((z0 - y0).F, (-((Fpu)d.Z - x0)).F, ((Fpu)d.Y - x0).F));
            across = OpenIGI.Game.Physics.RigidBodyKernels.Normalise<Fpu>(new Vector3f(
                (((Fpu)d.Y * side.Z) - ((Fpu)side.Y * d.Z)).F,
                (-(((Fpu)d.X * side.Z) - ((Fpu)side.X * d.Z))).F,
                (((Fpu)side.Y * d.X) - ((Fpu)side.X * d.Y)).F));
            up = side;
        }
        else
        {
            Fpu inverse = (Fpu)1.0 / length;
            across = new Vector3f(((Fpu)across.X * inverse).F, ((Fpu)across.Y * inverse).F, ((Fpu)across.Z * inverse).F);
            up = OpenIGI.Game.Physics.RigidBodyKernels.Normalise<Fpu>(new Vector3f(
                (((Fpu)across.Y * d.Z) - ((Fpu)across.Z * d.Y)).F,
                (-(((Fpu)across.X * d.Z) - ((Fpu)across.Z * d.X))).F,
                (((Fpu)across.X * d.Y) - ((Fpu)across.Y * d.X)).F));
        }

        return new Vector3f(
            ((((Fpu)across.X * lx) + ((Fpu)up.X * lz)) + ((Fpu)d.X * ly)).F,
            ((((Fpu)across.Y * lx) + ((Fpu)up.Y * lz)) + ((Fpu)d.Y * ly)).F,
            ((((Fpu)across.Z * lx) + ((Fpu)up.Z * lz)) + ((Fpu)d.Z * ly)).F);
    }
}

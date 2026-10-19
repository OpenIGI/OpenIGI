using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>How an explosion's damage falls away past its full range, <c>ExplosionDamageFalloffType_e</c>.</summary>
public enum Igi2ExplosionFalloff
{
    /// <summary>In a straight line to nothing at the end of the falloff.</summary>
    Linear = 0,

    /// <summary>As the square of the share left, <c>EXPLOSIONDAMAGE_EXPONENTIAL_FALLOFF</c>.</summary>
    Exponential = 1,
}

/// <summary>An explosion as <c>Explosion0_NewEx</c> (<c>igi2 0x50A650</c>) is given it.</summary>
/// <param name="Position">Where it goes off.</param>
/// <param name="Count">How many times it goes off, <c>nExplosion</c>.</param>
/// <param name="Period">The ticks before it first goes off, and the bound on each wait after, <c>nExplosionTime</c>.</param>
/// <param name="Damage">The damage at its heart, <c>vDamage</c>.</param>
/// <param name="FullRange">How far the full damage reaches, <c>vFullRange</c>.</param>
/// <param name="FalloffRange">How far past that it falls away, <c>vFallOffRange</c>.</param>
/// <param name="BurnRange">The size of the mark it burns, <c>vBurnRange</c>.</param>
/// <param name="FragmentCount">How many fragments it throws, <c>nFragment</c>.</param>
/// <param name="FireballCount">How many fireballs it shows, <c>nFireball</c>.</param>
/// <param name="Ground">What it struck, which is burnt; <see langword="null"/> for nothing.</param>
/// <param name="FragmentModel">The fragments' model.</param>
/// <param name="Sound">Its sound.</param>
/// <param name="AiEvent">The AI event it raises, <c>eHumanAIEvent</c>; -1 for none.</param>
/// <param name="AiEventFlags">The event's flags.</param>
/// <param name="AiEventRange">How far the event carries.</param>
/// <param name="Direction">The direction the fragments are thrown about.</param>
/// <param name="MaxAlpha">The cone the fragments are thrown in, in radians.</param>
/// <param name="MinSpeed">The slowest a fragment leaves.</param>
/// <param name="MaxSpeed">The fastest.</param>
/// <param name="WeaponType">The weapon it came from, or <see langword="null"/>.</param>
/// <param name="Owner">Who set it off, or <see langword="null"/>.</param>
/// <param name="Falloff">How its damage falls away.</param>
public sealed record Igi2ExplosionRequest(
    Vector3d Position,
    int Count,
    int Period,
    float Damage,
    float FullRange,
    float FalloffRange,
    float BurnRange,
    int FragmentCount,
    int FireballCount,
    Igi2Intersection? Ground,
    string FragmentModel,
    string Sound,
    int AiEvent,
    int AiEventFlags,
    float AiEventRange,
    Vector3f Direction,
    float MaxAlpha,
    float MinSpeed,
    float MaxSpeed,
    Igi2WeaponTypeTask? WeaponType,
    IIgi2WeaponOwner? Owner,
    Igi2ExplosionFalloff Falloff);

/// <summary>What an explosion tells each of its targets, <c>ExplosionHitContext_t</c>.</summary>
/// <param name="World">The explosion world, which resolves what the blast does through what lies between.</param>
/// <param name="Position">Where it went off.</param>
/// <param name="Team">The team of the human who set it off, or -1.</param>
/// <param name="Damage">The damage at its heart.</param>
/// <param name="FullRange">How far the full damage reaches.</param>
/// <param name="FalloffRange">How far past that it falls away.</param>
/// <param name="ExplosionTime">The ticks its shake runs over, <c>nExplosionTime</c>: 90.</param>
/// <param name="WeaponType">The weapon it came from, or <see langword="null"/>.</param>
/// <param name="Owner">Who set it off, or <see langword="null"/>.</param>
/// <param name="Falloff">How its damage falls away.</param>
public readonly record struct Igi2ExplosionHit(
    Igi2ExplosionWorld World,
    Vector3d Position,
    int Team,
    float Damage,
    float FullRange,
    float FalloffRange,
    int ExplosionTime,
    Igi2WeaponTypeTask? WeaponType,
    IIgi2WeaponOwner? Owner,
    Igi2ExplosionFalloff Falloff)
{
    /// <summary>The explosion as either game's targets read it: its fields, and the whole of it as the context.</summary>
    /// <returns>The shared explosion.</returns>
    public ExplosionHit ToShared() => new(Position, Team, Damage, FullRange, FalloffRange, Owner, this);

    /// <summary>The damage it does a target, <c>GameFunctions_ResolveExplosionHit</c> (<c>igi2 0x48D1F0</c>).</summary>
    /// <param name="target">Where the target is, <c>tPos</c>.</param>
    /// <param name="radius">How big the target is.</param>
    /// <returns>The damage.</returns>
    public Fpu Resolve(Vector3d target, float radius) => World.Resolve(this, target, radius, null);

    /// <summary>The damage it does a target, as <see cref="Resolve(Vector3d, float)"/>, the target named so the line through walls passes it.</summary>
    /// <param name="target">Where the target is.</param>
    /// <param name="radius">The target's size.</param>
    /// <param name="self">The target, <c>ptQTask</c>.</param>
    /// <returns>The damage.</returns>
    public Fpu Resolve(Vector3d target, float radius, object? self) => World.Resolve(this, target, radius, self);
}

/// <summary>Something an explosion can hurt: a member of <c>GameFunctions_ptExplosionQTaskList</c>.</summary>
/// <remarks>The engine's <see cref="IExplosionHitTarget"/>, read in this game's terms: an explosion this game set off is taken whole.</remarks>
public interface IIgi2ExplosionTarget : IExplosionHitTarget
{
    /// <summary>An explosion reaches it, its <c>ExplosionHit</c> handler.</summary>
    /// <param name="hit">The explosion.</param>
    void ExplosionHit(in Igi2ExplosionHit hit);

    /// <inheritdoc/>
    void IExplosionHitTarget.ExplosionHit(in ExplosionHit hit)
    {
        if (hit.Context is Igi2ExplosionHit own)
        {
            ExplosionHit(own);
        }
    }
}

/// <summary>What an explosion shows and sounds, behind which none of its logic depends.</summary>
public interface IIgi2ExplosionEffects
{
    /// <summary>The explosion burns a mark on what it struck (<c>BulletHole_eCreateBulletHoleTaskEvent</c>).</summary>
    /// <param name="ground">What it struck.</param>
    /// <param name="radius">The mark's size.</param>
    void BurnMark(Igi2Intersection ground, float radius);

    /// <summary>The explosion sounds (<c>SoundSys_NewSoundFn</c> placed at the blast).</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="position">Where.</param>
    void Sound(string sound, Vector3d position);

    /// <summary>The explosion raises an AI event (<c>HumanAI_SendEvent</c>, <c>igi2 0x460A90</c>).</summary>
    /// <param name="aiEvent">The event.</param>
    /// <param name="flags">Its flags.</param>
    /// <param name="position">Where.</param>
    /// <param name="range">How far it carries.</param>
    /// <param name="team">The team it concerns: an explosion's, 1; a grenade's, every team.</param>
    void AiEvent(int aiEvent, int flags, Vector3d position, float range, int team = 1);
}

/// <summary>An explosion's effects that do nothing.</summary>
public sealed class Igi2NoExplosionEffects : IIgi2ExplosionEffects
{
    /// <inheritdoc/>
    public void BurnMark(Igi2Intersection ground, float radius)
    {
    }

    /// <inheritdoc/>
    public void Sound(string sound, Vector3d position)
    {
    }

    /// <inheritdoc/>
    public void AiEvent(int aiEvent, int flags, Vector3d position, float range, int team = 1)
    {
    }
}

/// <summary>
/// An explosion going off, <c>Explosion0_s</c> (<c>Explosion0_RunHandler</c>, <c>igi2 0x50A100</c>): it waits out its timer,
/// goes off, and waits a random share of its period before each further time.
/// </summary>
public sealed class Igi2Explosion : IIgi2DynamicTask
{
    private readonly Igi2ExplosionWorld _world;

    /// <summary>Initializes a new instance of the <see cref="Igi2Explosion"/> class: the task half of <c>Explosion0_NewEx</c>.</summary>
    /// <param name="world">The world it goes off in.</param>
    /// <param name="request">What it is.</param>
    /// <param name="direction">Its fragments' direction, normalised.</param>
    /// <param name="fragments">The container its fragments fly in.</param>
    internal Igi2Explosion(Igi2ExplosionWorld world, Igi2ExplosionRequest request, Vector3f direction, Igi2FragmentContainer fragments)
    {
        _world = world;
        Request = request;
        Direction = direction;
        Fragments = fragments;
        Count = request.Count;
        Timer = request.Period;
    }

    /// <summary>Gets what it is.</summary>
    public Igi2ExplosionRequest Request { get; }

    /// <summary>Gets its fragments' direction, <c>tDirection</c>.</summary>
    public Vector3f Direction { get; }

    /// <summary>Gets the container its fragments fly in.</summary>
    public Igi2FragmentContainer Fragments { get; }

    /// <summary>Gets how many more times it goes off, <c>nExplosion</c>.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the ticks before it next goes off.</summary>
    public int Timer { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Each time but the last is followed by a wait of the run stream's next word modulo the period.</remarks>
    public bool Run()
    {
        if (Timer != 0)
        {
            Timer--;
        }

        if (Timer != 0 || Count == 0)
        {
            return true;
        }

        _world.GoOff(this);
        Count--;
        if (Count == 0)
        {
            return false;
        }

        Timer = (int)(_world.Random.NextRaw() % (uint)Request.Period);
        return true;
    }
}

/// <summary>An explosion's fireball, <c>FireBall</c> (<c>Explosion0_FireBallRunHandler</c>, <c>igi2 0x5098B0</c>): a model that waits, then shows its life out.</summary>
/// <param name="position">Where it is.</param>
/// <param name="size">How big, <c>vRadius</c>.</param>
/// <param name="lifetime">The ticks it shows.</param>
/// <param name="fadeout">The ticks it fades over.</param>
/// <param name="delay">The ticks before it shows.</param>
/// <param name="animationDuration">The ticks its animation runs.</param>
public sealed class Igi2FireBall(Vector3d position, float size, int lifetime, int fadeout, int delay, int animationDuration) : IIgi2DynamicTask
{
    /// <summary>The fireball's model, <c>1006_01_1</c>.</summary>
    public const string Model = "1006_01_1";

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; } = position;

    /// <summary>Gets how big it is.</summary>
    public float Size { get; } = size;

    /// <summary>Gets the ticks it shows for, <c>nLifetime</c>.</summary>
    public int TotalLifetime { get; } = lifetime;

    /// <summary>Gets the ticks it has left.</summary>
    public int Lifetime { get; private set; } = lifetime;

    /// <summary>Gets the ticks it fades over.</summary>
    public int Fadeout { get; } = fadeout;

    /// <summary>Gets the ticks before it shows.</summary>
    public int Delay { get; private set; } = delay;

    /// <summary>Gets the ticks its animation runs.</summary>
    public int AnimationDuration { get; } = animationDuration;

    /// <inheritdoc/>
    public bool Run()
    {
        if (Delay > 0)
        {
            Delay--;
            return true;
        }

        if (Lifetime == 0)
        {
            return false;
        }

        Lifetime--;
        return true;
    }
}

/// <summary>A puff of smoke particles an explosion's smoke starts, <c>Explosion0_AddSmoke</c> (<c>igi2 0x508610</c>).</summary>
/// <param name="Flags">The particle system's graphics flags.</param>
/// <param name="Position">Where.</param>
/// <param name="Size">How big.</param>
/// <param name="SizeDelta">How much it grows a tick.</param>
/// <param name="Lifetime">The ticks it lives.</param>
/// <param name="Fadetime">The ticks it fades over.</param>
/// <param name="Intensity">How bright.</param>
/// <param name="Rotation">Its turn.</param>
/// <param name="RotationDelta">How fast it turns.</param>
public sealed record Igi2SmokePuff(
    int Flags,
    Vector3d Position,
    float Size,
    float SizeDelta,
    int Lifetime,
    int Fadetime,
    float Intensity,
    float Rotation,
    float RotationDelta);

/// <summary>
/// An explosion's smoke, <c>ExplosionSmoke</c> (<c>Explosion0_SmokeRunHandler</c>, <c>igi2 0x5095C0</c>): a white puff when it
/// starts and a black one late in its life.
/// </summary>
public sealed class Igi2ExplosionSmoke : IIgi2DynamicTask
{
    private readonly IgiRandom _random;
    private readonly List<Igi2SmokePuff> _puffs = [];
    private bool _isBlack;

    /// <summary>Initializes a new instance of the <see cref="Igi2ExplosionSmoke"/> class.</summary>
    /// <param name="random">The run stream.</param>
    /// <param name="position">Where.</param>
    /// <param name="radius">How big, <c>vRadius</c>.</param>
    /// <param name="lifetime">The ticks it lives.</param>
    public Igi2ExplosionSmoke(IgiRandom random, Vector3d position, float radius, int lifetime)
    {
        _random = random;
        Position = position;
        Radius = radius;
        TotalLifetime = lifetime;
        Lifetime = lifetime;
    }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets how big its puffs start, <c>vRadius</c>.</summary>
    public float Radius { get; }

    /// <summary>Gets the ticks it lives, <c>nTotalLifetime</c>.</summary>
    public int TotalLifetime { get; }

    /// <summary>Gets the ticks it has left, <c>nLifetime</c>.</summary>
    public int Lifetime { get; private set; }

    /// <summary>Gets the puffs it has started.</summary>
    public IReadOnlyList<Igi2SmokePuff> Puffs => _puffs;

    /// <summary>Gets the black puff's brightness, set each tick once it has started.</summary>
    public float BlackIntensity { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// On its first tick the white puff starts: it lives a tenth of the smoke's life, fades over four fifths to all of it at
    /// random, and is a tenth bright and a tenth more at random. At nine tenths of its life the black puff starts, which brightens
    /// to three tenths at half the smoke's life and fades out by nine tenths.
    /// </remarks>
    public bool Run()
    {
        if (Lifetime <= 0)
        {
            return false;
        }

        int total = TotalLifetime;
        if (Lifetime == total)
        {
            int partLife = Fpu.ToInt32((Fpu)(double)total * 0.1f);
            Fpu fade = ((Draw() * 0.2f) + 0.8f) * (double)total;
            int partFade = Fpu.ToInt32(fade);
            float sizeDelta = ((Fpu)1f / (double)total).F;
            float intensity = ((Draw() + 1f) * 0.1f).F;
            _puffs.Add(Puff(0x1000008, sizeDelta, partLife, partFade, intensity));
        }

        if (Lifetime == Fpu.ToInt32((Fpu)0.9f * (double)total))
        {
            float sizeDelta = ((Fpu)1f / (double)total).F;
            _puffs.Add(Puff(0x1000010, sizeDelta, total, 0, 0.001f));
            _isBlack = true;
        }

        if (_isBlack)
        {
            Fpu life = (double)Lifetime, whole = (double)total;
            Fpu half = whole * 0.5f;
            Fpu share = life.Value <= half.Value ? life / half : (Fpu)1f - ((life - half) / ((whole * 0.9f) - half));
            BlackIntensity = (share * 0.3f).F;
        }

        Lifetime--;
        return true;
    }

    private Fpu Draw() => _random.NextDouble();

    /// <summary><c>Explosion0_AddSmoke</c>: a particle at the smoke, turned at random and turning at random.</summary>
    private Igi2SmokePuff Puff(int flags, float sizeDelta, int lifetime, int fadetime, float intensity)
    {
        float rotation = (Draw() * 6.2831855f).F;
        float rotationDelta = (((Fpu)0.5 - Draw()) * 0.018849557f).F;
        return new Igi2SmokePuff(flags, Position, Radius, sizeDelta, lifetime, fadetime, intensity, rotation, rotationDelta);
    }
}

/// <summary>One fragment an explosion throws, <c>Fragment</c>: a point that flies, turning, for the container's lifetime.</summary>
public sealed class Igi2Fragment
{
    /// <summary>Gets or sets where it is.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its velocity, units a tick.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets its orientation, nine floats row by row.</summary>
    public float[] Orientation { get; init; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets its scale, the fragment's radius over 2048.</summary>
    public float Scale { get; init; }

    /// <summary>Gets or sets the ticks it has left, <c>nLifetime</c>.</summary>
    public int Lifetime { get; set; }
}

/// <summary>
/// The fragments an explosion throws, <c>SimpointObjContainer</c> (<c>SimpointObjContainer_RunHandler</c>, <c>igi2 0x5B6080</c>):
/// they fall and slow, and turn together at a spin that dies away; none of it touches anything.
/// </summary>
/// <param name="model">The fragments' model.</param>
/// <param name="lifetime">The ticks each lives.</param>
/// <param name="gravityScale">What the gravity is scaled by: the explosion's full range over 4551.</param>
/// <param name="spin">The spin about each axis, radians a tick.</param>
public sealed class Igi2FragmentContainer(string model, int lifetime, float gravityScale, Vector3f spin) : IIgi2DynamicTask
{
    /// <summary>What a fragment's velocity keeps each tick, <c>0x3F7AE148</c>.</summary>
    public const float AirResistance = 0.98f;

    /// <summary>The fragments' gravity before the explosion's scale, <c>0x4232674F</c>.</summary>
    public const float Gravity = 44.600887f;

    private readonly List<Igi2Fragment> _fragments = [];
    private Vector3f _spin = spin;

    /// <summary>Gets the fragments' model.</summary>
    public string Model { get; } = model;

    /// <summary>Gets the ticks each fragment lives.</summary>
    public int Lifetime { get; } = lifetime;

    /// <summary>Gets the fragments still flying.</summary>
    public IReadOnlyList<Igi2Fragment> Flying => _fragments;

    /// <summary>Gets or sets a value indicating whether the fragments have been thrown; an emptied container that has is gone.</summary>
    public bool IsStarted { get; set; }

    /// <summary>Adds a fragment, <c>SimpointObjContainer_Add</c> (<c>igi2 0x5B68E0</c>).</summary>
    /// <param name="fragment">The fragment.</param>
    public void Add(Igi2Fragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        fragment.Lifetime = Lifetime;
        _fragments.Add(fragment);
    }

    /// <inheritdoc/>
    public bool Run()
    {
        if (_fragments.Count == 0)
        {
            return !IsStarted;
        }

        Fpu air = AirResistance;
        float gravity = (((Fpu)gravityScale * Gravity) * air).F;
        _spin = new Vector3f(((Fpu)_spin.X * 0.99f).F, ((Fpu)_spin.Y * 0.99f).F, ((Fpu)_spin.Z * 0.99f).F);
        float[] turn = Igi2Pose.Rotation(_spin.X, _spin.Y, _spin.Z);
        foreach (Igi2Fragment f in _fragments)
        {
            Vector3f v = f.Velocity;
            float vx = (air * v.X).F, vy = (air * v.Y).F, vz = ((air * v.Z) - gravity).F;
            f.Velocity = new Vector3f(vx, vy, vz);
            f.Position = new Vector3d(((Fpu)vx + f.Position.X).Value, ((Fpu)vy + f.Position.Y).Value, ((Fpu)vz + f.Position.Z).Value);
            float[] o = f.Orientation;
            float[] turned = new float[9];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    turned[(3 * r) + c] = ((((Fpu)turn[3 * r] * o[c]) + ((Fpu)turn[(3 * r) + 2] * o[6 + c])) + ((Fpu)turn[(3 * r) + 1] * o[3 + c])).F;
                }
            }

            turned.CopyTo(o, 0);
        }

        // SimpointObj_RunHandler (igi2 0x5B66E0): each counts its life down, and is gone once it has none.
        for (int i = _fragments.Count - 1; i >= 0; i--)
        {
            Igi2Fragment f = _fragments[i];
            if (f.Lifetime > 0)
            {
                f.Lifetime--;
            }
            else
            {
                _fragments.RemoveAt(i);
            }
        }

        return true;
    }
}

/// <summary>
/// Where explosions go off: the run stream they draw from, the collision their blast is traced through, the targets they
/// reach (<c>GameFunctions_ptExplosionQTaskList</c>) and the level's dynamic tasks they and what they leave run in.
/// </summary>
/// <param name="random">The run stream, <c>Game.tRunRandomState</c>.</param>
/// <param name="collision">The collision a blast is traced through to a target.</param>
/// <param name="materials">The level's materials, whose penetration a blast loses through each face.</param>
/// <param name="dynamic">The level's dynamic tasks.</param>
public sealed partial class Igi2ExplosionWorld(IgiRandom random, IIgi2CollisionQuery collision, Igi2GameMaterials materials, Igi2DynamicTasks dynamic)
    : IIgi2GrenadeEffects
{
    /// <summary>The query groups a blast is traced against: the ground, the splines, the solid objects and the forests.</summary>
    public const uint BlastGroups = 0x98000000 | Igi2CollisionWorld.ForestGroup;

    /// <summary>How far above the blast and the target the line between them runs: 409.6 units.</summary>
    public const double BlastLift = 409.6000061035156;

    /// <summary>The ticks a blast's shake runs over, <c>nExplosionTime</c> in the hit context.</summary>
    public const int ShakeTime = 90;

    private readonly List<IExplosionHitTarget> _targets = [];
    private readonly Igi2LineTrace _blast = new(1);

    /// <summary>Gets the run stream.</summary>
    public IgiRandom Random { get; } = random;

    /// <summary>Gets the level's dynamic tasks.</summary>
    public Igi2DynamicTasks Dynamic { get; } = dynamic;

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
        Igi2Intersection? ground,
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

        Vector3f direction = Physics.Igi2RigidBody.Normalised(request.Direction);
        if (request.Ground is { HitObject: Igi2RigidObject or Igi2SplineSegment } struck)
        {
            Effects.BurnMark(struck, request.BurnRange);
        }

        (float a, float b, float c) = spin ?? ((Draw() * 0.6283185f).F, (Draw() * 0.6283185f).F, (Draw() * 0.6283185f).F);
        Igi2FragmentContainer fragments = new(
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

        if (damage.Value == 0.0)
        {
            return damage;
        }

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
            Igi2Intersection crossed = _blast.Intersections[0];
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
        Igi2FireBall? first = null;
        if (r.FireballCount != 0)
        {
            first = new Igi2FireBall(at, radius, 30, 24, 0, 30);
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
            Dynamic.Add(new Igi2FireBall(placed, size, life, fade, delay, life));
        }

        int smokeLife = r.FireballCount > 1
            ? Fpu.ToInt32(((((Fpu)60f - ((Fpu)60f / (double)r.FireballCount)) - 1f) + 60f))
            : 60;
        if (r.FireballCount != 0)
        {
            Dynamic.Add(new Igi2ExplosionSmoke(Random, at, (reach * 0.8999999761581421).F, smokeLife));
        }

        if (r.FragmentCount != 0)
        {
            for (int i = 0; i < r.FragmentCount; i++)
            {
                float speed = ((Draw() * ((Fpu)r.MaxSpeed - r.MinSpeed)) + r.MinSpeed).F;
                Vector3f cone = RandomUnitVectorInCone(Physics.Igi2RigidBody.Normalised(explosion.Direction), r.MaxAlpha);
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
    private Igi2Fragment NewFragment(Vector3d position, Vector3f velocity, float radius)
    {
        float gamma = (Draw() * 0.031415928f).F;
        float beta = (Draw() * 0.031415928f).F;
        float alpha = (Draw() * 0.031415928f).F;
        return new Igi2Fragment
        {
            Position = position,
            Velocity = velocity,
            Orientation = Igi2Pose.Rotation(alpha, beta, gamma),
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
            Vector3f side = Physics.Igi2RigidBody.Normalised(new Vector3f((z0 - y0).F, (-((Fpu)d.Z - x0)).F, ((Fpu)d.Y - x0).F));
            across = Physics.Igi2RigidBody.Normalised(new Vector3f(
                (((Fpu)d.Y * side.Z) - ((Fpu)side.Y * d.Z)).F,
                (-(((Fpu)d.X * side.Z) - ((Fpu)side.X * d.Z))).F,
                (((Fpu)side.Y * d.X) - ((Fpu)side.X * d.Y)).F));
            up = side;
        }
        else
        {
            Fpu inverse = (Fpu)1.0 / length;
            across = new Vector3f(((Fpu)across.X * inverse).F, ((Fpu)across.Y * inverse).F, ((Fpu)across.Z * inverse).F);
            up = Physics.Igi2RigidBody.Normalised(new Vector3f(
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

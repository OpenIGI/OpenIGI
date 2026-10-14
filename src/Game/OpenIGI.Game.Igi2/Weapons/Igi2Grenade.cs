using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a grenade does when its fuse runs out, <c>GRENADE_EXPLOSIONTYPE_*</c> (<c>Grenade_Open</c>, <c>igi2 0x485390</c>).</summary>
public static class Igi2GrenadeKind
{
    /// <summary><c>GRENADE_EXPLOSIONTYPE_NORMAL</c>: it explodes.</summary>
    public const int Normal = 0;

    /// <summary><c>GRENADE_EXPLOSIONTYPE_FLASHBANG</c>: it flashes.</summary>
    public const int Flashbang = 1;

    /// <summary><c>GRENADE_EXPLOSIONTYPE_SMOKE</c>: it smokes.</summary>
    public const int Smoke = 2;
}

/// <summary>
/// An explosion a grenade asks for, the arguments <c>Grenade_RunHandler</c> (<c>igi2 0x484260</c>) gives <c>Explosion0_New</c>
/// (<c>0x50A4E0</c>) besides its fixed ones.
/// </summary>
/// <param name="Position">Where it goes off: the grenade.</param>
/// <param name="Ground">What the probe straight down from there struck, which the explosion marks.</param>
/// <param name="DamageFactor">The launcher's <c>Explosion Damage Factor</c>.</param>
/// <param name="FullRange">How far the full damage reaches, the <c>Explosion Radius</c> in units.</param>
/// <param name="FalloffRange">How far beyond it the damage falls away, the <c>Explosion Falloff</c> in units.</param>
/// <param name="Thrower">Who threw it, or <see langword="null"/>.</param>
public readonly record struct Igi2GrenadeExplosion(
    Vector3d Position,
    Igi2Intersection Ground,
    float DamageFactor,
    float FullRange,
    float FalloffRange,
    IIgi2WeaponOwner? Thrower)
{
    /// <summary>The explosion's model, <c>1009_01_1</c>.</summary>
    public const string Model = "1009_01_1";

    /// <summary>The explosion's sound, <c>explo_grenade</c>.</summary>
    public const string Sound = "explo_grenade";
}

/// <summary>What a grenade sounds, tells the AI and does at its end, behind which its flight does not depend.</summary>
public interface IIgi2GrenadeEffects
{
    /// <summary>Starts a flash and tells every human, <c>flashbang.c FlashBang_New</c>.</summary>
    /// <param name="position">Where it went off.</param>
    /// <param name="ground">The ground probe's intersection.</param>
    /// <param name="team">The thrower's team, or minus one.</param>
    void FlashBang(Vector3d position, Igi2Intersection ground, int team)
    {
    }

    /// <summary>Creates the plume at fuse-out, <c>grenade.c Grenade_RunHandler</c>.</summary>
    /// <param name="position">Where the grenade is.</param>
    /// <returns>The plume, or null when effects are disabled.</returns>
    Igi2SmokeTask? Smoke(Vector3d position) => null;

    /// <summary>Tells humans in the smoke sphere, <c>grenade.c Grenade_RunHandler</c>.</summary>
    /// <param name="position">The sphere centre.</param>
    /// <param name="range">The sphere radius.</param>
    void SmokeArea(Vector3d position, float range)
    {
    }

    /// <summary>A grenade explodes, <c>Explosion0_New</c> (<c>igi2 0x50A4E0</c>).</summary>
    /// <param name="explosion">The explosion.</param>
    void Explode(in Igi2GrenadeExplosion explosion);

    /// <summary>A grenade sounds (<c>SoundSys_NewSoundFn</c> placed at it).</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="position">Where.</param>
    void Sound(string sound, Vector3d position);

    /// <summary>A grenade tells the AI of itself, <c>Grenade_SendDetectionEvent</c> (<c>igi2 0x483EC0</c>).</summary>
    /// <param name="aiEvent">The event.</param>
    /// <param name="flags">Its flags.</param>
    /// <param name="position">Where the grenade is.</param>
    /// <param name="range">How far it carries.</param>
    void AiEvent(int aiEvent, int flags, Vector3d position, float range);
}

/// <summary>A grenade's effects that do nothing: the grenade is silent, and at its end simply gone.</summary>
public sealed class Igi2NoGrenadeEffects : IIgi2GrenadeEffects
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2NoGrenadeEffects Instance { get; } = new();

    /// <inheritdoc/>
    public void Explode(in Igi2GrenadeExplosion explosion)
    {
    }

    /// <inheritdoc/>
    public void Sound(string sound, Vector3d position)
    {
    }

    /// <inheritdoc/>
    public void AiEvent(int aiEvent, int flags, Vector3d position, float range)
    {
    }
}

/// <summary>A thrown grenade in flight, <c>Grenade_s</c>: a physics object with a fuse.</summary>
public sealed class Igi2Grenade : IIgi2DynamicTask
{
    /// <summary>The gravity a grenade falls by, <c>Grenade_Move</c> (<c>igi2 0x483FE0</c>): units a tick squared.</summary>
    public const float Gravity = 44.600887f;

    /// <summary>What its velocity keeps each tick: all of it.</summary>
    public const float AirResistance = 1f;

    /// <summary>What its spin keeps each tick and each contact.</summary>
    public const float SpinResistance = 0.92f;

    /// <summary>How far down the fuse's end looks for the ground under it: 4096 units (<c>0xC0B0000000000000</c>).</summary>
    public const double GroundProbe = -4096.0;

    /// <summary>The sound a grenade makes when it strikes something, <c>grenade_bnc_1</c>.</summary>
    public const string BounceSound = "grenade_bnc_1";

    /// <summary>The AI event a flying grenade raises every fifth tick of its fuse.</summary>
    public const int FlightEvent = 0x10;

    /// <summary>The AI event a grenade's bounce raises.</summary>
    public const int BounceEvent = 0x11;

    /// <summary>How far a bounce is heard: 20480 units.</summary>
    public const float BounceRange = 20480f;

    /// <summary>The ticks without a contact after which the next one bounces again.</summary>
    public const int BounceRest = 10;

    private readonly Igi2LineTrace _ground = new(1);
    private readonly Igi2Grenades? _owner;
    private Igi2SmokeTask? _smoke;
    private bool _smokeStarted;

    /// <summary>Gets the ticks since smoke began, <c>grenade.c Grenade_RunHandler</c>.</summary>
    public int SmokeTicks { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="Igi2Grenade"/> class.</summary>
    /// <param name="thrown">The throw it leaves the hand with.</param>
    /// <param name="model">The model it is drawn as.</param>
    /// <param name="body">Its physics object, or <see langword="null"/> when its weapon names no physics type: it does not move.</param>
    /// <param name="kind">What it does when its fuse runs out, <see cref="Igi2GrenadeKind"/>.</param>
    /// <param name="owner">The grenade world whose world and effects its ticks use, or <see langword="null"/> for one only run by hand.</param>
    public Igi2Grenade(in Igi2GrenadeThrow thrown, string model, Igi2PhysicsObject? body, int kind, Igi2Grenades? owner = null)
    {
        _owner = owner;
        Throw = thrown;
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Body = body;
        Kind = kind;
        ExplodeTimer = thrown.ExplodeTime < 0 ? 0 : thrown.ExplodeTime;
    }

    /// <summary>Gets the throw it left the hand with.</summary>
    public Igi2GrenadeThrow Throw { get; }

    /// <summary>Gets the model it is drawn as: the weapon's <c>ProjectileModel</c> tag.</summary>
    public string Model { get; }

    /// <summary>Gets its physics object, <c>tPhysicsObj</c>.</summary>
    public Igi2PhysicsObject? Body { get; }

    /// <summary>Gets what it does when its fuse runs out.</summary>
    public int Kind { get; }

    /// <summary>Gets the ticks left on its fuse, <c>nExplodeTimer</c>.</summary>
    public int ExplodeTimer { get; private set; }

    /// <summary>Gets a value indicating whether it has bounced since it last went ten ticks without a contact, <c>Grenade_s + 0x774</c>.</summary>
    public bool HasBounced { get; private set; }

    /// <summary>Gets the ticks it has run, the clock its bounces are timed by.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets the tick it last bounced on, <c>Grenade_s + 0x770</c>.</summary>
    public int BounceTick { get; private set; }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position => Body?.Position ?? Throw.Position;

    /// <summary>Gets its orientation, nine floats row by row.</summary>
    public float[] Orientation => Body?.Orientation ?? Throw.Orientation;

    /// <inheritdoc/>
    /// <remarks>Run in the level's dynamic tasks, with its grenade world's world and effects.</remarks>
    /// <exception cref="InvalidOperationException">The grenade belongs to no grenade world.</exception>
    public bool Run() =>
        _owner is { } owner ? Run(owner.World, owner.Effects) : throw new InvalidOperationException("The grenade belongs to no grenade world.");

    /// <summary>A tick of the grenade, <c>Grenade_RunHandler</c> (<c>igi2 0x484260</c>).</summary>
    /// <param name="world">What it flies through.</param>
    /// <param name="effects">What its end shows and does.</param>
    /// <returns><see langword="false"/> once it is gone.</returns>
    /// <remarks>
    /// <para>
    /// While its fuse burns it counts down and moves (<c>Grenade_Move</c>, <c>0x483FE0</c>): unless at rest, its physics
    /// object takes a tick under the grenade's gravity and damping.
    /// </para>
    /// <para>
    /// An ordinary grenade tells the AI of itself on every fifth tick of its fuse. A contact at the last move sounds the
    /// bounce, which an ordinary grenade tells the AI of too, once until ten ticks go by without one.
    /// </para>
    /// <para>
    /// The tick the fuse is out, the ground under it is probed and, for an ordinary grenade, the explosion goes off with the
    /// launcher's parameters, and the grenade is gone. Smoke keeps moving until its plume has faded.
    /// </para>
    /// </remarks>
    public bool Run(Igi2PhysicsWorld world, IIgi2GrenadeEffects effects)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(effects);
        int tick = Ticks++;
        if (ExplodeTimer != 0)
        {
            ExplodeTimer--;
            if (ExplodeTimer % 5 == 0 && Kind == Igi2GrenadeKind.Normal)
            {
                effects.AiEvent(FlightEvent, 1, Position, -1f);
            }

            ControlSound(effects, tick);
            if (Body is { } body && !body.IsIdle())
            {
                body.Update(Gravity, AirResistance, SpinResistance, world);
            }

            return true;
        }

        if (Kind == Igi2GrenadeKind.Smoke)
        {
            // grenade.c Grenade_RunHandler: the creation tick does not increment the smoke clock.
            if (!_smokeStarted)
            {
                effects.Sound("explo_smoke", Position);
                _smoke = effects.Smoke(Position);
                _smokeStarted = true;
            }
            else
            {
                SmokeTicks++;
                if (SmokeTicks % 30 == 0)
                {
                    effects.SmokeArea(Position, 12288f);
                }

                if (SmokeTicks > 900 && _smoke is { } stopping)
                {
                    stopping.GenerateFactor = 0.0;
                }

                if (SmokeTicks > 1080)
                {
                    if (_smoke?.System is { } finished)
                    {
                        finished.IsSelfDestruct = true;
                    }

                    return false;
                }
            }

            ControlSound(effects, tick);
            if (Body is { } smokingBody && !smokingBody.IsIdle())
            {
                smokingBody.Update(Gravity, AirResistance, SpinResistance, world);
            }

            if (_smoke is { } smoke)
            {
                smoke.Position = Position;
                smoke.Emit();
            }

            return true;
        }

        _ground.Starts[0] = Position;
        _ground.Includes = world.Includes;
        world.Query.NextZ(_ground, [GroundProbe], 0xFFFFFFFF);
        if (Kind == Igi2GrenadeKind.Flashbang)
        {
            // grenade.c Grenade_RunHandler: both ranges are 122880 and the context carries the thrower's team.
            effects.FlashBang(Position, _ground.Intersections[0], Throw.Thrower is Humans.Igi2Human human ? human.Team : -1);
        }

        if (Kind == Igi2GrenadeKind.Normal)
        {
            Igi2GrenadeParameters p = Throw.Parameters;
            effects.Explode(new Igi2GrenadeExplosion(
                Position,
                _ground.Intersections[0],
                p.ExplosionDamageFactor,
                ((Fpu)p.ExplosionRadiusMeters * 4096f).F,
                ((Fpu)p.ExplosionFalloffMeters * 4096f).F,
                Throw.Thrower));
        }

        return false;
    }

    /// <summary>
    /// <c>Grenade_ControlSound</c> (<c>igi2 0x483F20</c>): a contact at the last move bounces, if it has not since it last went
    /// ten ticks without one.
    /// </summary>
    private void ControlSound(IIgi2GrenadeEffects effects, int tick)
    {
        if (Body is { ContactCount: not 0 })
        {
            if (HasBounced)
            {
                return;
            }

            effects.Sound(BounceSound, Position);
            if (Kind == Igi2GrenadeKind.Normal)
            {
                effects.AiEvent(BounceEvent, 2, Position, BounceRange);
            }

            HasBounced = true;
            BounceTick = tick;
        }
        else if (tick - BounceTick > BounceRest)
        {
            HasBounced = false;
        }
    }
}

/// <summary>
/// The level's grenades, <c>Grenade_ptQTaskList</c>: each thrown into the level's dynamic tasks, flown each tick there, and
/// gone when spent.
/// </summary>
/// <param name="types">The physics object types, which a grenade's weapon's <c>Fixed Model</c> names.</param>
/// <param name="models">The level's models, by name.</param>
/// <param name="world">What the grenades fly through.</param>
/// <param name="dynamic">The level's dynamic tasks the grenades run in.</param>
/// <param name="logger">Where a grenade with no physics type is reported.</param>
public sealed class Igi2Grenades(
    Igi2PhysicsObjectTypes types,
    Func<string, MefModel?> models,
    Igi2PhysicsWorld world,
    Igi2DynamicTasks dynamic,
    IOpenIgiLogger? logger = null) : IIgi2GrenadeWorld
{
    /// <summary>The weapon tag that names a grenade's model, <c>ProjectileModel</c>.</summary>
    public const string ProjectileModelTag = "ProjectileModel";

    private readonly IOpenIgiLogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets what the grenades fly through.</summary>
    public Igi2PhysicsWorld World { get; } = world;

    /// <summary>Gets the level's dynamic tasks the grenades run in.</summary>
    public Igi2DynamicTasks Dynamic { get; } = dynamic;

    /// <summary>Gets the grenades in flight, in the order they were thrown.</summary>
    public IEnumerable<Igi2Grenade> Flying => Dynamic.Tasks.OfType<Igi2Grenade>();

    /// <summary>Gets or sets what a grenade's end shows and does.</summary>
    public IIgi2GrenadeEffects Effects { get; set; } = Igi2NoGrenadeEffects.Instance;

    /// <summary>What a weapon's grenade does at its end: its physics type's second value, <c>GRENADE_EXPLOSIONTYPE_*</c>.</summary>
    /// <param name="weapon">The weapon.</param>
    /// <returns>The kind, normal when the type has no second value, or <see langword="null"/> when the weapon has no physics type.</returns>
    public int? KindOf(Igi2WeaponTypeTask weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (!types.TryFind(weapon.FixedModel, out Igi2PhysicsObjectType type))
        {
            return null;
        }

        return type.Parameters.Count > 1 ? type.Parameters[1].ToInt32() : Igi2GrenadeKind.Normal;
    }

    /// <summary>
    /// A grenade leaves the hand, <c>Grenade_New</c> (<c>igi2 0x484FD0</c>): drawn as its weapon's <c>ProjectileModel</c>, a
    /// physics object of the type its <c>Fixed Model</c> names, at the throw's place with its speed and orientation.
    /// </summary>
    /// <param name="grenade">The throw.</param>
    /// <remarks>
    /// The contact points sit on the drawn model's magic vertices. A weapon whose model has no physics type would stop the
    /// original on its first tick; here its grenade lies where it was thrown until its fuse runs out.
    /// </remarks>
    public void Launch(in Igi2GrenadeThrow grenade)
    {
        Igi2WeaponTypeTask weapon = grenade.WeaponType;
        string model = string.Empty;
        foreach (Engine.Tasks.GameTask child in weapon.Children)
        {
            if (child is Igi2TagItemString16Task { TagName: ProjectileModelTag } tag)
            {
                model = tag.Value;
                break;
            }
        }

        Igi2PhysicsObject? body = null;
        int kind = KindOf(weapon) ?? Igi2GrenadeKind.Normal;
        if (types.TryFind(weapon.FixedModel, out Igi2PhysicsObjectType type))
        {
            body = new Igi2PhysicsObject(type, models(model), grenade.Position, Vector3f.One);
            body.Body.Velocity = grenade.Speed;
            body.Body.UpdateSpeedApproximation();
            grenade.Orientation.AsSpan(0, 9).CopyTo(body.Orientation);
        }
        else
        {
            _logger.Warning("Grenade", $"No physics object type for {weapon.FixedModel}: the grenade does not fly.");
        }

        Dynamic.Add(new Igi2Grenade(grenade, model, body, kind, this));
    }
}

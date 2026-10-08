using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;

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

/// <summary>What a grenade's end shows and does, behind which its flight does not depend.</summary>
public interface IIgi2GrenadeEffects
{
    /// <summary>A grenade explodes, <c>Explosion0_New</c> (<c>igi2 0x50A4E0</c>).</summary>
    /// <param name="explosion">The explosion.</param>
    void Explode(in Igi2GrenadeExplosion explosion);
}

/// <summary>A grenade's end that does nothing: the grenade is simply gone.</summary>
public sealed class Igi2NoGrenadeEffects : IIgi2GrenadeEffects
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2NoGrenadeEffects Instance { get; } = new();

    /// <inheritdoc/>
    public void Explode(in Igi2GrenadeExplosion explosion)
    {
    }
}

/// <summary>A thrown grenade in flight, <c>Grenade_s</c>: a physics object with a fuse.</summary>
public sealed class Igi2Grenade
{
    /// <summary>The gravity a grenade falls by, <c>Grenade_Move</c> (<c>igi2 0x483FE0</c>): units a tick squared.</summary>
    public const float Gravity = 44.600887f;

    /// <summary>What its velocity keeps each tick: all of it.</summary>
    public const float AirResistance = 1f;

    /// <summary>What its spin keeps each tick and each contact.</summary>
    public const float SpinResistance = 0.92f;

    /// <summary>How far down the fuse's end looks for the ground under it: 4096 units (<c>0xC0B0000000000000</c>).</summary>
    public const double GroundProbe = -4096.0;

    private readonly Igi2LineTrace _ground = new(1);

    /// <summary>Initializes a new instance of the <see cref="Igi2Grenade"/> class.</summary>
    /// <param name="thrown">The throw it leaves the hand with.</param>
    /// <param name="model">The model it is drawn as.</param>
    /// <param name="body">Its physics object, or <see langword="null"/> when its weapon names no physics type: it does not move.</param>
    /// <param name="kind">What it does when its fuse runs out, <see cref="Igi2GrenadeKind"/>.</param>
    public Igi2Grenade(in Igi2GrenadeThrow thrown, string model, Igi2PhysicsObject? body, int kind)
    {
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

    /// <summary>Gets where it is.</summary>
    public Vector3d Position => Body?.Position ?? Throw.Position;

    /// <summary>Gets its orientation, nine floats row by row.</summary>
    public float[] Orientation => Body?.Orientation ?? Throw.Orientation;

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
    /// The tick the fuse is out, the ground under it is probed and, for an ordinary grenade, the explosion goes off with the
    /// launcher's parameters, and the grenade is gone. Flash and smoke grenades, the AI's hearing of a grenade every fifth
    /// tick and its sounds are not ported yet.
    /// </para>
    /// </remarks>
    public bool Run(Igi2PhysicsWorld world, IIgi2GrenadeEffects effects)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(effects);
        if (ExplodeTimer != 0)
        {
            ExplodeTimer--;
            if (Body is { } body && !body.IsIdle())
            {
                body.Update(Gravity, AirResistance, SpinResistance, world);
            }

            return true;
        }

        _ground.Starts[0] = Position;
        _ground.Includes = world.Includes;
        world.Query.NextZ(_ground, [GroundProbe], 0xFFFFFFFF);
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
}

/// <summary>The level's grenades in flight, <c>Grenade_ptQTaskList</c>: thrown into it, flown each tick, and gone when spent.</summary>
/// <param name="types">The physics object types, which a grenade's weapon's <c>Fixed Model</c> names.</param>
/// <param name="models">The level's models, by name.</param>
/// <param name="world">What the grenades fly through.</param>
/// <param name="logger">Where a grenade with no physics type is reported.</param>
public sealed class Igi2Grenades(
    Igi2PhysicsObjectTypes types,
    Func<string, MefModel?> models,
    Igi2PhysicsWorld world,
    IOpenIgiLogger? logger = null) : IIgi2GrenadeWorld
{
    /// <summary>The weapon tag that names a grenade's model, <c>ProjectileModel</c>.</summary>
    public const string ProjectileModelTag = "ProjectileModel";

    private readonly List<Igi2Grenade> _flying = [];
    private readonly IOpenIgiLogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the grenades in flight, in the order they were thrown.</summary>
    public IReadOnlyList<Igi2Grenade> Flying => _flying;

    /// <summary>Gets or sets what a grenade's end shows and does.</summary>
    public IIgi2GrenadeEffects Effects { get; set; } = Igi2NoGrenadeEffects.Instance;

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
        int kind = Igi2GrenadeKind.Normal;
        if (types.TryFind(weapon.FixedModel, out Igi2PhysicsObjectType type))
        {
            body = new Igi2PhysicsObject(type, models(model), grenade.Position, Vector3f.One);
            body.Body.Velocity = grenade.Speed;
            body.Body.UpdateSpeedApproximation();
            grenade.Orientation.AsSpan(0, 9).CopyTo(body.Orientation);
            if (type.Parameters.Count > 1)
            {
                kind = type.Parameters[1].ToInt32();
            }
        }
        else
        {
            _logger.Warning("Grenade", $"No physics object type for {weapon.FixedModel}: the grenade does not fly.");
        }

        _flying.Add(new Igi2Grenade(grenade, model, body, kind));
    }

    /// <summary>A tick of every grenade in flight thrown before it; those that are spent are gone.</summary>
    public void Run()
    {
        int count = _flying.Count;
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            Igi2Grenade grenade = _flying[i];
            if (grenade.Run(world, Effects))
            {
                _flying[kept++] = grenade;
            }
        }

        _flying.RemoveRange(kept, count - kept);
    }
}

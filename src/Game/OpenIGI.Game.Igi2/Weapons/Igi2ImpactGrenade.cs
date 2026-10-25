using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// An impact grenade's parameters, read from its fire type's tags as <c>ImpactGrenade_CreateWeaponTypeParametersHandler</c>
/// (<c>igi2 0x4CC7B0</c>) reads them.
/// </summary>
/// <param name="Model">What it is drawn as, <c>Model Name</c>.</param>
/// <param name="Damage">The damage at the blast's heart, <c>Explosion Damage</c>, one unless tagged.</param>
/// <param name="Radius">The blast's reach in metres, <c>Explosion Radius</c>, one unless tagged.</param>
public readonly record struct Igi2ImpactGrenadeParameters(string Model, float Damage, float Radius)
{
    /// <summary>Reads a fire type's impact grenade tags.</summary>
    /// <param name="fireType">The fire type, whose children carry the tags.</param>
    /// <returns>The parameters.</returns>
    public static Igi2ImpactGrenadeParameters Of(GameTask fireType)
    {
        ArgumentNullException.ThrowIfNull(fireType);
        string model = string.Empty;
        float damage = 1f, radius = 1f;
        foreach (GameTask child in fireType.Children)
        {
            switch (child)
            {
                case Igi2TagItemString16Task { TagName: "Model Name" } tag:
                    model = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Explosion Damage" } tag:
                    damage = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Explosion Radius" } tag:
                    radius = tag.Value;
                    break;
            }
        }

        return new Igi2ImpactGrenadeParameters(model, damage, radius);
    }
}

/// <summary>
/// A grenade fired from a launcher that bursts on what it strikes, <c>ImpactGrenade</c>: it flies under its own gravity, trailing
/// smoke, and the first thing its path meets sets it off.
/// </summary>
/// <remarks>
/// <para>
/// Fired (<c>ImpactGrenade_CreateProjectileHandler</c>, <c>0x4CC8B0</c>) along the shot's forward column at the gun's speed, metres a
/// second made units a tick; the shooter both made it and is passed over by its path.
/// </para>
/// <para>
/// Each tick (<c>ImpactGrenade_RunHandler</c>, <c>0x4CC2F0</c>) its speed keeps 0.995 of itself, after 35.68 more of fall, and it
/// moves by it; the line from where it was to where it is goes as a weapon's does. Struck, it goes off where it struck: one blast
/// of its damage, full over its radius and falling away over as much again, three fragments and a fireball, <c>explo_proxy</c>,
/// and AI event 20 heard 75 metres off; and it is gone. Otherwise its trail takes a puff, three draws of the run stream.
/// </para>
/// </remarks>
public sealed class Igi2ImpactGrenade : IIgi2DynamicTask
{
    /// <summary>A metre a second, in units a tick.</summary>
    public const double MetresPerSecond = 136.53334045410156;

    /// <summary>Its blast's fragments' model.</summary>
    public const string FragmentModel = "1009_01_1";

    /// <summary>Its blast's sound.</summary>
    public const string BlastSound = "explo_proxy";

    private readonly WorldLineTrace _path = new(1);
    private readonly Igi2ExplosionWorld _explosions;
    private readonly IIgi2CollisionQuery _collision;
    private readonly IIgi2WeaponOwner? _shooter;
    private readonly Particle2DSystem? _trail;
    private Vector3f _speed;

    /// <summary>Initializes a new instance of the <see cref="Igi2ImpactGrenade"/> class: fired, <c>ImpactGrenade_New</c> (<c>igi2 0x4CC940</c>).</summary>
    /// <param name="launch">The shot.</param>
    /// <param name="parameters">Its fire type's tags.</param>
    /// <param name="explosions">Where its blast goes off and its trail's stream draws from.</param>
    /// <param name="collision">What its path is traced through.</param>
    public Igi2ImpactGrenade(in Igi2ProjectileLaunch launch, Igi2ImpactGrenadeParameters parameters, Igi2ExplosionWorld explosions, IIgi2CollisionQuery collision)
    {
        ArgumentNullException.ThrowIfNull(explosions);
        ArgumentNullException.ThrowIfNull(collision);
        _explosions = explosions;
        _collision = collision;
        _shooter = launch.Thrower;
        Parameters = parameters;
        Position = launch.Position;
        Orientation = [.. launch.Orientation];
        float[] m = launch.Orientation;
        Fpu speed = (Fpu)launch.Speed * MetresPerSecond;
        _speed = new Vector3f(
            ((Fpu)((((Fpu)m[2] + m[0]) * 0.0) + m[1]).F * speed).F,
            ((Fpu)((((Fpu)m[5] + m[3]) * 0.0) + m[4]).F * speed).F,
            (speed * ((((Fpu)m[8] + m[6]) * 0.0) + m[7]).F).F);

        // ImpactGrenade_CreateHandler: its trail, grey smoke a metre and a half across.
        _trail = explosions.Particles?.New(
            "smoke1.spr", false, 0, default, new Vector3f(127f, 127f, 127f), 614.4f, 614.4f, 819200f, 0x1000008, 0, 0.1f, 0f, 20, null);
    }

    /// <summary>Gets its tags.</summary>
    public Igi2ImpactGrenadeParameters Parameters { get; }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets its orientation, nine floats row by row.</summary>
    public float[] Orientation { get; }

    /// <summary>Gets its speed, units a tick.</summary>
    public Vector3f Speed => _speed;

    /// <inheritdoc/>
    public bool Run()
    {
        Vector3d from = Position, to = from;
        ImpactRoundFlight.Step<Fpu>(ref _speed, ref to);
        Position = to;

        _path.Starts[0] = from;
        _path.Ends[0] = Position;
        _path.Count = 1;
        _path.Includes = Igi2GunShot.WeaponCollisionIncludes(new WeaponCollisionQuery(_shooter, _shooter, 1f));
        _collision.TraceLines(_path, uint.MaxValue);
        WorldIntersection hit = _path.Intersections[0];
        if (hit.IsHit)
        {
            float reach = ((Fpu)Parameters.Radius * 2048.0).F;
            _explosions.New(
                hit.Position,
                1,
                1,
                Parameters.Damage,
                reach,
                reach,
                4096f,
                3,
                1,
                hit,
                FragmentModel,
                BlastSound,
                20,
                0x402,
                307200f,
                null,
                _shooter,
                Igi2ExplosionFalloff.Linear);
            return false;
        }

        if (_trail is { } trail)
        {
            IgiRandom random = _explosions.Random;
            float rise = (((Fpu)Draw(random) * 0.75 + 0.15) * 4096000.0 * 9.259259059035685e-06).F;
            float intensity = ((Draw(random) + 1.0) * 0.35).F;
            Fpu spin = Draw(random);
            float rotation = ((((spin - 0.5) + spin) - 0.5) * 0.05235987901687622 + 0.012799081392586231).F;
            trail.AddParticle(Position, new Vector3f(0f, 0f, rise), intensity, rotation, 0f, 1f, 7, 7);
        }

        return true;
    }

    // Random_GetRandomNumberReal32: the draw's low sixteen bits over 65536.
    private static Fpu Draw(IgiRandom random) => (Fpu)(double)(ushort)random.NextRaw() * 0.0000152587890625;
}

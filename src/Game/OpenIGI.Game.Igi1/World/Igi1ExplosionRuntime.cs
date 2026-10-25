using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One <c>Explosion0</c> as Project I.G.I.'s callers ask for it (<c>Explosion_Spawn</c>, <c>0x480FE0</c>).</summary>
/// <param name="Position">Where it goes off, in world units.</param>
/// <param name="Count">How many times it goes off.</param>
/// <param name="Period">The ticks before it first goes off, and the bound on each wait after.</param>
/// <param name="Damage">The blast's damage factor; none sends no blast.</param>
/// <param name="FullRange">The full-damage radius, which also sizes the fire, the smoke and the fragments' fall.</param>
/// <param name="FalloffRange">How far past the full range the damage falls away.</param>
/// <param name="Fragments">How many fragments it throws.</param>
/// <param name="Fireballs">How many FireBalls it lights.</param>
/// <param name="Sound">The sound it goes off with; empty for none.</param>
/// <param name="Blast">What its blast does as it goes off, when its damage is not nothing.</param>
/// <param name="SoundPriority">The voice priority its sound plays at.</param>
public sealed record Igi1ExplosionRequest(
    Vector3d Position,
    int Count,
    int Period,
    float Damage,
    float FullRange,
    float FalloffRange,
    int Fragments,
    int Fireballs,
    string Sound,
    Action? Blast = null,
    int SoundPriority = 90);

/// <summary>Project I.G.I.'s <c>Explosion0</c>: what it is and the fragments it throws, going off through <c>0x47F200</c>.</summary>
public sealed class Igi1Explosion : Explosion0
{
    private readonly Igi1ExplosionRuntime _runtime;

    internal Igi1Explosion(Igi1ExplosionRuntime runtime, Igi1ExplosionRequest request, Vector3f direction, ExplosionFragments fragments)
        : base(runtime.Random, request.Count, request.Period)
    {
        _runtime = runtime;
        Request = request;
        Direction = direction;
        Fragments = fragments;
    }

    /// <summary>Gets what it is.</summary>
    public Igi1ExplosionRequest Request { get; }

    /// <summary>Gets the axis its fragments are thrown about, normalised.</summary>
    public Vector3f Direction { get; }

    /// <summary>Gets the container its fragments fly in.</summary>
    public ExplosionFragments Fragments { get; }

    /// <inheritdoc/>
    protected override void GoOff() => _runtime.Burst(this);
}

/// <summary>
/// Project I.G.I.'s explosions as its simulation runs them: <c>Explosion0</c> and what it leaves -- FireBalls, the smoke
/// and the fragments -- on the engine's own random stream, in the order the original draws it.
/// </summary>
/// <remarks>
/// <para>
/// The tasks are the ones both titles share (<see cref="Explosion0"/>, <see cref="ExplosionFireBall"/>,
/// <see cref="ExplosionSmoke"/>, <see cref="ExplosionFragments"/>); the numbers are this title's. A blast lights its first
/// FireBall for 75 ticks, a quarter smaller when others share it, and scatters the rest through its range, each lit 75/n
/// ticks after the last and up to fourteen later; its smoke lasts 150 ticks, more when several FireBalls burn; its
/// fragments fly for 45 ticks.
/// </para>
/// <para>
/// A fragment leaves along the blast's axis tipped by a draw of the cone (<see cref="FragmentDirection"/>), not as the
/// sequel throws it. The smoke's two puffs are particle systems of <c>smoke2.spr</c> on the impact effects' module, so the
/// one particle drawer shows them.
/// </para>
/// </remarks>
public sealed class Igi1ExplosionRuntime : IExplosionScene
{
    /// <summary>The fragments' and the blast's model, <c>1009_01_1</c>.</summary>
    public const string FragmentModel = "1009_01_1";

    /// <summary>The smoke's sprite.</summary>
    public const string SmokeSprite = "smoke2.spr";

    // 0x47F200's first FireBall: 75 ticks, its animation as long, and a quarter smaller when it is not alone.
    private const int FireballTicks = 75;
    private const float SharedFireballScale = 0.75f;

    // 0x47F200's smoke: 150 ticks, its puffs 0.9 of the range across.
    private const float SmokeFrames = 150f;
    private const double SmokeScale = 0.8999999761581421;

    // 0x47F200's stagger spread, taken off each FireBall's delay (0x5341AC).
    private const float DelaySpread = -15f;

    // 0x4810A0's fragments: the container's 45 ticks, its tumble, and each piece 204.8 across.
    private const int FragmentTicks = 45;
    private const float SpinRange = 0.62831855f;
    private const float GravityPerRange = 0.00021972656f;
    private const float FragmentSize = 204.8f;
    private const float FragmentScale = 0.00048828125f;
    private const float TumbleRange = 0.03141593f;

    // 0x480FE0: the cone and the fragments' speeds.
    private const float Cone = 1.5707964f;
    private const float MinSpeed = 272f;
    private const float MaxSpeed = 1360f;

    // 0x47EAD0: the smoke rises at 2.23 a tick, a grey of 12.7.
    private const float SmokeRise = 2.23f;
    private const float SmokeColour = 12.7f;
    private const float SmokeDepth = 819200f;

    private readonly DynamicTasks _tasks = new();
    private readonly List<ExplosionFireBall> _fireBalls = [];
    private readonly List<ExplosionFragments> _fragmentGroups = [];

    /// <summary>Initializes a new instance of the <see cref="Igi1ExplosionRuntime"/> class.</summary>
    /// <param name="random">The engine's own stream, which every draw of an explosion comes from.</param>
    /// <param name="particles">The particle module the smoke's systems are made in, or <see langword="null"/> for none.</param>
    public Igi1ExplosionRuntime(IgiRandom random, Particle2DModule? particles)
    {
        ArgumentNullException.ThrowIfNull(random);
        Random = random;
        Particles = particles;
    }

    /// <summary>Gets the stream explosions draw from.</summary>
    public IgiRandom Random { get; }

    /// <summary>Gets the particle module the smoke is made in.</summary>
    public Particle2DModule? Particles { get; }

    /// <summary>Gets or sets what an explosion plays as it goes off: the sound, where, and at what priority.</summary>
    public Action<string, Vector3d, int>? Sound { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<ExplosionFireBall> FireBalls => _fireBalls;

    /// <inheritdoc/>
    public IReadOnlyList<ExplosionFragments> FragmentGroups => _fragmentGroups;

    /// <summary>Starts an explosion thrown about a random axis, <c>Explosion_Spawn</c> (<c>0x480FE0</c>) into <c>0x4810A0</c>.</summary>
    /// <param name="request">The explosion.</param>
    /// <returns>The explosion; one that went off on its first run and has no more to do is already gone.</returns>
    /// <remarks>
    /// The axis is three draws, mostly up; the fragments' container spins at three more. The explosion runs its first tick at
    /// once, as the task made in its maker's run does, so a blast with no delay -- or the one tick a grenade's and a rocket's
    /// ask for -- goes off as it is set.
    /// </remarks>
    public Igi1Explosion Spawn(Igi1ExplosionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Vector3f axis = new(
            Random.NextSingle() - 0.5f,
            Random.NextSingle() - 0.5f,
            Random.NextSingle() - 0.3f);
        if (axis.X != 0f || axis.Y != 0f || axis.Z != 0f)
        {
            axis = axis.Normalized();
        }

        Vector3f spin = new(
            Random.NextSingle() * SpinRange,
            Random.NextSingle() * SpinRange,
            Random.NextSingle() * SpinRange);
        ExplosionFragments fragments = new(FragmentModel, FragmentTicks, request.FullRange * GravityPerRange, spin);
        Igi1Explosion explosion = new(this, request, axis, fragments);
        _fragmentGroups.Add(fragments);
        _tasks.Add(fragments);
        if (explosion.Run())
        {
            _tasks.Add(explosion);
        }

        return explosion;
    }

    /// <summary>Runs every explosion task a tick, in the order they were made.</summary>
    public void Tick()
    {
        _tasks.Run();
        _fireBalls.RemoveAll(fireBall => !_tasks.Contains(fireBall));
        _fragmentGroups.RemoveAll(group => !_tasks.Contains(group));
    }

    /// <summary>Drops every explosion, as a level that ends does.</summary>
    public void Clear()
    {
        _tasks.Clear();
        _fireBalls.Clear();
        _fragmentGroups.Clear();
    }

    /// <summary>The way one fragment leaves the blast (<c>0x47F200</c>).</summary>
    /// <param name="axis">The blast's axis.</param>
    /// <param name="around">The turn about the axis, in radians.</param>
    /// <param name="off">The tip away from the axis, in radians.</param>
    /// <returns>The unit direction.</returns>
    /// <remarks>
    /// A frame is stood on the axis (<c>0x4B4010</c>: a level side, the axis, and the third square to both), turned by
    /// <paramref name="around"/> about the axis and tipped <paramref name="off"/> away from it; the fragment leaves along
    /// the tipped axis. The first turn is a draw over half a turn only, so every fragment of a blast flies to the same side
    /// of it, at any angle from straight along the axis to the cone's edge.
    /// </remarks>
    public static Vector3f FragmentDirection(Vector3f axis, float around, float off)
    {
        Vector3f d = axis.Normalized();
        Vector3f level = d.X == 0f && d.Y == 0f ? new Vector3f(0f, 1f, 0f) : new Vector3f(d.Y, -d.X, 0f).Normalized();
        Vector3f side = Vector3f.Cross(level, d);
        Vector3f other = Vector3f.Cross(d, side).Normalized();
        side = side.Normalized();
        float tip = MathF.Sin(off);
        return (side * (MathF.Sin(around) * tip)) - (other * (MathF.Cos(around) * tip)) + (d * MathF.Cos(off));
    }

    // 0x47F200: the FireBalls, the smoke, the fragments, the sound, then the blast.
    internal void Burst(Igi1Explosion explosion)
    {
        Igi1ExplosionRequest r = explosion.Request;
        double size = r.FullRange;
        if (size == 0.0)
        {
            return;
        }

        Vector3d at = r.Position;
        ExplosionFireBall? first = null;
        if (r.Fireballs > 0)
        {
            first = new ExplosionFireBall(
                at,
                r.Fireballs > 1 ? (float)(size * SharedFireballScale) : (float)size,
                FireballTicks,
                60,
                0,
                FireballTicks);
            Add(first);
        }

        for (int i = 0; i < r.Fireballs - 1;)
        {
            float scale = ((Random.NextSingle() * 0.25f) + 0.5f) * (float)size;
            _ = Random.NextSingle();
            double dx = (Random.NextSingle() - 0.5) * size;
            double dy = (Random.NextSingle() - 0.5) * size;
            double dz = (Random.NextSingle() - 0.5) * size;
            // The game runs the FPU chopping (0xC00), so every round_int here truncates toward zero.
            int life = (int)((Random.NextSingle() + 1f) * first!.TotalLifetime * 0.5f);
            i++;
            int early = (int)(Random.NextSingle() * DelaySpread);
            int delay = (FireballTicks / r.Fireballs * i) - early;
            Add(new ExplosionFireBall(
                new Vector3d(at.X + dx, at.Y + dy, at.Z + dz),
                scale,
                life,
                (int)(life * 0.9f),
                delay,
                life));
        }

        int frames = r.Fireballs > 1
            ? (int)(SmokeFrames - (SmokeFrames / (double)r.Fireballs) - 1.0 + SmokeFrames)
            : (int)SmokeFrames;
        if (r.Fireballs != 0)
        {
            _tasks.Add(new ExplosionSmoke(Random, at, (float)(size * SmokeScale), frames) { Emit = EmitSmoke });
        }

        if (r.Fragments > 0)
        {
            for (int i = 0; i < r.Fragments; i++)
            {
                float speed = (Random.NextSingle() * (MaxSpeed - MinSpeed)) + MinSpeed;
                float around = Random.NextSingle() * MathF.PI;
                float off = Random.NextSingle() * Cone;
                Vector3f direction = FragmentDirection(explosion.Direction, around, off);
                _ = Random.NextSingle();

                // 0x47F0D0: the piece's own small turn, drawn about Z, then Y, then X.
                float az = Random.NextSingle() * TumbleRange;
                float ay = Random.NextSingle() * TumbleRange;
                float ax = Random.NextSingle() * TumbleRange;
                explosion.Fragments.Add(new ExplosionFragment
                {
                    Position = at,
                    PreviousPosition = at,
                    Velocity = direction * speed,
                    Orientation = EngineEuler.Rotation(ax, ay, az),
                    Scale = FragmentSize * FragmentScale,
                });
            }

            explosion.Fragments.IsStarted = true;
        }

        if (r.Sound.Length > 0)
        {
            Sound?.Invoke(r.Sound, at, r.SoundPriority);
        }

        if (r.Damage != 0f)
        {
            r.Blast?.Invoke();
        }
    }

    private void Add(ExplosionFireBall fireBall)
    {
        _fireBalls.Add(fireBall);
        _tasks.Add(fireBall);
    }

    // 0x47EAD0: a puff is a particle system of its own, of one particle, rising at 2.23 a tick.
    private Particle2DSystem? EmitSmoke(ExplosionSmokePuff puff)
    {
        if (Particles is not { } module)
        {
            return null;
        }

        Particle2DLook look = new(
            SmokeSprite,
            IsAnimated: false,
            Frames: module.FramesOf(SmokeSprite),
            AnimationDelta: 0,
            new Vector3f(SmokeColour, SmokeColour, SmokeColour),
            puff.Size,
            puff.Size,
            SmokeDepth,
            puff.Flags | Particle2DFlags.DepthTested);
        Particle2DSystem system = module.New(look, new Vector3f(0f, 0f, SmokeRise), puff.SizeDelta, 1, collider: null);
        system.AddParticle(puff.Position, new Vector3f(0f, 0f, 1f), puff.Intensity, puff.RotationDelta, puff.Rotation, 1f, puff.Lifetime, puff.Fadetime);
        return system;
    }
}

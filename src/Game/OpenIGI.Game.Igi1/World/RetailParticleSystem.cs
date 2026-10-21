using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One live particle of a <see cref="RetailParticleSystem"/>.</summary>
/// <remarks>
/// The original's record is 88 bytes and every field here is one of its slots, named for what
/// <c>0x4FD660</c> (the step) and <c>0x4FD020</c> (the draw) actually do with it:
/// <list type="table">
/// <item><term><c>+0</c></term><description><see cref="Position"/>, three <b>doubles</b>.</description></item>
/// <item><term><c>+24</c></term><description><see cref="Velocity"/>, three floats, in units per tick.</description></item>
/// <item><term><c>+36</c></term><description><see cref="CollisionCountdown"/> — seeded from a global
/// counter modulo sixteen, so the batch's collision traces are spread over sixteen ticks.</description></item>
/// <item><term><c>+38</c></term><description><see cref="HoldTicks"/>, the ticks before the fade starts.</description></item>
/// <item><term><c>+40</c></term><description><see cref="Frame"/>, the sprite frame.</description></item>
/// <item><term><c>+42</c></term><description><see cref="FrameTimer"/>.</description></item>
/// <item><term><c>+44</c></term><description><see cref="Spin"/>, radians per tick.</description></item>
/// <item><term><c>+48</c></term><description><see cref="Alpha"/> — the draw reads it as the quad's
/// alpha, never as a size.</description></item>
/// <item><term><c>+52</c></term><description><see cref="FadePerTick"/>.</description></item>
/// <item><term><c>+56</c></term><description><see cref="Angle"/>, the accumulated roll.</description></item>
/// <item><term><c>+60</c></term><description><see cref="Growth"/> — the draw multiplies the system's
/// size by this, and only when the system declares a growth rate.</description></item>
/// <item><term><c>+64</c></term><description><see cref="CollisionFrom"/>, where the next collision
/// segment starts.</description></item>
/// </list>
/// </remarks>
public struct RetailParticle
{
    /// <summary>Where the particle is, in world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Where its next collision segment starts — the position at its last collision tick.</summary>
    public Vector3d CollisionFrom { get; set; }

    /// <summary>How far it moves each tick, in world units.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Ticks left before this particle is offered to the collision batch again.</summary>
    public short CollisionCountdown { get; set; }

    /// <summary>Ticks left before the fade begins.</summary>
    public short HoldTicks { get; set; }

    /// <summary>The sprite frame being drawn.</summary>
    public short Frame { get; set; }

    /// <summary>Ticks spent on the current frame.</summary>
    public short FrameTimer { get; set; }

    /// <summary>How far the quad turns each tick, in radians.</summary>
    public float Spin { get; set; }

    /// <summary>The quad's alpha, which the fade drives to zero.</summary>
    public float Alpha { get; set; }

    /// <summary>How much alpha one tick takes off, once the hold has run out.</summary>
    public float FadePerTick { get; set; }

    /// <summary>The quad's accumulated roll, in radians.</summary>
    public float Angle { get; set; }

    /// <summary>The size multiplier, which the system's growth rate raises each tick.</summary>
    public float Growth { get; set; }
}

/// <summary>What one particle's collision segment met.</summary>
/// <param name="From">Where the segment started, in world units.</param>
/// <param name="To">Where it ended — the particle's position this tick.</param>
/// <param name="Hit">The surface it met.</param>
public readonly record struct RetailParticleContact(Vector3d From, Vector3d To, WorldRayHit Hit);

/// <summary>
/// One <c>Particle2DDynCubeObjTask</c> — the original's whole particle engine, in the shape
/// <c>0x4FDF00</c> builds it and <c>0x4FD660</c> steps it.
/// </summary>
/// <remarks>
/// <para>
/// Every effect in the game that is not a model is one of these: the puff a round kicks off a wall
/// (<c>concrete1.spr</c>), the mist a body throws (<c>blood1.spr</c>) and the droplets that fly off it
/// (<c>blood2.spr</c>). A system owns a <b>fixed</b> particle pool — two for the wall puff, two for the
/// blood mist, ten for the droplets — so a burst into one wall or one guard cannot stack indefinitely.
/// Emitting past the pool is simply dropped, exactly as <c>0x4FE120</c>'s
/// <c>if (live &lt; capacity)</c> drops it.
/// </para>
/// <para>
/// The step is four lines and no more: velocity gains the system's acceleration, position gains
/// velocity, the roll gains the spin and the size multiplier gains the growth rate. Then either the
/// hold ticks down, or the alpha does; a particle dies when its alpha or its size multiplier reaches
/// zero. There is no drag, no turbulence and no per-particle gravity — the acceleration is a property
/// of the <i>system</i>.
/// </para>
/// <para>
/// Collision is optional and <b>staggered</b>. A system given a contact handler tests each particle
/// once every sixteen ticks, and which tick is decided by a process-wide counter taken modulo sixteen
/// when the particle is emitted (<c>0x4FE120</c>'s <c>dword_A7A4DC</c>). The segment traced is from
/// where the particle was at its <i>last</i> test to where it is now, so nothing is missed by the
/// sparse sampling; a particle that meets something is handed to the callback and then dies.
/// </para>
/// </remarks>
public sealed class RetailParticleSystem
{
    /// <summary>How many ticks separate one particle's collision tests.</summary>
    /// <remarks>
    /// <c>0x4FDA74</c> reloads the countdown with fifteen after a test, which with the tick that fired
    /// it makes a period of sixteen — the same sixteen the emit's <c>% 16</c> stagger spreads over.
    /// </remarks>
    public const int CollisionPeriodTicks = 16;

    /// <summary>The reload the original writes back after a test.</summary>
    public const short CollisionReload = 15;

    private static int _staggerCounter;

    private readonly RetailParticle[] _particles;
    private readonly bool _collides;
    private int _count;

    /// <summary>Initializes a new instance of the <see cref="RetailParticleSystem"/> class.</summary>
    /// <param name="acceleration">What every particle's velocity gains each tick, in units per tick².</param>
    /// <param name="sizeInUnits">The quad's authored width and height, in world units.</param>
    /// <param name="growthPerTick">What the size multiplier gains each tick; zero pins the size.</param>
    /// <param name="frameCount">How many frames the sprite has.</param>
    /// <param name="ticksPerFrame">How many ticks one frame is held; zero advances every tick.</param>
    /// <param name="capacity">The pool size — the original's <c>maxParticles</c>.</param>
    /// <param name="collides">Whether particles are traced against the world.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public RetailParticleSystem(
        Vector3f acceleration,
        float sizeInUnits,
        float growthPerTick,
        int frameCount,
        int ticksPerFrame,
        int capacity,
        bool collides = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Acceleration = acceleration;
        SizeInUnits = sizeInUnits;
        GrowthPerTick = growthPerTick;
        FrameCount = Math.Max(1, frameCount);
        TicksPerFrame = Math.Max(0, ticksPerFrame);
        _collides = collides;
        _particles = new RetailParticle[capacity];
    }

    /// <summary>Gets what every particle's velocity gains each tick.</summary>
    public Vector3f Acceleration { get; }

    /// <summary>Gets the quad's authored size in world units, before <see cref="RetailParticle.Growth"/>.</summary>
    public float SizeInUnits { get; }

    /// <summary>Gets what the size multiplier gains each tick.</summary>
    public float GrowthPerTick { get; }

    /// <summary>Gets how many frames the sprite has.</summary>
    public int FrameCount { get; }

    /// <summary>Gets how many ticks one frame is held.</summary>
    public int TicksPerFrame { get; }

    /// <summary>Gets the pool size.</summary>
    public int Capacity => _particles.Length;

    /// <summary>Gets how many particles are alive.</summary>
    public int Count => _count;

    /// <summary>Gets the live particles.</summary>
    public ReadOnlySpan<RetailParticle> Particles => _particles.AsSpan(0, _count);

    /// <summary>Resets the process-wide collision stagger, so a test starts from a known phase.</summary>
    public static void ResetStagger() => _staggerCounter = 0;

    /// <summary>Drops every live particle.</summary>
    public void Clear() => _count = 0;

    /// <summary>Emits one particle, as <c>0x4FE120</c> does.</summary>
    /// <param name="position">Where it starts, in world units.</param>
    /// <param name="velocity">How far it moves each tick, in world units.</param>
    /// <param name="alpha">Its starting alpha.</param>
    /// <param name="spin">How far it turns each tick, in radians.</param>
    /// <param name="angle">Its starting roll, in radians.</param>
    /// <param name="growth">Its starting size multiplier.</param>
    /// <param name="holdTicks">Ticks before the fade begins.</param>
    /// <param name="fadeTicks">Ticks the fade takes; zero fades in one tick.</param>
    /// <returns><see langword="false"/> when the pool was full and the particle was dropped.</returns>
    /// <remarks>
    /// <b>A full pool drops the particle silently.</b> There is no oldest-first eviction: the original
    /// only writes when <c>live &lt; capacity</c>, so a body already showing ten droplets shows no more
    /// until some die.
    /// </remarks>
    public bool Emit(
        Vector3d position,
        Vector3f velocity,
        float alpha,
        float spin,
        float angle,
        float growth,
        int holdTicks,
        int fadeTicks)
    {
        if (_count >= _particles.Length)
        {
            return false;
        }

        ref RetailParticle p = ref _particles[_count];
        p.Position = position;
        p.CollisionFrom = position;
        p.Velocity = velocity;

        // 0x4FE17A: the stagger is a process-wide counter, not a per-system one, so two systems
        // emitting on the same tick get different collision phases.
        p.CollisionCountdown = (short)(_staggerCounter % CollisionPeriodTicks);
        _staggerCounter = (_staggerCounter + 1) % CollisionPeriodTicks;

        p.HoldTicks = (short)holdTicks;
        p.Frame = 0;
        p.FrameTimer = 0;
        p.Spin = spin;
        p.Alpha = alpha;
        p.FadePerTick = fadeTicks != 0 ? 1f / fadeTicks : 1f;
        p.Angle = angle;
        p.Growth = growth;
        _count++;
        return true;
    }

    /// <summary>Emits one particle into a cone about a direction, as <c>0x4FE210</c> does.</summary>
    /// <param name="position">Where it starts, in world units.</param>
    /// <param name="direction">The cone's axis; need not be unit length.</param>
    /// <param name="coneRadians">The cone's full width — the pitch is drawn on ±half of it.</param>
    /// <param name="speedMinimum">The slowest launch speed, in units per tick.</param>
    /// <param name="speedMaximum">The fastest.</param>
    /// <param name="alpha">The starting alpha.</param>
    /// <param name="spin">Radians of roll per tick.</param>
    /// <param name="angle">The starting roll.</param>
    /// <param name="growth">The starting size multiplier.</param>
    /// <param name="holdTicks">Ticks before the fade begins.</param>
    /// <param name="fadeTicks">Ticks the fade takes.</param>
    /// <param name="random">The draw source.</param>
    /// <returns><see langword="false"/> when the pool was full.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x4FE210</c> builds an orthonormal frame from the axis — side is
    /// <c>normalize(cross(helper, axis))</c> with the helper <c>(1,0,0)</c>, or <c>(0,1,0)</c> when the
    /// axis leans more than <c>0.75</c> along X, and up is <c>cross(side, axis)</c>. It draws a pitch
    /// on <b>±half</b> the cone and a roll on <c>(−π, π]</c>, composes them through
    /// <c>0x4B38E0</c> as <c>Rz(roll)·Rx(pitch)</c>, and launches along that matrix's <b>third
    /// column</b>:
    /// </para>
    /// <para>
    /// <c>sin r · sin p · up − cos r · sin p · side + cos p · axis</c>
    /// </para>
    /// <para>
    /// The draws happen in that order — pitch first, then roll — and both are taken before the speed,
    /// so keeping the order keeps every later value in the tick on the same number.
    /// </para>
    /// </remarks>
    public bool EmitCone(
        Vector3d position,
        Vector3f direction,
        float coneRadians,
        float speedMinimum,
        float speedMaximum,
        float alpha,
        float spin,
        float angle,
        float growth,
        int holdTicks,
        int fadeTicks,
        IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Vector3f axis = SafeNormalize(direction);

        // 0x4FE2CB: the helper flips so the cross product can never collapse.
        float helperX = 1f;
        float helperY = 0f;
        if (MathF.Abs(axis.X) > 0.75f)
        {
            helperX = 0f;
            helperY = 1f;
        }

        Vector3f side = SafeNormalize(new Vector3f(
            helperY * axis.Z,
            -(helperX * axis.Z),
            (helperX * axis.Y) - (helperY * axis.X)));
        Vector3f up = new(
            (side.Y * axis.Z) - (side.Z * axis.Y),
            (side.Z * axis.X) - (side.X * axis.Z),
            (side.X * axis.Y) - (side.Y * axis.X));

        // 0x4FE3F4 then 0x4FE40E, in the order the original draws them.
        float pitchDraw = random.NextSingle() * coneRadians;
        float roll = MathF.PI - (random.NextSingle() * (MathF.PI * 2f));
        float pitch = (coneRadians * 0.5f) - pitchDraw;

        float pitchSin = MathF.Sin(pitch);
        float pitchCos = MathF.Cos(pitch);
        float upWeight = MathF.Sin(roll) * pitchSin;
        float sideWeight = -MathF.Cos(roll) * pitchSin;
        Vector3f launch = SafeNormalize(new Vector3f(
            (upWeight * up.X) + (sideWeight * side.X) + (pitchCos * axis.X),
            (upWeight * up.Y) + (sideWeight * side.Y) + (pitchCos * axis.Y),
            (upWeight * up.Z) + (sideWeight * side.Z) + (pitchCos * axis.Z)));

        float speed = (random.NextSingle() * (speedMaximum - speedMinimum)) + speedMinimum;
        return Emit(
            position,
            launch * speed,
            alpha,
            spin,
            angle,
            growth,
            holdTicks,
            fadeTicks);
    }

    /// <summary>Steps every particle one tick, as <c>0x4FD660</c> does.</summary>
    /// <param name="walls">The world cast used for collision; ignored by a system that does not collide.</param>
    /// <param name="onContact">What to do where a particle met a surface.</param>
    /// <remarks>
    /// <para>
    /// The order is the original's: integrate and age every particle first, dropping the dead, and only
    /// then run the collision batch over whatever survived.
    /// </para>
    /// <para>
    /// <b>A particle that dies is replaced by the last one, and the loop does not advance past it</b>
    /// (<c>0x4FDBF8</c> jumps over the increment, and <c>0x4FDD30</c> swaps with the tail). The
    /// swapped-in particle is therefore stepped twice on the tick another one died. That is kept.
    /// </para>
    /// <para>
    /// <b>One divergence.</b> The original gathers every due particle, traces the whole batch in one
    /// call, and then kills the ones that hit <i>by the index recorded before any of the kills</i> — so
    /// a batch with two contacts can kill a bystander instead of the second hit. Particles are traced
    /// one at a time here and the right one always dies. Nothing is observably different: every
    /// particle in a colliding system is one droplet of the same spray.
    /// </para>
    /// </remarks>
    public void Tick(WorldRayCast? walls = null, Action<RetailParticleContact>? onContact = null)
    {
        for (int i = 0; i < _count;)
        {
            ref RetailParticle p = ref _particles[i];

            p.Velocity = new Vector3f(
                p.Velocity.X + Acceleration.X,
                p.Velocity.Y + Acceleration.Y,
                p.Velocity.Z + Acceleration.Z);
            p.Position = new Vector3d(
                p.Position.X + p.Velocity.X,
                p.Position.Y + p.Velocity.Y,
                p.Position.Z + p.Velocity.Z);
            p.Angle += p.Spin;
            p.Growth += GrowthPerTick;

            // 0x4FD756: a single-frame sprite never advances, because the loop is guarded on
            // frameCount - 1 rather than on frameCount.
            if (FrameCount > 1)
            {
                p.FrameTimer++;
                if (p.FrameTimer >= TicksPerFrame)
                {
                    p.FrameTimer = 0;
                    if (p.Frame < FrameCount - 1)
                    {
                        p.Frame++;
                    }
                }
            }

            if (p.HoldTicks != 0)
            {
                p.HoldTicks--;
                i++;
                continue;
            }

            p.Alpha -= p.FadePerTick;
            if (p.Alpha <= 0f || p.Growth <= 0f)
            {
                RemoveAt(i);
                continue;
            }

            i++;
        }

        if (!_collides || walls is null || _count == 0)
        {
            return;
        }

        for (int i = 0; i < _count;)
        {
            ref RetailParticle p = ref _particles[i];
            if (p.CollisionCountdown != 0)
            {
                p.CollisionCountdown--;
                i++;
                continue;
            }

            Vector3d from = p.CollisionFrom;
            Vector3d to = p.Position;
            p.CollisionCountdown = CollisionReload;
            p.CollisionFrom = to;

            if (walls(from, to, out WorldRayHit hit))
            {
                onContact?.Invoke(new RetailParticleContact(from, to, hit));
                RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    private static Vector3f SafeNormalize(Vector3f value)
    {
        float lengthSquared = value.LengthSquared;
        if (!(lengthSquared > 0f))
        {
            return value;
        }

        float inverse = 1f / MathF.Sqrt(lengthSquared);
        return new Vector3f(value.X * inverse, value.Y * inverse, value.Z * inverse);
    }

    // The original keeps a free list and swaps the dying slot's pointer to the end; the visible effect
    // is that the survivors close up, which is what this does.
    private void RemoveAt(int index)
    {
        _count--;
        if (index != _count)
        {
            _particles[index] = _particles[_count];
        }
    }
}

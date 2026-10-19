using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Where a particle met a surface, handed to its system's collision callback, <c>Particle2DCollisionContext_t</c>.</summary>
/// <param name="Intersection">What the particle's line struck.</param>
/// <param name="Start">Where the line started: the particle at its last collision check.</param>
/// <param name="End">Where it ended: the particle now.</param>
public readonly record struct Igi2ParticleCollision(Igi2Intersection Intersection, Vector3d Start, Vector3d End);

/// <summary>
/// The 2-D particle module, <c>Particle2DDynCubeObj</c>'s shared state: its own random stream, which aims the cones particles are
/// thrown in, and the phase new particles take in the collision checks, which every system shares.
/// </summary>
/// <remarks>
/// The stream is seeded once, as <c>Particle2DDynCubeObj_Open</c> seeds <c>Particle2DDynCubeObj_tRandomState</c>; it is not the
/// level's run stream. A system's collision callback, though, may draw from the run stream.
/// </remarks>
public sealed class Igi2Particles
{
    /// <summary>The ticks between a particle's collision checks, <c>15</c>.</summary>
    public const int CollisionInterval = 15;

    /// <summary>The groups particles are traced against: every type's solids (<c>0x20000000</c>).</summary>
    public const uint CollisionGroups = Igi2CollisionWorld.EveryTypeGroup;

    private int _collisionPhase;

    /// <summary>Initializes a new instance of the <see cref="Igi2Particles"/> class.</summary>
    /// <param name="collision">The level's collision, which particles are traced against.</param>
    /// <param name="dynamic">The level's dynamic tasks, under which each system runs.</param>
    public Igi2Particles(IIgi2CollisionQuery collision, Igi2DynamicTasks dynamic)
    {
        Collision = collision ?? throw new ArgumentNullException(nameof(collision));
        Dynamic = dynamic ?? throw new ArgumentNullException(nameof(dynamic));
    }

    /// <summary>Gets the module's own random stream, seeded as <c>Particle2DDynCubeObj_Open</c> seeds it.</summary>
    public IgiRandom Random { get; } = new(unchecked((uint)-624411820), unchecked((uint)-896272554), 430872269u);

    /// <summary>Gets the level's collision.</summary>
    public IIgi2CollisionQuery Collision { get; }

    /// <summary>Gets the level's dynamic tasks.</summary>
    public Igi2DynamicTasks Dynamic { get; }

    /// <summary>Makes a particle system under the level's dynamic task, <c>Particle2DDynCubeObj_New</c>.</summary>
    /// <param name="sprite">The sprite its particles draw with.</param>
    /// <param name="animated">Whether the sprite has frames to step through (its tilemap).</param>
    /// <param name="frames">How many frames the sprite has.</param>
    /// <param name="acceleration">What is added to every particle's speed each tick.</param>
    /// <param name="color">The particles' colour.</param>
    /// <param name="width">The particles' drawn width.</param>
    /// <param name="height">The particles' drawn height.</param>
    /// <param name="range">How far off the particles are drawn.</param>
    /// <param name="transparency">The drawing's transparency bits.</param>
    /// <param name="animationDelta">The ticks each sprite frame shows.</param>
    /// <param name="sizeDelta">What each particle's size grows by each tick.</param>
    /// <param name="shininess">How shiny the particles are drawn.</param>
    /// <param name="maxParticles">How many particles the system holds at most.</param>
    /// <param name="onCollision">What a particle that meets a surface does, or <see langword="null"/> for particles that pass through.</param>
    /// <returns>The system.</returns>
    public Igi2Particle2DSystem New(
        string sprite,
        bool animated,
        int frames,
        Vector3f acceleration,
        Vector3f color,
        float width,
        float height,
        float range,
        int transparency,
        int animationDelta,
        float sizeDelta,
        float shininess,
        int maxParticles,
        Action<Igi2ParticleCollision>? onCollision)
    {
        Igi2Particle2DSystem system = new(this, maxParticles)
        {
            Sprite = sprite,
            IsAnimated = animated,
            Frames = frames,
            Acceleration = acceleration,
            Color = color,
            Width = width,
            Height = height,
            Range = range,
            Transparency = transparency | 0x40000,
            AnimationDelta = animationDelta,
            SizeDelta = sizeDelta,
            Shininess = shininess,
            OnCollision = onCollision,
        };
        Dynamic.Add(system);
        return system;
    }

    /// <summary>Takes the collision phase the next particle starts in, the shared counter <c>Particle2DDynCubeObj_AddParticle</c> steps.</summary>
    /// <returns>The phase, 0 to 15.</returns>
    internal int TakeCollisionPhase()
    {
        int phase = _collisionPhase;
        _collisionPhase = (short)(phase + 1) % 16;
        return phase;
    }
}

/// <summary>One particle of a 2-D particle system, a slot of <c>Particle2DDynCubeObj</c>'s particle table (88 bytes).</summary>
public sealed class Igi2Particle2D
{
    /// <summary>Gets or sets where the particle is.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the particle's speed, per tick.</summary>
    public Vector3f Speed { get; set; }

    /// <summary>Gets or sets the ticks until the particle is next checked for collision (offset 36).</summary>
    public short CollisionCountdown { get; set; }

    /// <summary>Gets or sets the ticks the particle lives before it starts fading (offset 38).</summary>
    public short LifeTime { get; set; }

    /// <summary>Gets or sets the sprite frame the particle shows (offset 40).</summary>
    public short Frame { get; set; }

    /// <summary>Gets or sets the ticks the frame has shown (offset 42).</summary>
    public short FrameTicks { get; set; }

    /// <summary>Gets or sets what the rotation turns by each tick (offset 44).</summary>
    public float RotationDelta { get; set; }

    /// <summary>Gets or sets how bright the particle is (offset 48); it fades to nothing once its life is spent.</summary>
    public float Intensity { get; set; }

    /// <summary>Gets or sets what the intensity fades by each tick (offset 52).</summary>
    public float Fade { get; set; }

    /// <summary>Gets or sets the particle's rotation (offset 56).</summary>
    public float Rotation { get; set; }

    /// <summary>Gets or sets the particle's size (offset 60).</summary>
    public float Size { get; set; }

    /// <summary>Gets or sets where the particle was at its last collision check (offset 64).</summary>
    public Vector3d LastCheckedPosition { get; set; }
}

/// <summary>
/// A system of 2-D particles, <c>Particle2DDynCubeObj</c>: blood, sparks and dust that fly, fade and, when it has a callback,
/// meet the level's surfaces.
/// </summary>
/// <remarks>
/// <para>
/// The particles sit in slots; one that dies swaps places with the last live one, so the order changes as they die, as the
/// original's pointer table does. A system that is empty when it runs deletes itself (it is self-destructing from creation).
/// </para>
/// <para>
/// Each live particle is checked for collision every fifteen ticks, in a phase given when it is added, by a line from where it
/// was last checked to where it is. Every line struck is handed to the callback, and its particle dies. The particle that dies
/// is the one the line was made for <i>by its index when the lines were made</i>: an earlier death this pass may have swapped
/// another particle into that slot, and then that one dies instead, as in the original.
/// </para>
/// </remarks>
public sealed class Igi2Particle2DSystem : IIgi2DynamicTask
{
    private readonly Igi2Particles _module;
    private readonly Igi2Particle2D[] _slots;
    private readonly Igi2LineTrace _trace;
    private readonly int[] _lineParticle;

    internal Igi2Particle2DSystem(Igi2Particles module, int maxParticles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParticles);
        _module = module;
        _slots = new Igi2Particle2D[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            _slots[i] = new Igi2Particle2D();
        }

        _lineParticle = new int[maxParticles];
        _trace = new Igi2LineTrace(maxParticles) { Includes = static owner => owner is not Humans.Igi2Human };
    }

    /// <summary>Gets the live particles, in slot order.</summary>
    public IEnumerable<Igi2Particle2D> Particles => _slots.Take(Count);

    /// <summary>Gets how many particles are alive.</summary>
    public int Count { get; private set; }

    /// <summary>Gets how many particles the system holds at most.</summary>
    public int MaxParticles => _slots.Length;

    /// <summary>Gets a value indicating whether the system has deleted itself.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Gets or sets a value indicating whether the system deletes itself once empty (from creation, yes).</summary>
    public bool IsSelfDestruct { get; set; } = true;

    /// <summary>Gets or sets the sprite the particles draw with.</summary>
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the sprite has frames the particles step through.</summary>
    public bool IsAnimated { get; set; }

    /// <summary>Gets or sets how many frames the sprite has.</summary>
    public int Frames { get; set; }

    /// <summary>Gets or sets what is added to each particle's speed each tick.</summary>
    public Vector3f Acceleration { get; set; }

    /// <summary>Gets or sets the particles' colour.</summary>
    public Vector3f Color { get; set; }

    /// <summary>Gets or sets the particles' drawn width.</summary>
    public float Width { get; set; }

    /// <summary>Gets or sets the particles' drawn height.</summary>
    public float Height { get; set; }

    /// <summary>Gets or sets how far off the particles are drawn.</summary>
    public float Range { get; set; }

    /// <summary>Gets or sets the drawing's transparency bits.</summary>
    public int Transparency { get; set; }

    /// <summary>Gets or sets the ticks each sprite frame shows.</summary>
    public int AnimationDelta { get; set; }

    /// <summary>Gets or sets what each particle's size grows by each tick.</summary>
    public float SizeDelta { get; set; }

    /// <summary>Gets or sets how shiny the particles are drawn.</summary>
    public float Shininess { get; set; }

    /// <summary>Gets or sets what a particle that meets a surface does, or <see langword="null"/> when particles pass through.</summary>
    public Action<Igi2ParticleCollision>? OnCollision { get; set; }

    /// <summary>Adds a particle, unless the system is full, <c>Particle2DDynCubeObj_AddParticle</c>.</summary>
    /// <param name="position">Where it starts.</param>
    /// <param name="speed">Its speed, per tick.</param>
    /// <param name="intensity">How bright it starts.</param>
    /// <param name="rotationDelta">What its rotation turns by each tick.</param>
    /// <param name="rotation">Its rotation.</param>
    /// <param name="size">Its size.</param>
    /// <param name="lifeTime">The ticks it lives before it fades.</param>
    /// <param name="fadeTime">The ticks it fades over; none fades it at once.</param>
    public void AddParticle(Vector3d position, Vector3f speed, float intensity, float rotationDelta, float rotation, float size, int lifeTime, int fadeTime)
    {
        if (Count >= _slots.Length)
        {
            return;
        }

        Igi2Particle2D particle = _slots[Count];
        particle.Position = position;
        particle.LastCheckedPosition = position;
        particle.Speed = speed;
        particle.CollisionCountdown = (short)_module.TakeCollisionPhase();
        particle.LifeTime = (short)lifeTime;
        particle.Intensity = intensity;
        particle.Rotation = rotation;
        particle.Size = size;
        particle.RotationDelta = rotationDelta;
        particle.Frame = 0;
        particle.FrameTicks = 0;
        particle.Fade = fadeTime != 0 ? ((Fpu)1.0 / fadeTime).F : 1f;
        Count++;
    }

    /// <summary>
    /// Adds a particle thrown somewhere within a cone, at a speed between two, <c>Particle2DDynCubeObj_AddConeParticle</c>: three
    /// draws of the module's own stream pick the angle off the axis, the turn around it and the speed.
    /// </summary>
    /// <param name="position">Where it starts.</param>
    /// <param name="direction">The cone's axis.</param>
    /// <param name="maxAngle">The cone's full opening angle, in radians.</param>
    /// <param name="minSpeed">The slowest it may go.</param>
    /// <param name="maxSpeed">The fastest it may go.</param>
    /// <param name="intensity">How bright it starts.</param>
    /// <param name="rotationDelta">What its rotation turns by each tick.</param>
    /// <param name="rotation">Its rotation.</param>
    /// <param name="size">Its size.</param>
    /// <param name="lifeTime">The ticks it lives before it fades.</param>
    /// <param name="fadeTime">The ticks it fades over.</param>
    public void AddConeParticle(
        Vector3d position, Vector3f direction, float maxAngle, float minSpeed, float maxSpeed,
        float intensity, float rotationDelta, float rotation, float size, int lifeTime, int fadeTime)
    {
        Vector3f z = Igi2RigidBody.Normalised(direction);

        // A side axis from whichever of X or Y the cone's axis is further from.
        Fpu ux = 1f, uy = 0f;
        if (Math.Abs(z.X) > 0.75f)
        {
            ux = 0f;
            uy = 1f;
        }

        Vector3f side = new(
            (((Fpu)z.Z * uy) - ((Fpu)z.Y * 0f)).F,
            (-((ux * z.Z) - ((Fpu)z.X * 0f))).F,
            ((ux * z.Y) - (uy * z.X)).F);
        Vector3f y = Igi2RigidBody.Normalised(side);
        Vector3f x = new(
            (((Fpu)y.Y * z.Z) - ((Fpu)y.Z * z.Y)).F,
            (-(((Fpu)y.X * z.Z) - ((Fpu)y.Z * z.X))).F,
            (((Fpu)y.X * z.Y) - ((Fpu)y.Y * z.X)).F);

        IgiRandom random = _module.Random;
        Fpu off = (Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale;
        float around = ((Fpu)3.1415927410125732f - (((Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale) * 6.2831854820251465f)).F;
        float tilt = (((Fpu)maxAngle * 0.5f) - (off * maxAngle)).F;
        float[] turn = Igi2Pose.Rotation(tilt, 0f, around);
        Fpu speedShare = (Fpu)(double)(ushort)random.NextRaw() * IgiRandom.Scale;

        // The rotation's third column carried into the cone's frame.
        float vx = ((((Fpu)turn[2] * x.X) + ((Fpu)turn[5] * y.X)) + ((Fpu)turn[8] * z.X)).F;
        float vy = ((((Fpu)turn[2] * x.Y) + ((Fpu)turn[5] * y.Y)) + ((Fpu)turn[8] * z.Y)).F;
        float vz = ((((Fpu)turn[2] * x.Z) + ((Fpu)turn[5] * y.Z)) + ((Fpu)turn[8] * z.Z)).F;
        Fpu speed = (speedShare * ((Fpu)maxSpeed - minSpeed)) + minSpeed;
        Fpu outZ = vz;
        if (vx != 0f || vy != 0f || vz != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)vx * vx) + ((Fpu)vy * vy)) + ((Fpu)vz * vz));
            vx = ((Fpu)vx * inverse).F;
            vy = ((Fpu)vy * inverse).F;
            outZ = inverse * vz;
        }

        AddParticle(position, new Vector3f(((Fpu)vx * speed).F, ((Fpu)vy * speed).F, (outZ * speed).F), intensity, rotationDelta, rotation, size, lifeTime, fadeTime);
    }

    /// <summary>Lets every particle go at once, <c>Particle2DDynCubeObj_RemoveParticles</c>.</summary>
    public void RemoveParticles() => Count = 0;

    /// <summary>Moves every live particle by an offset, as a smoke whose source moves carries its plume (<c>Particle2DDynCubeObj_MoveParticles</c>).</summary>
    /// <param name="move">The offset.</param>
    public void MoveParticles(Vector3d move)
    {
        for (int i = 0; i < Count; i++)
        {
            Vector3d p = _slots[i].Position;
            _slots[i].Position = new Vector3d(((Fpu)p.X + move.X).Value, ((Fpu)move.Y + p.Y).Value, ((Fpu)p.Z + move.Z).Value);
        }
    }

    /// <summary>A tick of the system, <c>Particle2DDynCubeObj_RunHandler</c>.</summary>
    /// <returns><see langword="false"/> once the system has deleted itself.</returns>
    public bool Run()
    {
        if (Count == 0)
        {
            if (IsSelfDestruct)
            {
                IsDeleted = true;
                return false;
            }

            return true;
        }

        int lastFrame = IsAnimated ? Frames - 1 : 0;
        int i = 0;
        while (i < Count)
        {
            Igi2Particle2D p = _slots[i];
            Vector3f a = Acceleration, v = p.Speed;
            v = new Vector3f(((Fpu)v.X + a.X).F, ((Fpu)a.Y + v.Y).F, ((Fpu)a.Z + v.Z).F);
            p.Speed = v;
            p.Position = new Vector3d(((Fpu)v.X + p.Position.X).Value, ((Fpu)v.Y + p.Position.Y).Value, ((Fpu)v.Z + p.Position.Z).Value);
            p.Rotation = ((Fpu)p.RotationDelta + p.Rotation).F;
            p.Size = ((Fpu)SizeDelta + p.Size).F;
            if (lastFrame != 0)
            {
                p.FrameTicks++;
                if (p.FrameTicks >= AnimationDelta)
                {
                    p.FrameTicks = 0;
                    if (p.Frame < lastFrame)
                    {
                        p.Frame++;
                    }
                }
            }

            if (p.LifeTime != 0)
            {
                p.LifeTime--;
            }
            else
            {
                p.Intensity = ((Fpu)p.Intensity - p.Fade).F;
                if (p.Intensity <= 0f || p.Size <= 0f)
                {
                    RemoveAt(i);
                    continue;
                }
            }

            i++;
        }

        if (OnCollision is { } onCollision)
        {
            Collide(onCollision);
        }

        return true;
    }

    private void Collide(Action<Igi2ParticleCollision> onCollision)
    {
        int lines = 0;
        for (int i = 0; i < Count; i++)
        {
            Igi2Particle2D p = _slots[i];
            if (p.CollisionCountdown != 0)
            {
                p.CollisionCountdown--;
            }

            if (p.CollisionCountdown == 0)
            {
                _lineParticle[lines] = i;
                _trace.Starts[lines] = p.LastCheckedPosition;
                _trace.Ends[lines] = p.Position;
                Igi2Intersection reset = _trace.Intersections[lines];
                reset.IsHit = false;
                reset.HitObject = null;
                lines++;
                p.LastCheckedPosition = p.Position;
                p.CollisionCountdown = Igi2Particles.CollisionInterval;
            }
        }

        if (lines == 0)
        {
            return;
        }

        _trace.Count = lines;
        _module.Collision.TraceLines(_trace, Igi2Particles.CollisionGroups);
        for (int k = 0; k < lines; k++)
        {
            Igi2Intersection hit = _trace.Intersections[k];
            if (!hit.IsHit)
            {
                continue;
            }

            onCollision(new Igi2ParticleCollision(hit, _trace.Starts[k], _trace.Ends[k]));
            if (Count != 0)
            {
                RemoveAt(_lineParticle[k]);
            }
        }
    }

    /// <summary>A particle dies: the last live one takes its slot.</summary>
    private void RemoveAt(int index)
    {
        int last = Count - 1;
        if (index != last)
        {
            (_slots[index], _slots[last]) = (_slots[last], _slots[index]);
        }

        Count--;
    }
}

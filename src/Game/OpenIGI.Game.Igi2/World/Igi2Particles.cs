using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World.Particles;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Where a particle met a surface, handed to its system's collision callback, <c>Particle2DCollisionContext_t</c>.</summary>
/// <param name="Intersection">What the particle's line struck.</param>
/// <param name="Start">Where the line started: the particle at its last collision check.</param>
/// <param name="End">Where it ended: the particle now.</param>
public readonly record struct Igi2ParticleCollision(WorldIntersection Intersection, Vector3d Start, Vector3d End);

/// <summary>
/// The 2-D particle engine in an I.G.I. 2 level: the engine both titles share (<see cref="Particle2DModule"/>), its systems run
/// under the level's dynamic task in the order they were made and their particles traced against the level's collision.
/// </summary>
public sealed class Igi2Particles
{
    /// <summary>The groups particles are traced against: every type's solids (<c>0x20000000</c>).</summary>
    public const uint CollisionGroups = Igi2CollisionWorld.EveryTypeGroup;

    /// <summary>Initializes a new instance of the <see cref="Igi2Particles"/> class.</summary>
    /// <param name="collision">The level's collision, which particles are traced against.</param>
    /// <param name="dynamic">The level's dynamic tasks, under which each system runs.</param>
    /// <param name="spriteFrames">How many frames a sprite has, by name; one each when omitted.</param>
    public Igi2Particles(IIgi2CollisionQuery collision, Igi2DynamicTasks dynamic, Func<string, int>? spriteFrames = null)
    {
        Collision = collision ?? throw new ArgumentNullException(nameof(collision));
        Dynamic = dynamic ?? throw new ArgumentNullException(nameof(dynamic));
        Module.SpriteFrames = spriteFrames;
        Module.Made = system => Dynamic.Add(new Running(system));
    }

    /// <summary>Gets the engine: its stream, its collision phase, its live systems.</summary>
    public Particle2DModule Module { get; } = new();

    /// <summary>Gets the engine's own random stream, seeded as <c>Particle2DDynCubeObj_Open</c> seeds it.</summary>
    public IgiRandom Random => Module.Random;

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
    /// <param name="transparency">The drawing's transparency bits; depth-tested is added, as the original adds it.</param>
    /// <param name="animationDelta">The ticks each sprite frame shows.</param>
    /// <param name="sizeDelta">What each particle's size grows by each tick.</param>
    /// <param name="shininess">How shiny the particles are drawn.</param>
    /// <param name="maxParticles">How many particles the system holds at most.</param>
    /// <param name="onCollision">What a particle that meets a surface does, or <see langword="null"/> for particles that pass through.</param>
    /// <returns>The system.</returns>
    public Particle2DSystem New(
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
        Particle2DLook look = new(sprite, animated, frames, animationDelta, color, width, height, range, transparency | Particle2DFlags.DepthTested, shininess);
        return Module.New(look, acceleration, sizeDelta, maxParticles, onCollision is null ? null : Collider(maxParticles, onCollision));
    }

    /// <summary>What a system's particles meet: the level's solids, never a human, each strike handed to a callback.</summary>
    /// <param name="capacity">How many particles the system holds at most.</param>
    /// <param name="onCollision">What a particle that meets a surface does.</param>
    /// <returns>The collider.</returns>
    public IParticleCollider Collider(int capacity, Action<Igi2ParticleCollision> onCollision)
    {
        ArgumentNullException.ThrowIfNull(onCollision);
        return new LineCollider(Collision, capacity, onCollision);
    }

    // A system as one of the level's dynamic tasks.
    private sealed class Running(Particle2DSystem system) : IIgi2DynamicTask
    {
        public bool Run() => system.Run();
    }

    // The tick's lines traced together against the level's solids (never a human), each strike handed to the callback in turn.
    private sealed class LineCollider(IIgi2CollisionQuery collision, int capacity, Action<Igi2ParticleCollision> onCollision) : IParticleCollider
    {
        private readonly WorldLineTrace _trace = new(capacity) { Includes = static owner => owner is not Humans.Igi2Human };

        public void Collide(ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<bool> struck)
        {
            for (int k = 0; k < starts.Length; k++)
            {
                _trace.Starts[k] = starts[k];
                _trace.Ends[k] = ends[k];
                WorldIntersection reset = _trace.Intersections[k];
                reset.IsHit = false;
                reset.HitObject = null;
            }

            _trace.Count = starts.Length;
            collision.TraceLines(_trace, CollisionGroups);
            for (int k = 0; k < starts.Length; k++)
            {
                WorldIntersection hit = _trace.Intersections[k];
                if (hit.IsHit)
                {
                    onCollision(new Igi2ParticleCollision(hit, starts[k], ends[k]));
                    struck[k] = true;
                }
            }
        }
    }
}

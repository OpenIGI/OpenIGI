using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World.Particles;

/// <summary>
/// What a particle system's particles meet: each tick, the lines of the particles due a check are traced together, and each
/// strike is answered (a blood drop's stain, a puff's mark) before its particle dies.
/// </summary>
public interface IParticleCollider
{
    /// <summary>Traces the tick's lines and answers each strike, marking which struck.</summary>
    /// <param name="starts">Where each line starts: its particle at its last check.</param>
    /// <param name="ends">Where each ends: its particle now.</param>
    /// <param name="struck">Set for each line that met a surface.</param>
    void Collide(ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<bool> struck);
}

/// <summary>
/// The 2-D particle engine's shared state, <c>Particle2DDynCubeObj</c>'s module, the same in both titles: its own random stream,
/// which aims the cones particles are thrown in, the phase new particles take in the collision checks, and the systems alive.
/// </summary>
/// <remarks>
/// The stream is seeded once and is not the level's run stream. The two engines seed it differently: I.G.I. 2's
/// <c>Particle2DDynCubeObj_Open</c> stores three words (<see cref="NewStream"/>); Project I.G.I.'s registration (<c>0x4FCF50</c>)
/// resets its <c>dword_A7A4C0</c> and mixes in <c>0x12332D53</c>, which its runtime hands the module. The collision phase is one
/// counter for every system, as the original's global is.
/// </remarks>
public sealed class Particle2DModule
{
    /// <summary>The ticks between a particle's collision checks, <c>15</c>.</summary>
    public const int CollisionInterval = 15;

    private readonly List<Particle2DSystem> _systems = [];
    private int _collisionPhase;

    /// <summary>Initializes a new instance of the <see cref="Particle2DModule"/> class.</summary>
    /// <param name="random">The module's stream; <see cref="NewStream"/> when omitted.</param>
    public Particle2DModule(IgiRandom? random = null) => Random = random ?? NewStream();

    /// <summary>Gets the module's own random stream.</summary>
    public IgiRandom Random { get; }

    /// <summary>The module's stream as I.G.I. 2's <c>Particle2DDynCubeObj_Open</c> seeds it.</summary>
    /// <returns>The stream.</returns>
    public static IgiRandom NewStream() => new(unchecked((uint)-624411820), unchecked((uint)-896272554), 430872269u);

    /// <summary>Gets the systems alive, in the order they were made.</summary>
    public IReadOnlyList<Particle2DSystem> Systems => _systems;

    /// <summary>Gets or sets what is told of each system made: a title that runs its systems under its own task order adds it there.</summary>
    public Action<Particle2DSystem>? Made { get; set; }

    /// <summary>Gets or sets how many frames a sprite has, by its name, as a system reads its sprite's frames; one when unknown.</summary>
    public Func<string, int>? SpriteFrames { get; set; }

    /// <summary>How many frames a sprite has, as <see cref="SpriteFrames"/> says.</summary>
    /// <param name="sprite">The sprite's name.</param>
    /// <returns>The frames, one at least.</returns>
    public int FramesOf(string sprite) => Math.Max(1, SpriteFrames?.Invoke(sprite) ?? 1);

    /// <summary>Makes a particle system, <c>Particle2DDynCubeObj_New</c>.</summary>
    /// <param name="look">How its particles are drawn.</param>
    /// <param name="acceleration">What is added to every particle's speed each tick.</param>
    /// <param name="sizeDelta">What each particle's size grows by each tick.</param>
    /// <param name="maxParticles">How many particles the system holds at most.</param>
    /// <param name="collider">What its particles meet, or <see langword="null"/> for particles that pass through.</param>
    /// <returns>The system, alive until it empties (it is self-destructing from creation) or is let go.</returns>
    public Particle2DSystem New(Particle2DLook look, Vector3f acceleration, float sizeDelta, int maxParticles, IParticleCollider? collider)
    {
        ArgumentNullException.ThrowIfNull(look);
        Particle2DSystem system = new(this, maxParticles)
        {
            Look = look,
            Acceleration = acceleration,
            SizeDelta = sizeDelta,
            Collider = collider,
        };
        _systems.Add(system);
        Made?.Invoke(system);
        return system;
    }

    /// <summary>Lets a system go, as one that deleted itself is let go.</summary>
    /// <param name="system">The system.</param>
    public void Remove(Particle2DSystem system) => _systems.Remove(system);

    /// <summary>Runs every system a tick, in the order they were made, letting go of those that deleted themselves.</summary>
    /// <remarks>A title whose systems run under its own task order (I.G.I. 2's dynamic tasks) runs each itself instead.</remarks>
    public void RunAll()
    {
        for (int i = 0; i < _systems.Count;)
        {
            if (_systems[i].Run())
            {
                i++;
            }
        }
    }

    /// <summary>Lets every system go, as a level that ends does.</summary>
    public void Clear() => _systems.Clear();

    /// <summary>Takes the collision phase the next particle starts in, the shared counter <c>Particle2DDynCubeObj_AddParticle</c> steps.</summary>
    /// <returns>The phase, 0 to 15.</returns>
    internal int TakeCollisionPhase()
    {
        int phase = _collisionPhase;
        _collisionPhase = (short)(phase + 1) % 16;
        return phase;
    }
}

/// <summary>How a particle system's particles are drawn, the draw half of <c>Particle2DDynCubeObj_New</c>'s arguments.</summary>
/// <param name="Sprite">The sprite they draw with.</param>
/// <param name="IsAnimated">Whether the sprite has frames the particles step through.</param>
/// <param name="Frames">How many frames the sprite has.</param>
/// <param name="AnimationDelta">The ticks each frame shows.</param>
/// <param name="Color">The particles' colour, on a scale where 128 is white.</param>
/// <param name="Width">Half the drawn width, in world units.</param>
/// <param name="Height">Half the drawn height, in world units.</param>
/// <param name="Range">How far off they are drawn, in world units.</param>
/// <param name="Transparency">The drawing's graphic flags (<see cref="Particle2DFlags"/>).</param>
/// <param name="Shininess">How much of the world's light the colour takes.</param>
public sealed record Particle2DLook(
    string Sprite,
    bool IsAnimated,
    int Frames,
    int AnimationDelta,
    Vector3f Color,
    float Width,
    float Height,
    float Range,
    int Transparency,
    float Shininess = 0f);

/// <summary>The graphic flags a particle system draws with, as both titles' sprite renderers read them.</summary>
public static class Particle2DFlags
{
    /// <summary>Additive: the sprite's light by its alpha, plus what is behind (<c>8</c>).</summary>
    public const int Additive = 0x8;

    /// <summary>Darkening: what is behind by the sprite's inverse (<c>0x10</c>).</summary>
    public const int Darkening = 0x10;

    /// <summary>Depth-tested and not written, every particle system's (<c>0x40000</c>).</summary>
    public const int DepthTested = 0x40000;

    /// <summary>A particle fades through its alpha; without it, its colour darkens by its intensity at full opacity (<c>0x80000</c>).</summary>
    public const int FadesByOpacity = 0x80000;

    /// <summary>Filtered sampling, set on every particle's draw (<c>0x1000000</c>).</summary>
    public const int Filtered = 0x1000000;
}

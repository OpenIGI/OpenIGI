using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Simulates and draws the particle bursts where rounds land.</summary>
/// <remarks>
/// <para>
/// Presentation only, and optional (<c>r_impact_particles</c>; the vanilla preset pins it to zero).
/// Each burst is composed from what the material table says was struck: sparks are additive
/// velocity-stretched streaks that fall under real gravity and die fast; dust is alpha-blended soft
/// billboards that drift off the surface, expand and thin out. Particles step on the fixed 30 Hz
/// tick like everything else and are drawn interpolated between ticks, so they glide at any frame
/// rate without the simulation owing anything to it.
/// </para>
/// <para>
/// Every rule the tracer renderer learned the hard way is followed here: geometry streams through
/// one fixed-capacity mesh updated in place (never create-and-draw in the same frame), draws happen
/// in the FORWARD pass with the emissive override (light must not land in the G-buffer), the
/// procedural textures shape their alpha (a solid-alpha quad stamps rectangles into the scene's
/// alpha channel), every epsilon guard rejects NaN by polarity, and each emitted quad is
/// finite-checked — a NaN reaching the HDR target is the one thing an additive blend can darken
/// with.
/// </para>
/// </remarks>
public sealed class ImpactParticleRenderer : IDisposable
{
    /// <summary>The most live particles; a burst that would pass it is trimmed, oldest spared.</summary>
    public const int MaximumParticles = 1024;

    /// <summary>Inside this distance from the eye a particle is not drawn, in metres.</summary>
    public const float NearCullMetres = 0.6f;

    /// <summary>Over how many metres past the cull a particle fades up to full strength.</summary>
    public const float NearFadeMetres = 2.5f;

    /// <summary>Extra size per metre of distance from the eye, so a far burst stays visible.</summary>
    /// <remarks>
    /// The tracer's own cheat, and for the same reason: a four-centimetre spark at eighty metres is
    /// less than a pixel, and a burst that reads at arm's length but vanishes at range makes long
    /// shots feel like they land on nothing.
    /// </remarks>
    public const float SizeBoostPerMetre = 0.0022f;

    /// <summary>The texture edge, in texels, for both procedural sprites.</summary>
    public const int TextureSize = 64;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    /// <summary>Real gravity per tick², in world units — the same 9.8 m/s² the ragdoll falls at.</summary>
    private const float GravityPerTick = 44.600887f;

    // The muzzle flame's recipe: fully emissive so the forward shader treats the quads as light
    // (sparks) or self-shaded haze (dust) rather than as surfaces to be lit.
    internal static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly IgiRandom _random = new();
    private readonly ImpactParticle[] _particles = new ImpactParticle[MaximumParticles];
    private readonly ParticleQuadBuilder _quads = new();
    private readonly RetailExplosionEffects _explosions;
    private int _count;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _sparkTexture = TextureHandle.None;
    private TextureHandle _dustTexture = TextureHandle.None;
    private TextureHandle _retailFireballSpriteTexture = TextureHandle.None;
    private TextureHandle _retailSmokeSpriteTexture = TextureHandle.None;
    private readonly SpriteStripFrame[] _retailFireballFrames;
    private readonly SpriteStripFrame[] _retailSmokeFrames;
    private TextureHandle _glassTexture = TextureHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImpactParticleRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="fileSystem">The retail VFS, used to load fireball/smoke sprite frames when available.</param>
    /// <param name="spriteArchive">The archive in <paramref name="fileSystem"/> the fireball and smoke sprites ship in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ImpactParticleRenderer(IGraphicsDevice device, VirtualFileSystem? fileSystem = null, string? spriteArchive = null)
        : this(device, resources: null, fileSystem, spriteArchive)
    {
    }

    /// <summary>Initializes the renderer with the mission resource registry as well as the VFS.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">The bound retail resources, used for the authored explosion model texture.</param>
    /// <param name="fileSystem">The retail VFS, used to load fireball/smoke sprite frames.</param>
    /// <param name="spriteArchive">The archive in <paramref name="fileSystem"/> the fireball and smoke sprites ship in.</param>
    public ImpactParticleRenderer(
        IGraphicsDevice device,
        ResourceRegistry? resources,
        VirtualFileSystem? fileSystem = null,
        string? spriteArchive = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _explosions = new RetailExplosionEffects(
            device,
            resources,
            _random,
            (positionUnits, velocity, sizeMetres, lifeTicks) =>
                EmitRetailSprite(positionUnits, velocity, sizeMetres, lifeTicks, smoke: true));
        _retailFireballFrames = [];
        _retailSmokeFrames = [];
        if (fileSystem is not null && spriteArchive is not null)
        {
            SpriteStrip spark = SpriteStrip.Load(_device, fileSystem, spriteArchive, "fireball.spr");
            _retailFireballSpriteTexture = spark.Texture;
            _retailFireballFrames = spark.Frames;
            SpriteStrip dust = SpriteStrip.Load(_device, fileSystem, spriteArchive, "smoke2.spr");
            _retailSmokeSpriteTexture = dust.Texture;
            _retailSmokeFrames = dust.Frames;
        }
    }

    /// <summary>Gets how many particles are alive.</summary>
    public int LiveCount => _count;

    /// <summary>Gets whether the retail fireball and smoke sprites were loaded from the common archive.</summary>
    public bool UsesRetailEffectSprites => _retailFireballSpriteTexture.IsValid && _retailSmokeSpriteTexture.IsValid;

    /// <summary>Gets the decoded retail fireball frame count, for parity diagnostics and tests.</summary>
    internal int RetailFireballFrameCount => _retailFireballFrames.Length;

    /// <summary>Gets the decoded retail smoke frame count, for parity diagnostics and tests.</summary>
    internal int RetailSmokeFrameCount => _retailSmokeFrames.Length;

    /// <summary>Gets whether the IGI1 common pack supplied the native <c>1009_01_1</c> fragment model.</summary>
    public bool UsesRetailExplosionModel => _explosions.UsesRetailExplosionModel;

    /// <summary>Gets whether the common pack supplied the native 1006 FireBall model.</summary>
    internal bool UsesRetailFireballModel => _explosions.UsesRetailFireballModel;

    /// <summary>Gets how many animation frames the FireBall's texture has, for parity tests.</summary>
    internal int RetailFireballModelFrameCount => _explosions.RetailFireballModelFrameCount;

    /// <summary>Gets the blend the FireBall model's face group asks for.</summary>
    internal BlendMode RetailFireballBlend => _explosions.RetailFireballBlend;

    /// <summary>Gets the blend the fragment model's face group asks for.</summary>
    internal BlendMode RetailFragmentBlend => _explosions.RetailFragmentBlend;

    /// <summary>
    /// Binds the IGI1 authored fireball model and its common-pack material.
    /// </summary>
    /// <param name="mission">The loaded IGI1 mission whose common pack owns the effect.</param>
    /// <returns><see langword="true"/> when <c>1009_01_1</c> and its texture resolved.</returns>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _explosions.Load(mission);
    }

    /// <summary>Maps a struck material's name onto a burst kind.</summary>
    /// <param name="materialName">The game material's name, or empty when none resolved.</param>
    /// <returns>The kind; <see cref="ImpactSurfaceKind.Stone"/> when the name says nothing.</returns>
    /// <remarks>
    /// By name because that is what the retail table offers — its rows are <c>Concrete</c>,
    /// <c>Metal</c>, <c>Ground</c>, <c>Wood</c>, <c>Flesh</c> and kin, and nothing else in a row
    /// classifies it. Unknown names take stone, the commonest wall in the game.
    /// </remarks>
    public static ImpactSurfaceKind KindOf(string materialName)
    {
        if (materialName.Contains("metal", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("steel", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("iron", StringComparison.OrdinalIgnoreCase))
        {
            return ImpactSurfaceKind.Metal;
        }

        if (materialName.Contains("ground", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("dirt", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("sand", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("grass", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("mud", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("gravel", StringComparison.OrdinalIgnoreCase))
        {
            return ImpactSurfaceKind.Dirt;
        }

        if (materialName.Contains("wood", StringComparison.OrdinalIgnoreCase))
        {
            return ImpactSurfaceKind.Wood;
        }

        if (materialName.Contains("flesh", StringComparison.OrdinalIgnoreCase))
        {
            return ImpactSurfaceKind.Flesh;
        }

        return ImpactSurfaceKind.Stone;
    }

    /// <summary>Spawns one impact's burst.</summary>
    /// <param name="positionUnits">Where the round landed, in world units.</param>
    /// <param name="normal">The struck surface's normal; a zero vector reads as an omni burst.</param>
    /// <param name="kind">What was struck, which composes the burst.</param>
    /// <param name="intensity">The <c>r_impact_particles</c> value, scaling particle counts.</param>
    public void SpawnBurst(Vector3d positionUnits, Vector3f normal, ImpactSurfaceKind kind, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!(intensity > 0f))
        {
            return;
        }

        (int sparks, int dust) = kind switch
        {
            ImpactSurfaceKind.Metal => (12, 2),
            ImpactSurfaceKind.Stone => (6, 5),
            ImpactSurfaceKind.Dirt => (2, 7),
            ImpactSurfaceKind.Wood => (4, 4),
            ImpactSurfaceKind.Flesh => (0, 6),
            _ => (6, 5),
        };

        sparks = (int)(sparks * Math.Min(intensity, 2f));
        dust = (int)(dust * Math.Min(intensity, 2f));

        for (int i = 0; i < sparks; i++)
        {
            EmitSpark(positionUnits, normal, kind);
        }

        for (int i = 0; i < dust; i++)
        {
            EmitDust(positionUnits, normal, kind);
        }
    }

    /// <summary>Spawns the retail Explosion0 fireball, fragments and smoke column.</summary>
    /// <param name="positionUnits">The blast's centre, in world units.</param>
    /// <param name="intensity">The particle-count scale; one is the authored look.</param>
    /// <param name="fireballScale">The authored <c>Explosion fireballs</c> value.</param>
    /// <param name="fragments">The authored <c>Explosion fragments</c> count.</param>
    /// <param name="includeSmoke">Whether the native <c>ExplosionSmoke</c> child is present.</param>
    /// <remarks>
    /// With the retail models bound this is one <see cref="SpawnRetailExplosion(Vector3d, RetailExplosionShape)"/>
    /// FireBall a metre of range per unit of <paramref name="fireballScale"/>; callers that know their
    /// <c>Explosion0</c> arguments call that directly. The procedural burst is kept for installations
    /// where the retail effect models cannot be resolved.
    /// </remarks>
    public void SpawnExplosion(
        Vector3d positionUnits,
        float intensity,
        float fireballScale = 1f,
        int fragments = 0,
        bool includeSmoke = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!(intensity > 0f))
        {
            return;
        }

        if (UsesRetailExplosionModel && UsesRetailFireballModel)
        {
            float authoredScale = fireballScale > 0f ? fireballScale : 1f;
            SpawnRetailExplosion(
                positionUnits,
                new RetailExplosionShape(
                    FullRangeUnits: authoredScale * UnitsPerMetre,
                    Fireballs: 1,
                    Fragments: Math.Max(0, fragments)),
                includeSmoke);
            return;
        }

        float scale = Math.Clamp(fireballScale, 0.25f, 2f);
        int embers = Math.Max(1, (int)MathF.Round(18f * Math.Min(intensity, 2f) * scale));
        int fragmentCount = fragments > 0
            ? Math.Max(1, (int)MathF.Round(fragments * Math.Min(intensity, 2f)))
            : Math.Max(4, (int)MathF.Round(8f * Math.Min(intensity, 2f)));
        int smoke = includeSmoke ? (int)(14 * Math.Min(intensity, 2f)) : 0;
        Vector3f up = new(0f, 0f, 1f);

        for (int i = 0; i < embers; i++)
        {
            float speed = (6f + ((float)_random.NextDouble() * 16f)) * UnitsPerMetre / 30f;
            Vector3f direction = ScatterAround(up * 0.35f, spread: 1f);
            float heat = 0.6f + ((float)_random.NextDouble() * 0.4f);

            Emit(new ImpactParticle
            {
                Position = positionUnits,
                PreviousPosition = positionUnits,
                Velocity = direction * speed,
                LifeTicks = 12 + (int)_random.NextBounded(17),
                Size = 0.06f + ((float)_random.NextDouble() * 0.06f),
                Growth = 0f,
                GravityScale = 1f,
                Drag = 0.95f,
                Red = 1f,
                Green = 0.55f * heat + 0.2f,
                Blue = 0.15f * heat,
                IsSpark = true,
            });
        }

        for (int i = 0; i < fragmentCount; i++)
        {
            float speed = (8f + ((float)_random.NextDouble() * 14f)) * UnitsPerMetre / 30f;
            Vector3f direction = ScatterAround(up * 0.15f, spread: 1.1f);

            Emit(new ImpactParticle
            {
                Position = positionUnits,
                PreviousPosition = positionUnits,
                Velocity = direction * speed,
                LifeTicks = 16 + (int)_random.NextBounded(18),
                Size = 0.035f + ((float)_random.NextDouble() * 0.045f),
                Growth = 0f,
                GravityScale = 1.25f,
                Drag = 0.965f,
                Red = 1f,
                Green = 0.68f + ((float)_random.NextDouble() * 0.2f),
                Blue = 0.28f,
                IsSpark = true,
            });
        }

        for (int i = 0; i < smoke; i++)
        {
            float speed = (1f + ((float)_random.NextDouble() * 3f)) * UnitsPerMetre / 30f;
            Vector3f direction = ScatterAround(up, spread: 0.8f);
            float shade = 0.5f + ((float)_random.NextDouble() * 0.5f);

            Emit(new ImpactParticle
            {
                Position = positionUnits,
                PreviousPosition = positionUnits,
                Velocity = direction * speed,
                LifeTicks = 35 + (int)_random.NextBounded(31),
                Size = 0.35f + ((float)_random.NextDouble() * 0.3f),
                Growth = 0.02f + ((float)_random.NextDouble() * 0.015f),
                GravityScale = -0.04f,
                Drag = 0.92f,
                Red = 0.16f * shade,
                Green = 0.15f * shade,
                Blue = 0.14f * shade,
                IsSpark = false,
            });
        }
    }

    /// <summary>Spawns one retail <c>Explosion0</c>: its FireBalls, its smoke and its flying fragments.</summary>
    /// <param name="positionUnits">The blast's centre, in world units.</param>
    /// <param name="shape">The caller's <c>Explosion0</c> arguments.</param>
    /// <remarks>
    /// <para>
    /// This is <c>FUN_0047F200</c> as the destructible objects reach it through <c>FUN_00480FE0</c>
    /// (the camera lens <c>0x441770</c>, the alarm light <c>0x43A5F0</c>, <c>ExplodeMagicObj</c>
    /// <c>0x43C0B0</c>, <c>ExplodeObject</c> <c>0x481A60</c>). The flame is <c>1006_01_1</c> FireBalls
    /// sized by the full damage range; the debris is <c>1009_01_1</c>, a small rock, thrown as
    /// separate pieces a tenth of its size. No sprite is drawn over the blast: <c>fireball.spr</c> only
    /// ever trails a fragment spawned with a trail, and none of these callers ask for one.
    /// </para>
    /// <para>
    /// Retail creates <c>ExplosionSmoke</c> whenever the blast has a FireBall; the smoke here stays the
    /// port's two-puff approximation of that particle system.
    /// </para>
    /// </remarks>
    public void SpawnRetailExplosion(Vector3d positionUnits, RetailExplosionShape shape) =>
        SpawnRetailExplosion(positionUnits, shape, includeSmoke: shape.Fireballs > 0);

    private void SpawnRetailExplosion(Vector3d positionUnits, RetailExplosionShape shape, bool includeSmoke)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!UsesRetailExplosionModel || !UsesRetailFireballModel)
        {
            SpawnExplosion(positionUnits, 1f, shape.FullRangeUnits / UnitsPerMetre, shape.Fragments, includeSmoke);
            return;
        }

        _explosions.Spawn(positionUnits, shape, includeSmoke);
    }

    /// <summary>Lays one soft puff of a launcher rocket's smoke trail.</summary>
    /// <param name="positionUnits">Where along the flight path the puff is left, in world units.</param>
    /// <param name="velocityUnits">The rocket's velocity, whose reverse the exhaust drifts along.</param>
    /// <param name="intensity">The particle-count scale; one is the authored look.</param>
    /// <remarks>
    /// The retail missile task lays smoke every tick while its motor burns (<c>0x00428630</c>, gated on
    /// <c>cfg+280</c> and the burn timer). A puff is dropped where the rocket was, not carried with it, so
    /// a stationary column is left hanging in the air; it drifts slowly back along the exhaust, expands
    /// and greys out. This is that behaviour through the same soft billboard the dust uses.
    /// </remarks>
    public void SpawnRocketSmoke(Vector3d positionUnits, Vector3f velocityUnits, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!(intensity > 0f))
        {
            return;
        }

        // A little of the exhaust's reverse, so the column trails behind the nozzle rather than sitting
        // dead still, plus a small omni jitter so it is a cloud and not a bead.
        float back = 0.04f + ((float)_random.NextDouble() * 0.03f);
        Vector3f drift = new(
            (-velocityUnits.X * back) + (((float)_random.NextDouble() - 0.5f) * 220f),
            (-velocityUnits.Y * back) + (((float)_random.NextDouble() - 0.5f) * 220f),
            (-velocityUnits.Z * back) + (((float)_random.NextDouble() - 0.5f) * 220f) + 60f);

        float shade = 0.62f + ((float)_random.NextDouble() * 0.28f);

        Emit(new ImpactParticle
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            Velocity = drift,
            LifeTicks = 26 + (int)_random.NextBounded(14),
            Size = 0.28f + ((float)_random.NextDouble() * 0.18f),
            Growth = 0.045f + ((float)_random.NextDouble() * 0.03f),
            GravityScale = -0.03f,
            Drag = 0.9f,
            Red = 0.72f * shade,
            Green = 0.71f * shade,
            Blue = 0.70f * shade,
            IsSpark = false,
        });
    }

    /// <summary>Creates retail-style glass shards from the placed pane's collision triangles.</summary>
    /// <param name="placement">The exact glass placement that was hit.</param>
    /// <param name="impactUnits">The bullet's world-space impact, in engine units.</param>
    /// <remarks>
    /// Retail <c>GlassShatter_CreateShards</c> first selects collision faces with the signed
    /// dot-product test in <c>FUN_0044BD70</c>, then walks the selected faces around the impact.
    /// Reading the same HSMC mesh and preserving that winding test prevents the back-facing half of
    /// a pane from becoming shards: nearby front-facing faces become translucent pieces, kicked away
    /// from the impact and then pulled down.
    /// </remarks>
    public void SpawnGlassShatter(WorldPlacement placement, Vector3d impactUnits)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(placement);

        List<GlassShard>? candidates = GlassShatterBuilder.Build(placement, impactUnits, _random);
        if (candidates is null)
        {
            return;
        }

        // FUN_0044a670 caps the shard records at 999. Keep that retail cap instead of the old
        // presentation-only 48-face shortcut; the renderer's 1024-particle pool still bounds the
        // worst case without changing the native selection order.
        // The native routine stops appending at 999; it does not sort the records after recursive
        // subdivision. Preserve that creation order so the same early-out faces survive the cap.
        foreach (GlassShard shard in candidates.Take(999))
        {
            Emit(new ImpactParticle
            {
                Position = shard.Centre,
                PreviousPosition = shard.Centre,
                Velocity = shard.Velocity,
                LifeTicks = shard.LifeTicks,
                Size = shard.SizeMetres,
                Growth = 0f,
                GravityScale = 0.9f,
                Drag = 0.975f,
                Red = 0.72f,
                Green = 0.92f,
                Blue = 1f,
                IsSpark = false,
                IsGlassShard = true,
                GlassA = shard.A - shard.Centre,
                GlassB = shard.B - shard.Centre,
                GlassC = shard.C - shard.Centre,
                GlassNormal = shard.Normal,
            });
        }

    }

    /// <summary>Advances every particle by one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _explosions.Tick();

        for (int i = _count - 1; i >= 0; i--)
        {
            ref ImpactParticle p = ref _particles[i];

            if (++p.AgeTicks >= p.LifeTicks)
            {
                p = _particles[--_count];
                continue;
            }

            p.PreviousPosition = p.Position;
            p.Velocity = new Vector3f(
                p.Velocity.X * p.Drag,
                p.Velocity.Y * p.Drag,
                (p.Velocity.Z - (GravityPerTick * p.GravityScale)) * p.Drag);
            p.Position = new Vector3d(
                p.Position.X + p.Velocity.X,
                p.Position.Y + p.Velocity.Y,
                p.Position.Z + p.Velocity.Z);
            p.Size += p.Growth;

            if (p.IsRetailSprite)
            {
                int frameCount = p.IsRetailSmoke ? _retailSmokeFrames.Length : _retailFireballFrames.Length;
                int frameIndex = frameCount > 0
                    ? Math.Min(frameCount - 1, (int)(((long)p.AgeTicks * frameCount) / p.LifeTicks))
                    : 0;
                SelectRetailSpriteFrame(ref p, p.IsRetailSmoke, frameIndex);
            }
        }
    }

    /// <summary>Draws every particle: the dust haze first, the sparks' light over it.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_count == 0 && !_explosions.HasDrawable)
        {
            return;
        }

        _explosions.Draw(camera, projection, alpha);

        if (_count == 0)
        {
            return;
        }

        Vector3d eye = camera.Position;
        _quads.Reset();

        // Dust first so the additive sparks glow over the haze rather than under it. Two batches of
        // one shared mesh, drawn as index ranges -- the decal renderer's pattern.
        int dustFirst = _quads.Used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref ImpactParticle p = ref _particles[i];
            if (p.IsSpark || p.IsGlassShard || p.IsRetailSprite)
            {
                continue;
            }

            _quads.AddDustQuad(ref p, eye, alpha);
        }

        int dustCount = (_quads.Used / 4) - dustFirst;

        int glassFirst = _quads.Used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref ImpactParticle p = ref _particles[i];
            if (!p.IsGlassShard)
            {
                continue;
            }

            _quads.AddGlassShardQuad(ref p, eye, alpha);
        }

        int glassCount = (_quads.Used / 4) - glassFirst;

        int retailSmokeFirst = _quads.Used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref ImpactParticle p = ref _particles[i];
            if (!p.IsRetailSprite || !p.IsRetailSmoke)
            {
                continue;
            }

            _quads.AddRetailSpriteQuad(ref p, eye, alpha);
        }

        int retailSmokeCount = (_quads.Used / 4) - retailSmokeFirst;

        int retailFireballFirst = _quads.Used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref ImpactParticle p = ref _particles[i];
            if (!p.IsRetailSprite || p.IsRetailSmoke)
            {
                continue;
            }

            _quads.AddRetailSpriteQuad(ref p, eye, alpha);
        }

        int retailFireballCount = (_quads.Used / 4) - retailFireballFirst;

        int sparkFirst = _quads.Used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref ImpactParticle p = ref _particles[i];
            if (!p.IsSpark || p.IsRetailSprite)
            {
                continue;
            }

            _quads.AddSparkQuad(ref p, eye, alpha);
        }

        int sparkCount = (_quads.Used / 4) - sparkFirst;
        if (_quads.Used == 0)
        {
            return;
        }

        if (_lastUsed > _quads.Used)
        {
            Array.Clear(_quads.Vertices, _quads.Used, _lastUsed - _quads.Used);
        }

        _lastUsed = _quads.Used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_quads.Vertices, QuadIndices.Build(MaximumParticles));
            }

            _device.UpdateAnimatedMesh(_mesh, _quads.Vertices);
            _device.ClearPoseInterpolation();

            if (dustCount > 0)
            {
                _device.DrawMesh(
                    _mesh,
                    dustFirst * 6,
                    dustCount * 6,
                    modelView,
                    GetDustTexture(),
                    TextureHandle.None,
                    BlendMode.AlphaBlend);
            }

            if (glassCount > 0)
            {
                _device.DrawMesh(
                    _mesh,
                    glassFirst * 6,
                    glassCount * 6,
                    modelView,
                    GetGlassTexture(),
                    TextureHandle.None,
                    BlendMode.AlphaBlend);
            }

            if (retailSmokeCount > 0)
            {
                _device.DrawMesh(
                    _mesh,
                    retailSmokeFirst * 6,
                    retailSmokeCount * 6,
                    modelView,
                    _retailSmokeSpriteTexture,
                    TextureHandle.None,
                    BlendMode.AlphaBlend);
            }

            if (retailFireballCount > 0)
            {
                _device.DrawMesh(
                    _mesh,
                    retailFireballFirst * 6,
                    retailFireballCount * 6,
                    modelView,
                    _retailFireballSpriteTexture,
                    TextureHandle.None,
                    BlendMode.Additive);
            }

            if (sparkCount > 0)
            {
                _device.DrawMesh(
                    _mesh,
                    sparkFirst * 6,
                    sparkCount * 6,
                    modelView,
                    GetSparkTexture(),
                    TextureHandle.None,
                    BlendMode.Additive);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // A burst that cannot upload this frame is simply not drawn.
            _ = ex;
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <summary>Removes every particle, as changing level does.</summary>
    public void Clear()
    {
        _count = 0;
        _explosions.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }

        if (_sparkTexture.IsValid)
        {
            _device.DestroyTexture(_sparkTexture);
            _sparkTexture = TextureHandle.None;
        }

        if (_dustTexture.IsValid)
        {
            _device.DestroyTexture(_dustTexture);
            _dustTexture = TextureHandle.None;
        }

        if (_retailFireballSpriteTexture.IsValid)
        {
            _device.DestroyTexture(_retailFireballSpriteTexture);
            _retailFireballSpriteTexture = TextureHandle.None;
        }

        if (_retailSmokeSpriteTexture.IsValid)
        {
            _device.DestroyTexture(_retailSmokeSpriteTexture);
            _retailSmokeSpriteTexture = TextureHandle.None;
        }

        if (_glassTexture.IsValid)
        {
            _device.DestroyTexture(_glassTexture);
            _glassTexture = TextureHandle.None;
        }

        _explosions.Release();
    }

    private void EmitSpark(Vector3d positionUnits, Vector3f normal, ImpactSurfaceKind kind)
    {
        // Metres a second, then world units a tick.
        float speed = (5f + ((float)_random.NextDouble() * 9f)) * UnitsPerMetre / 30f;
        Vector3f direction = ScatterAround(normal, spread: 0.85f);

        float warmth = 0.55f + ((float)_random.NextDouble() * 0.45f);
        (float r, float g, float b) = kind == ImpactSurfaceKind.Wood
            ? (0.9f * warmth, 0.6f * warmth, 0.3f * warmth)
            : (1f, 0.72f * warmth + 0.28f, 0.35f * warmth);

        Emit(new ImpactParticle
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            Velocity = direction * speed,
            LifeTicks = 10 + (int)_random.NextBounded(11),
            Size = 0.035f + ((float)_random.NextDouble() * 0.03f),
            Growth = 0f,
            GravityScale = 1f,
            Drag = 0.96f,
            Red = r,
            Green = g,
            Blue = b,
            IsSpark = true,
        });
    }

    private void EmitRetailSprite(
        Vector3d positionUnits,
        Vector3f velocity,
        float sizeMetres,
        int lifeTicks,
        bool smoke)
    {
        ImpactParticle particle = new()
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            Velocity = velocity,
            LifeTicks = Math.Max(1, lifeTicks),
            Size = Math.Max(0.01f, sizeMetres),
            Growth = smoke ? Math.Max(0.005f, sizeMetres * 0.02f) : 0f,
            GravityScale = smoke ? -0.04f : 0f,
            Drag = smoke ? 0.98f : 1f,
            Red = 1f,
            Green = 1f,
            Blue = 1f,
            IsSpark = !smoke,
            IsRetailSprite = true,
            IsRetailSmoke = smoke,
        };
        SelectRetailSpriteFrame(ref particle, smoke, frameIndex: 0);
        Emit(particle);
    }

    private void EmitDust(Vector3d positionUnits, Vector3f normal, ImpactSurfaceKind kind)
    {
        float speed = (0.6f + ((float)_random.NextDouble() * 1.6f)) * UnitsPerMetre / 30f;
        Vector3f direction = ScatterAround(normal, spread: 1.1f);

        float shade = 0.8f + ((float)_random.NextDouble() * 0.4f);
        (float r, float g, float b) = kind switch
        {
            ImpactSurfaceKind.Dirt => (0.4f * shade, 0.32f * shade, 0.22f * shade),
            ImpactSurfaceKind.Wood => (0.38f * shade, 0.28f * shade, 0.18f * shade),
            ImpactSurfaceKind.Flesh => (0.4f * shade, 0.05f * shade, 0.05f * shade),
            ImpactSurfaceKind.Metal => (0.5f * shade, 0.5f * shade, 0.52f * shade),
            _ => (0.46f * shade, 0.45f * shade, 0.43f * shade),
        };

        Emit(new ImpactParticle
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            Velocity = direction * speed,
            LifeTicks = 22 + (int)_random.NextBounded(23),
            Size = 0.11f + ((float)_random.NextDouble() * 0.07f),
            Growth = 0.010f + ((float)_random.NextDouble() * 0.007f),
            GravityScale = kind == ImpactSurfaceKind.Flesh ? 0.35f : 0.12f,
            Drag = 0.90f,
            Red = r,
            Green = g,
            Blue = b,
            IsSpark = false,
        });
    }

    private void Emit(in ImpactParticle particle)
    {
        if (_count >= MaximumParticles)
        {
            return;
        }

        _particles[_count++] = particle;
    }

    // A direction biased along the surface normal, opened by the spread. A zero normal -- a hit on a
    // body, whose facing nobody recorded -- degrades to a uniform scatter.
    private Vector3f ScatterAround(Vector3f normal, float spread)
    {
        Vector3f random;
        float lengthSquared;
        do
        {
            random = new Vector3f(
                ((float)_random.NextDouble() * 2f) - 1f,
                ((float)_random.NextDouble() * 2f) - 1f,
                ((float)_random.NextDouble() * 2f) - 1f);
            lengthSquared = (random.X * random.X) + (random.Y * random.Y) + (random.Z * random.Z);
        }
        while (!(lengthSquared > 1e-6f) || lengthSquared > 1f);

        Vector3f direction = normal + (random * spread);
        float length = MathF.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        return !(length > 1e-6f) ? new Vector3f(0f, 0f, 1f) : direction / length;
    }

    private void SelectRetailSpriteFrame(ref ImpactParticle particle, bool smoke, int frameIndex)
    {
        SpriteStripFrame[] frames = smoke ? _retailSmokeFrames : _retailFireballFrames;
        if (frames.Length == 0)
        {
            particle.RetailU0 = 0f;
            particle.RetailV0 = 0f;
            particle.RetailU1 = 1f;
            particle.RetailV1 = 1f;
            return;
        }

        SpriteStripFrame frame = frames[Math.Clamp(frameIndex, 0, frames.Length - 1)];
        particle.RetailU0 = frame.U0;
        particle.RetailV0 = frame.V0;
        particle.RetailU1 = frame.U1;
        particle.RetailV1 = frame.V1;
    }

    private TextureHandle GetSparkTexture()
    {
        if (!_sparkTexture.IsValid)
        {
            _sparkTexture = ImpactTextures.CreateSpark(_device);
        }

        return _sparkTexture;
    }

    private TextureHandle GetDustTexture()
    {
        if (!_dustTexture.IsValid)
        {
            _dustTexture = ImpactTextures.CreateDust(_device);
        }

        return _dustTexture;
    }

    private TextureHandle GetGlassTexture()
    {
        if (!_glassTexture.IsValid)
        {
            _glassTexture = ImpactTextures.CreateGlass(_device);
        }

        return _glassTexture;
    }
}

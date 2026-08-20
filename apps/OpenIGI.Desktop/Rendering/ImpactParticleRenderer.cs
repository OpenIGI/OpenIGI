using OpenIGI.Core;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>What a burst of impact particles looks like, decided by what the round struck.</summary>
public enum ImpactSurfaceKind
{
    /// <summary>Concrete, stone, brick — grey dust with a few sparks. The fallback.</summary>
    Stone,

    /// <summary>Metal — a shower of hot sparks and almost no dust.</summary>
    Metal,

    /// <summary>Ground, sand, gravel, grass — a slow brown plume and hardly any sparks.</summary>
    Dirt,

    /// <summary>Wood — warm chips and a modest puff.</summary>
    Wood,

    /// <summary>A body — a brief red mist, no sparks.</summary>
    Flesh,
}

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

    private const int MaximumVertices = MaximumParticles * 4;
    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;
    // FUN_004810a0 stores param_2 (the ExplodeMagicObj caller passes 1) as Explosion0's repeat
    // count at +0x38. FUN_00480f40 runs FUN_0047f200 once, decrements that count, then destroys the
    // parent and its 1009_01_1 child. The caller's 0x15 is a FUN_00450CE0 child-task descriptor
    // argument, not this model's lifetime.
    private const int RetailExplosionLifeTicks = 1;
    private const int RetailFireballLifeTicks = 75;
    private const int RetailExplosionSmokeTicks = 150;
    private const int RetailExplosionSmokeSecondSpawnAge = 15;
    private const float RetailExplosionModelScale = 0.9f;
    private const float RetailFireballModelScale = 0.25f;
    private const float RetailExplosionDrag = 0.98f;
    private const float RetailExplosionAngleDrag = 0.99f;
    private const float RetailExplosionAngleRange = 0.628318548f;
    private const float RetailExplosionGravity = 44.600006f;
    private const string RetailExplosionModelName = "1009_01_1";
    private const string RetailFireballModelName = "1006_01_1";

    /// <summary>Real gravity per tick², in world units — the same 9.8 m/s² the ragdoll falls at.</summary>
    private const float GravityPerTick = 44.600887f;

    // The muzzle flame's recipe: fully emissive so the forward shader treats the quads as light
    // (sparks) or self-shaded haze (dust) rather than as surfaces to be lit.
    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private readonly IgiRandom _random = new();
    private readonly Particle[] _particles = new Particle[MaximumParticles];
    private readonly List<RetailExplosion> _retailExplosions = [];
    private readonly List<RetailFireball> _retailFireballs = [];
    private readonly List<RetailSmoke> _retailSmokes = [];
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private int _count;
    private int _used;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _sparkTexture = TextureHandle.None;
    private TextureHandle _dustTexture = TextureHandle.None;
    private readonly RetailSpriteFrame[] _sparkFrames;
    private readonly RetailSpriteFrame[] _dustFrames;
    private TextureHandle _glassTexture = TextureHandle.None;
    private MeshGeometry? _retailExplosionGeometry;
    private MeshHandle _retailExplosionMesh = MeshHandle.None;
    private TextureHandle _retailExplosionTexture = TextureHandle.None;
    private MeshGeometry? _retailFireballGeometry;
    private MeshHandle _retailFireballMesh = MeshHandle.None;
    private TextureHandle _retailFireballTexture = TextureHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImpactParticleRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="fileSystem">The retail VFS, used to load fireball/smoke sprite frames when available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ImpactParticleRenderer(IGraphicsDevice device, VirtualFileSystem? fileSystem = null)
        : this(device, resources: null, fileSystem)
    {
    }

    /// <summary>Initializes the renderer with the mission resource registry as well as the VFS.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">The bound retail resources, used for the authored explosion model texture.</param>
    /// <param name="fileSystem">The retail VFS, used to load fireball/smoke sprite frames.</param>
    public ImpactParticleRenderer(
        IGraphicsDevice device,
        ResourceRegistry? resources,
        VirtualFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _resources = resources;
        _sparkFrames = [];
        _dustFrames = [];
        if (fileSystem is not null)
        {
            RetailSpriteAsset spark = LoadRetailSprite(fileSystem, "fireball.spr");
            _sparkTexture = spark.Texture;
            _sparkFrames = spark.Frames;
            RetailSpriteAsset dust = LoadRetailSprite(fileSystem, "smoke2.spr");
            _dustTexture = dust.Texture;
            _dustFrames = dust.Frames;
        }
    }

    /// <summary>Gets how many particles are alive.</summary>
    public int LiveCount => _count;

    /// <summary>Gets whether the retail fireball and smoke sprites were loaded from the common archive.</summary>
    public bool UsesRetailEffectSprites => _sparkTexture.IsValid && _dustTexture.IsValid;

    /// <summary>Gets the decoded retail fireball frame count, for parity diagnostics and tests.</summary>
    internal int RetailFireballFrameCount => _sparkFrames.Length;

    /// <summary>Gets the decoded retail smoke frame count, for parity diagnostics and tests.</summary>
    internal int RetailSmokeFrameCount => _dustFrames.Length;

    /// <summary>Gets whether the IGI1 common pack supplied the native <c>1009_01_1</c> effect model.</summary>
    public bool UsesRetailExplosionModel => _retailExplosionGeometry is not null
        && _retailExplosionTexture.IsValid;

    /// <summary>Gets whether the common pack supplied the native 1006 FireBall model.</summary>
    internal bool UsesRetailFireballModel => _retailFireballGeometry is not null
        && _retailFireballTexture.IsValid;

    /// <summary>
    /// Binds the IGI1 authored fireball model and its common-pack material.
    /// </summary>
    /// <param name="mission">The loaded IGI1 mission whose common pack owns the effect.</param>
    /// <returns><see langword="true"/> when <c>1009_01_1</c> and its texture resolved.</returns>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReleaseRetailExplosionModel();
        ReleaseRetailFireballModel();
        if (_resources is null)
        {
            return false;
        }

        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                bool isExplosion = string.Equals(instance.Name, RetailExplosionModelName, StringComparison.OrdinalIgnoreCase);
                bool isFireball = string.Equals(instance.Name, RetailFireballModelName, StringComparison.OrdinalIgnoreCase);
                if ((!isExplosion && !isFireball)
                    || !pack.TryGetModel(instance, out MefModel? model)
                    || model is null)
                {
                    continue;
                }

                MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f / UnitsPerMetre);
                if (geometry is null)
                {
                    continue;
                }

                if (!pack.TryGetTextureName(instance, 0, out string textureName))
                {
                    continue;
                }

                TextureHandle texture = LoadRetailTexture(textureName);
                if (!texture.IsValid)
                {
                    continue;
                }

                if (isExplosion)
                {
                    _retailExplosionGeometry = geometry;
                    _retailExplosionTexture = texture;
                }
                else
                {
                    _retailFireballGeometry = geometry;
                    _retailFireballTexture = texture;
                }
            }
        }

        return UsesRetailExplosionModel;
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
    /// IGI1's <c>ExplodeMagicObj</c> calls <c>FUN_00480fe0</c>, which creates the authored
    /// <c>1009_01_1</c> child and passes the fireball field through the native
    /// <c>0.000219726557 × 4096</c> scale conversion. The model is therefore the primary fireball
    /// path when the common pack is bound. The renderer creates the matching fireball sprite, exact
    /// authored fragment count, and optional smoke child from the same common archive. The procedural
    /// burst is retained only for installations where the retail effect assets cannot be resolved.
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

        if (UsesRetailExplosionModel && UsesRetailEffectSprites)
        {
            float authoredScale = fireballScale > 0f ? fireballScale : 1f;
            // FUN_00480FE0 consumes three random values for the normalized launch vector and
            // FUN_004810A0 consumes three more for the native Simpoint Euler angles. Keep both
            // draws in the same order as retail; the vector is owned by the native child task,
            // while these angles are what FUN_005035B0 damps and FUN_004B38E0 composes for draw.
            _ = new Vector3f(
                _random.NextSingle() - 0.5f,
                _random.NextSingle() - 0.5f,
                _random.NextSingle() - 0.3f);
            Vector3f rotationAngles = new(
                _random.NextSingle() * RetailExplosionAngleRange,
                _random.NextSingle() * RetailExplosionAngleRange,
                _random.NextSingle() * RetailExplosionAngleRange);
            _retailExplosions.Add(new RetailExplosion(
                positionUnits,
                authoredScale * RetailExplosionModelScale,
                RetailExplosionLifeTicks,
                Velocity: Vector3f.Zero,
                RotationAngles: rotationAngles));

            // FUN_0047F200 creates the native FireBall model (1006_01_1) with a 75-tick
            // countdown and a 0.25 scale conversion. It also owns the fireball sprite; keep
            // both visual tasks because retail draws both when the model is available.
            if (UsesRetailFireballModel)
            {
                _retailFireballs.Add(new RetailFireball(
                    positionUnits,
                    authoredScale * RetailFireballModelScale,
                    RetailFireballLifeTicks,
                    rotationAngles));
            }

            // ExplodeMagicObj passes 0x15/2 through the child-task descriptor consumed by
            // FUN_00450CE0; neither value is the 1009 lifetime. The 1009 Simpoint, 1006 FireBall,
            // and sprite are separate native tasks, so keep their records independent instead of
            // replacing them with one generic cloud.
            EmitRetailSprite(
                positionUnits,
                velocity: Vector3f.Zero,
                sizeMetres: 0.9f * authoredScale,
                lifeTicks: RetailExplosionLifeTicks,
                smoke: false);

            for (int i = 0; i < Math.Max(0, fragments); i++)
            {
                float speed = (2f + ((float)_random.NextDouble() * 4f)) * UnitsPerMetre / 30f;
                EmitRetailSprite(
                    positionUnits,
                    ScatterAround(new Vector3f(0f, 0f, 0.25f), spread: 1f) * speed,
                    sizeMetres: 0.12f * authoredScale,
                    lifeTicks: RetailExplosionLifeTicks,
                    smoke: false);
            }

            if (includeSmoke)
            {
                // Explosion0 creates ExplosionSmoke with a 150-tick lifetime. The native task
                // emits its first smoke system immediately and its second at round(total * .9),
                // rather than destroying one billboard after the 21-tick model lifetime.
                EmitRetailSprite(
                    positionUnits,
                    new Vector3f(0f, 0f, UnitsPerMetre / 30f),
                    sizeMetres: 0.8f * authoredScale,
                    lifeTicks: RetailExplosionSmokeTicks,
                    smoke: true);
                _retailSmokes.Add(new RetailSmoke(
                    positionUnits,
                    authoredScale,
                    RetailExplosionSmokeTicks,
                    RetailExplosionSmokeSecondSpawnAge));
            }

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

            Emit(new Particle
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

            Emit(new Particle
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

            Emit(new Particle
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

        Emit(new Particle
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

        MefCollisionMesh? mesh;
        try
        {
            if (!MefCollisionMesh.TryRead(placement.Model.Container, out mesh) || mesh is null)
            {
                return;
            }
        }
        catch (PackFormatException)
        {
            // A malformed optional collision payload must not take down the render loop.
            return;
        }

        Vector3d originUnits = placement.Position * WorldScene.UnitsPerMetre;
        Matrix3f localToWorld = placement.Orientation.Transposed();
        // FUN_0044BD70 compares the local collision-face normal with (object position - contact
        // position), not with the rendered world-space normal. Matrix3f.Transform is the inverse of
        // the draw transform for this row-vector convention, so transform the contact back into
        // collision-local space before applying the strict native '< 0' test.
        Vector3d impactLocal = placement.Orientation.Transform(impactUnits - originUnits);
        Vector3d faceFilterDirection = -impactLocal;
        List<GlassShard> candidates = [];

        foreach (MefCollisionFace face in mesh.Faces)
        {
            if ((uint)face.I0 >= (uint)mesh.Vertices.Count
                || (uint)face.I1 >= (uint)mesh.Vertices.Count
                || (uint)face.I2 >= (uint)mesh.Vertices.Count)
            {
                continue;
            }

            Vector3d a = new(mesh.Vertices[face.I0].X, mesh.Vertices[face.I0].Y, mesh.Vertices[face.I0].Z);
            Vector3d b = new(mesh.Vertices[face.I1].X, mesh.Vertices[face.I1].Y, mesh.Vertices[face.I1].Z);
            Vector3d c = new(mesh.Vertices[face.I2].X, mesh.Vertices[face.I2].Y, mesh.Vertices[face.I2].Z);
            Vector3d edgeA = b - a;
            Vector3d edgeB = c - a;
            Vector3d normal = Vector3d.Cross(edgeA, edgeB);
            double normalLength = normal.Length;
            if (!(normalLength > 1e-6))
            {
                continue;
            }

            // This is intentionally strict. The retail code calls FUN_0044A670 only when the
            // signed face test is negative; zero and the opposite winding are skipped.
            if (!PassesRetailGlassFaceFilter(faceFilterDirection, a, b, c))
            {
                continue;
            }

            AppendGlassShards(
                candidates,
                a,
                b,
                c,
                normal / normalLength,
                impactLocal,
                originUnits,
                localToWorld,
                depth: 0);
        }

        // FUN_0044a670 caps the shard records at 999. Keep that retail cap instead of the old
        // presentation-only 48-face shortcut; the renderer's 1024-particle pool still bounds the
        // worst case without changing the native selection order.
        // The native routine stops appending at 999; it does not sort the records after recursive
        // subdivision. Preserve that creation order so the same early-out faces survive the cap.
        foreach (GlassShard shard in candidates.Take(999))
        {
            Emit(new Particle
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

    /// <summary>
    /// Splits a collision triangle using the distance-aware, longest-edge recursion in native
    /// <c>FUN_0044A670</c>. The retail routine measures the contact against every edge, converts that
    /// distance with <c>1/5120</c>, and keeps splitting while any edge exceeds
    /// <c>409.6 + 4505.6 * normalizedDistance</c> engine units. Its split point is deliberately not a
    /// midpoint: the short/long edge ratio is clamped around 0.5 and offset by the retail random
    /// range of 0.1. Keeping the recursion order is important because the native 999-record cap is
    /// an insertion-order cap.
    /// </summary>
    private void AppendGlassShards(
        List<GlassShard> shards,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        Vector3d normal,
        Vector3d impactLocal,
        Vector3d originUnits,
        Matrix3f localToWorld,
        int depth)
    {
        if (shards.Count >= 999)
        {
            return;
        }

        // The depth guard is only a malformed-mesh safety net. Normal retail triangles terminate by
        // the edge threshold; it prevents a degenerate collision payload from exhausting the stack.
        if (depth >= 24)
        {
            AppendGlassShardLeaf(shards, a, b, c, normal, impactLocal, originUnits, localToWorld);
            return;
        }

        double edgeAbSquared = (b - a).LengthSquared;
        double edgeBcSquared = (c - b).LengthSquared;
        double edgeCaSquared = (a - c).LengthSquared;
        double distanceCa = NormalizedGlassEdgeDistance(impactLocal, c, a);
        double distanceAb = NormalizedGlassEdgeDistance(impactLocal, a, b);
        double distanceBc = NormalizedGlassEdgeDistance(impactLocal, b, c);
        double thresholdCa = (distanceCa * 4505.600006d) + 409.600006d;
        double thresholdAb = (distanceAb * 4505.600006d) + 409.600006d;
        double thresholdBc = (distanceBc * 4505.600006d) + 409.600006d;

        if ((thresholdCa * thresholdCa >= edgeCaSquared)
            && (thresholdAb * thresholdAb >= edgeAbSquared)
            && (thresholdBc * thresholdBc >= edgeBcSquared))
        {
            AppendGlassShardLeaf(shards, a, b, c, normal, impactLocal, originUnits, localToWorld);
            return;
        }

        if (edgeBcSquared <= edgeAbSquared && edgeCaSquared <= edgeAbSquared)
        {
            double ratio = edgeBcSquared <= edgeCaSquared
                ? 1d - (Math.Sqrt(edgeBcSquared) / Math.Sqrt(edgeAbSquared))
                : Math.Sqrt(edgeCaSquared) / Math.Sqrt(edgeAbSquared);
            if (edgeBcSquared <= edgeCaSquared)
            {
                ratio = ratio <= 0.5d ? 0.5d : ratio;
                ratio -= _random.NextSingle() * 0.1d;
            }
            else
            {
                ratio = ratio >= 0.5d ? 0.5d : ratio;
                ratio += _random.NextSingle() * 0.1d;
            }

            Vector3d split = a + ((b - a) * ratio);
            AppendGlassShards(shards, b, c, split, normal, impactLocal, originUnits, localToWorld, depth + 1);
            AppendGlassShards(shards, split, c, a, normal, impactLocal, originUnits, localToWorld, depth + 1);
            return;
        }

        if (edgeCaSquared <= edgeBcSquared && edgeAbSquared <= edgeBcSquared)
        {
            double ratio = edgeCaSquared <= edgeAbSquared
                ? 1d - (Math.Sqrt(edgeCaSquared) / Math.Sqrt(edgeBcSquared))
                : Math.Sqrt(edgeAbSquared) / Math.Sqrt(edgeBcSquared);
            if (edgeCaSquared <= edgeAbSquared)
            {
                ratio = ratio <= 0.5d ? 0.5d : ratio;
                ratio -= _random.NextSingle() * 0.1d;
            }
            else
            {
                ratio = ratio >= 0.5d ? 0.5d : ratio;
                ratio += _random.NextSingle() * 0.1d;
            }

            Vector3d split = b + ((c - b) * ratio);
            AppendGlassShards(shards, c, a, split, normal, impactLocal, originUnits, localToWorld, depth + 1);
            AppendGlassShards(shards, split, a, b, normal, impactLocal, originUnits, localToWorld, depth + 1);
            return;
        }

        // The remaining branch is the native CA-longest case. Ties intentionally fall through in
        // the same order as FUN_0044A670's three mutually exclusive comparisons.
        double caRatio = edgeAbSquared <= edgeBcSquared
            ? 1d - (Math.Sqrt(edgeAbSquared) / Math.Sqrt(edgeCaSquared))
            : Math.Sqrt(edgeBcSquared) / Math.Sqrt(edgeCaSquared);
        if (edgeAbSquared <= edgeBcSquared)
        {
            caRatio = caRatio <= 0.5d ? 0.5d : caRatio;
            caRatio -= _random.NextSingle() * 0.1d;
        }
        else
        {
            caRatio = caRatio >= 0.5d ? 0.5d : caRatio;
            caRatio += _random.NextSingle() * 0.1d;
        }

        Vector3d caSplit = c + ((a - c) * caRatio);
        AppendGlassShards(shards, a, b, caSplit, normal, impactLocal, originUnits, localToWorld, depth + 1);
        AppendGlassShards(shards, caSplit, b, c, normal, impactLocal, originUnits, localToWorld, depth + 1);
    }

    private void AppendGlassShardLeaf(
        List<GlassShard> shards,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        Vector3d normal,
        Vector3d impactLocal,
        Vector3d originUnits,
        Matrix3f localToWorld)
    {
        double nearestEdgeDistance = Math.Min(
            NormalizedGlassEdgeDistance(impactLocal, c, a),
            Math.Min(
                NormalizedGlassEdgeDistance(impactLocal, a, b),
                NormalizedGlassEdgeDistance(impactLocal, b, c)));
        int lifeTicks = Math.Max(
            1,
            (int)Math.Round(((1d - nearestEdgeDistance) + _random.NextSingle()) * 0.5d * 30d));

        Vector3d centreLocal = (a + b + c) / 3d;
        Vector3d velocityLocal = new(
            ((_random.NextSingle() * 4096d) - 2048d) * 0.0333333351d,
            ((_random.NextSingle() * 4096d) - 2048d) * 0.0333333351d,
            ((_random.NextSingle() * 4096d) - 2048d) * 0.0333333351d);
        Vector3d worldA = originUnits + localToWorld.Transform(a);
        Vector3d worldB = originUnits + localToWorld.Transform(b);
        Vector3d worldC = originUnits + localToWorld.Transform(c);
        Vector3d worldCentre = originUnits + localToWorld.Transform(centreLocal);
        Vector3d worldNormal = localToWorld.Transform(normal);
        double normalLength = worldNormal.Length;
        if (!(normalLength > 1e-9))
        {
            return;
        }

        Vector3d worldVelocity = localToWorld.Transform(velocityLocal);
        shards.Add(new GlassShard(
            worldCentre,
            worldA,
            worldB,
            worldC,
            worldNormal / normalLength,
            0.0f,
            new Vector3f((float)worldVelocity.X, (float)worldVelocity.Y, (float)worldVelocity.Z),
            lifeTicks));
    }

    internal static bool PassesRetailGlassFaceFilter(Vector3d direction, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d normal = Vector3d.Cross(b - a, c - a);
        return normal.Length > 1e-9 && Vector3d.Dot(direction, normal) < 0d;
    }

    internal static double NormalizedGlassEdgeDistance(Vector3d point, Vector3d start, Vector3d end)
    {
        Vector3d edge = end - start;
        double edgeLengthSquared = edge.LengthSquared;
        if (!(edgeLengthSquared > 1e-12))
        {
            return Math.Clamp((point - start).Length * 0.000195312503d, 0d, 1d);
        }

        double projection = Vector3d.Dot(point - start, edge) / edgeLengthSquared;
        projection = Math.Clamp(projection, 0d, 1d);
        Vector3d nearest = start + (edge * projection);
        return Math.Clamp((point - nearest).Length * 0.000195312503d, 0d, 1d);
    }

    /// <summary>Advances the authored <c>SimpointObj</c> fireball using the retail container update.</summary>
    /// <remarks>
    /// IGI1's <c>FUN_005035B0</c> multiplies velocity by the model drag (<c>0.98</c>) and subtracts
    /// <c>p7 * 44.600006 * 0.98</c> from Z. The native <c>p7</c> is the authored fireball scale after
    /// the <c>4096 * 0.000219726557</c> conversion, so it is the renderer's model scale here.
    /// </remarks>
    internal static (Vector3d PositionUnits, Vector3f Velocity) AdvanceRetailExplosionMotion(
        Vector3d positionUnits,
        Vector3f velocity,
        float modelScale)
    {
        float gravity = modelScale * RetailExplosionGravity * RetailExplosionDrag;
        Vector3f nextVelocity = new(
            velocity.X * RetailExplosionDrag,
            velocity.Y * RetailExplosionDrag,
            (velocity.Z * RetailExplosionDrag) - gravity);
        return (
            new Vector3d(
                positionUnits.X + nextVelocity.X,
                positionUnits.Y + nextVelocity.Y,
                positionUnits.Z + nextVelocity.Z),
            nextVelocity);
    }

    /// <summary>Advances every particle by one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = _retailExplosions.Count - 1; i >= 0; i--)
        {
            RetailExplosion current = _retailExplosions[i];
            (Vector3d positionUnits, Vector3f velocity) = AdvanceRetailExplosionMotion(
                current.PositionUnits,
                current.Velocity,
                current.Scale);
            RetailExplosion explosion = current with
            {
                PositionUnits = positionUnits,
                Velocity = velocity,
                RotationAngles = current.RotationAngles * RetailExplosionAngleDrag,
                AgeTicks = current.AgeTicks + 1,
            };
            if (explosion.AgeTicks >= explosion.LifeTicks)
            {
                _retailExplosions.RemoveAt(i);
            }
            else
            {
                _retailExplosions[i] = explosion;
            }
        }

        for (int i = _retailFireballs.Count - 1; i >= 0; i--)
        {
            RetailFireball fireball = _retailFireballs[i] with
            {
                AgeTicks = _retailFireballs[i].AgeTicks + 1,
            };
            if (fireball.AgeTicks >= fireball.LifeTicks)
            {
                _retailFireballs.RemoveAt(i);
            }
            else
            {
                _retailFireballs[i] = fireball;
            }
        }

        for (int i = _retailSmokes.Count - 1; i >= 0; i--)
        {
            RetailSmoke current = _retailSmokes[i];
            if (!current.SecondEmitted && current.AgeTicks >= current.SecondSpawnAge)
            {
                EmitRetailSprite(
                    current.PositionUnits,
                    new Vector3f(0f, 0f, UnitsPerMetre / 30f),
                    sizeMetres: 0.8f * current.Scale,
                    lifeTicks: RetailExplosionSmokeTicks - current.SecondSpawnAge,
                    smoke: true);
                current = current with { SecondEmitted = true };
            }

            current = current with { AgeTicks = current.AgeTicks + 1 };
            if (current.AgeTicks >= current.TotalTicks)
            {
                _retailSmokes.RemoveAt(i);
            }
            else
            {
                _retailSmokes[i] = current;
            }
        }

        for (int i = _count - 1; i >= 0; i--)
        {
            ref Particle p = ref _particles[i];

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
                int frameCount = p.IsRetailSmoke ? _dustFrames.Length : _sparkFrames.Length;
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

        if (_count == 0 && _retailExplosions.Count == 0 && _retailFireballs.Count == 0)
        {
            return;
        }

        DrawRetailExplosions(camera, projection, alpha);
        DrawRetailFireballs(camera, projection, alpha);

        if (_count == 0)
        {
            return;
        }

        Vector3d eye = camera.Position;
        _used = 0;

        // Dust first so the additive sparks glow over the haze rather than under it. Two batches of
        // one shared mesh, drawn as index ranges -- the decal renderer's pattern.
        int dustFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (p.IsSpark || p.IsGlassShard || p.IsRetailSprite)
            {
                continue;
            }

            AddDustQuad(ref p, eye, alpha);
        }

        int dustCount = (_used / 4) - dustFirst;

        int glassFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.IsGlassShard)
            {
                continue;
            }

            AddGlassShardQuad(ref p, eye, alpha);
        }

        int glassCount = (_used / 4) - glassFirst;

        int retailSmokeFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.IsRetailSprite || !p.IsRetailSmoke)
            {
                continue;
            }

            AddRetailSpriteQuad(ref p, eye, alpha);
        }

        int retailSmokeCount = (_used / 4) - retailSmokeFirst;

        int retailFireballFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.IsRetailSprite || p.IsRetailSmoke)
            {
                continue;
            }

            AddRetailSpriteQuad(ref p, eye, alpha);
        }

        int retailFireballCount = (_used / 4) - retailFireballFirst;

        int sparkFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.IsSpark || p.IsRetailSprite)
            {
                continue;
            }

            AddSparkQuad(ref p, eye, alpha);
        }

        int sparkCount = (_used / 4) - sparkFirst;
        if (_used == 0)
        {
            return;
        }

        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, BuildQuadIndices());
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);
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
                    GetDustTexture(),
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
                    GetSparkTexture(),
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
        _retailExplosions.Clear();
        _retailFireballs.Clear();
        _retailSmokes.Clear();
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

        if (_glassTexture.IsValid)
        {
            _device.DestroyTexture(_glassTexture);
            _glassTexture = TextureHandle.None;
        }

        ReleaseRetailExplosionModel();
        ReleaseRetailFireballModel();
    }

    private void DrawRetailExplosions(
        in Camera3D camera,
        in Matrix4f projection,
        float alpha)
    {
        if (_retailExplosions.Count == 0 || _retailExplosionGeometry is null || !_retailExplosionTexture.IsValid)
        {
            return;
        }

        try
        {
            if (!_retailExplosionMesh.IsValid)
            {
                _retailExplosionMesh = _device.CreateMesh(
                    _retailExplosionGeometry.Vertices,
                    _retailExplosionGeometry.Indices);
            }

            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(DepthState.TestOnly);
            _device.SetMaterialOverride(GlowMaterial);

            // Explosion0 keeps the authored model's orientation and passes the fireball field to the
            // child as 4096 * 0.000219726557. The renderer's geometry is already in metres, so only
            // that native child scale remains to be applied here. Its source model is static; the
            // native FireBall task owns the sprite animation separately, while this is the authored
            // 1009_01_1 child itself.
            _ = alpha;
            foreach (RetailExplosion explosion in _retailExplosions)
            {
                Vector3d positionMetres = explosion.PositionUnits / WorldScene.UnitsPerMetre;
                Matrix4f rigid = camera.CreateModelView(
                    positionMetres,
                    Matrix3f.FromEngineEulerAngles(
                        explosion.RotationAngles.X,
                        explosion.RotationAngles.Y,
                        explosion.RotationAngles.Z));
                Matrix4f modelView = ScaleModelView(rigid, explosion.Scale);

                foreach (MeshSubset subset in _retailExplosionGeometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    _device.DrawMesh(
                        _retailExplosionMesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        _retailExplosionTexture,
                        TextureHandle.None,
                        BlendMode.AlphaBlend);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // The effect is optional presentation. A backend upload failure must not stop gameplay.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    private void DrawRetailFireballs(
        in Camera3D camera,
        in Matrix4f projection,
        float alpha)
    {
        if (_retailFireballs.Count == 0 || _retailFireballGeometry is null || !_retailFireballTexture.IsValid)
        {
            return;
        }

        try
        {
            if (!_retailFireballMesh.IsValid)
            {
                _retailFireballMesh = _device.CreateMesh(
                    _retailFireballGeometry.Vertices,
                    _retailFireballGeometry.Indices);
            }

            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(DepthState.TestOnly);
            _device.SetMaterialOverride(GlowMaterial);
            _ = alpha;

            foreach (RetailFireball fireball in _retailFireballs)
            {
                Vector3d positionMetres = fireball.PositionUnits / WorldScene.UnitsPerMetre;
                Matrix4f rigid = camera.CreateModelView(
                    positionMetres,
                    Matrix3f.FromEngineEulerAngles(
                        fireball.RotationAngles.X,
                        fireball.RotationAngles.Y,
                        fireball.RotationAngles.Z));
                Matrix4f modelView = ScaleModelView(rigid, fireball.Scale);

                foreach (MeshSubset subset in _retailFireballGeometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    _device.DrawMesh(
                        _retailFireballMesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        _retailFireballTexture,
                        TextureHandle.None,
                        BlendMode.AlphaBlend);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    private static Matrix4f ScaleModelView(in Matrix4f rigid, float scale) => new(
        rigid.M11 * scale, rigid.M12 * scale, rigid.M13 * scale, rigid.M14,
        rigid.M21 * scale, rigid.M22 * scale, rigid.M23 * scale, rigid.M24,
        rigid.M31 * scale, rigid.M32 * scale, rigid.M33 * scale, rigid.M34,
        rigid.M41, rigid.M42, rigid.M43, rigid.M44);

    private TextureHandle LoadRetailTexture(string name)
    {
        if (_resources is null || !_resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes))
        {
            return TextureHandle.None;
        }

        try
        {
            TextureImage image = TexFile.Load(bytes.Span);
            return _device.CreateTexture(
                new TextureDescription(
                    image.Width,
                    image.Height,
                    image.MipLevels,
                    TextureFilter.Linear,
                    TextureAddressMode.Clamp),
                image.Pixels);
        }
        catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
            return TextureHandle.None;
        }
    }

    private void ReleaseRetailExplosionModel()
    {
        if (_retailExplosionMesh.IsValid)
        {
            _device.DestroyMesh(_retailExplosionMesh);
            _retailExplosionMesh = MeshHandle.None;
        }

        if (_retailExplosionTexture.IsValid)
        {
            _device.DestroyTexture(_retailExplosionTexture);
            _retailExplosionTexture = TextureHandle.None;
        }

        _retailExplosionGeometry = null;
    }

    private void ReleaseRetailFireballModel()
    {
        if (_retailFireballMesh.IsValid)
        {
            _device.DestroyMesh(_retailFireballMesh);
            _retailFireballMesh = MeshHandle.None;
        }

        if (_retailFireballTexture.IsValid)
        {
            _device.DestroyTexture(_retailFireballTexture);
            _retailFireballTexture = TextureHandle.None;
        }

        _retailFireballGeometry = null;
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

        Emit(new Particle
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
        Particle particle = new()
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

        Emit(new Particle
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

    private void AddGlassShardQuad(ref Particle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d centreUnits = PresentationBlend.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d a = ((centreUnits + p.GlassA) / WorldScene.UnitsPerMetre) - eye;
        Vector3d b = ((centreUnits + p.GlassB) / WorldScene.UnitsPerMetre) - eye;
        Vector3d c = ((centreUnits + p.GlassC) / WorldScene.UnitsPerMetre) - eye;
        Vector3f normal = new((float)p.GlassNormal.X, (float)p.GlassNormal.Y, (float)p.GlassNormal.Z);
        float opacity = MathF.Min(0.9f, life * 0.9f)
            * NearFade(Vector3d.Distance(centreUnits / WorldScene.UnitsPerMetre, eye));
        Bgra32 colour = Tint(0.72f * opacity, 0.92f * opacity, opacity, opacity);

        Vector3f af = new((float)a.X, (float)a.Y, (float)a.Z);
        Vector3f bf = new((float)b.X, (float)b.Y, (float)b.Z);
        Vector3f cf = new((float)c.X, (float)c.Y, (float)c.Z);
        if (!IsFinite(af) || !IsFinite(bf) || !IsFinite(cf) || !IsFinite(normal))
        {
            return;
        }

        _vertices[_used] = new Vertex3D(af, normal, 0f, 0f, colour: colour);
        _vertices[_used + 1] = new Vertex3D(bf, normal, 1f, 0f, colour: colour);
        _vertices[_used + 2] = new Vertex3D(cf, normal, 0.5f, 1f, colour: colour);
        _vertices[_used + 3] = new Vertex3D(af, normal, 0f, 0f, colour: colour);
        _used += 4;
    }

    private void Emit(in Particle particle)
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

    private static Vector3f Normalize(Vector3f value)
    {
        float length = value.Length;
        return !(length > 1e-6f) ? new Vector3f(0f, 0f, 1f) : value / length;
    }

    private void AddDustQuad(ref Particle p, Vector3d eye, float alpha)
    {
        // Thins as it ages and as it grows -- a plume spreads its matter, it does not mint more.
        // The 0.55 is the third tuning of this number: 0.5 read as a hint, 0.7 as a smoke grenade.
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        float opacity = life * life * 0.55f;
        AddBillboard(ref p, eye, alpha, p.Size, opacity);
    }

    private void AddRetailSpriteQuad(ref Particle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        float strength = p.IsRetailSmoke ? life * 0.75f : life;
        AddBillboard(
            ref p,
            eye,
            alpha,
            p.Size,
            strength,
            p.RetailU0,
            p.RetailV0,
            p.RetailU1,
            p.RetailV1);
    }

    private void AddSparkQuad(ref Particle p, Vector3d eye, float alpha)
    {
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);

        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d head = PresentationBlend.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d headMetres = (head / WorldScene.UnitsPerMetre) - eye;

        double distance = headMetres.Length;
        float nearFade = NearFade(distance);
        if (!(nearFade > 0f))
        {
            return;
        }

        // Stretched along a third of a tick's travel, exactly the tracer's streak trick: a fast
        // spark reads as a line of light, a dying one collapses to a point.
        Vector3d tailMetres = new(
            headMetres.X - (p.Velocity.X * 0.35 / UnitsPerMetre),
            headMetres.Y - (p.Velocity.Y * 0.35 / UnitsPerMetre),
            headMetres.Z - (p.Velocity.Z * 0.35 / UnitsPerMetre));

        Vector3d along = tailMetres - headMetres;
        Vector3d acrossD = new(
            (along.Y * headMetres.Z) - (along.Z * headMetres.Y),
            (along.Z * headMetres.X) - (along.X * headMetres.Z),
            (along.X * headMetres.Y) - (along.Y * headMetres.X));
        double acrossLength = acrossD.Length;

        float strength = life * nearFade;
        Bgra32 headColour = Tint(p.Red * strength, p.Green * strength, p.Blue * strength, strength);
        Bgra32 tailColour = Tint(
            p.Red * strength * 0.4f, p.Green * strength * 0.4f, p.Blue * strength * 0.4f, strength);

        if (!(acrossLength > 1e-9))
        {
            // Head-on: the streak has no width axis, so it is a dot this frame.
            AddBillboard(ref p, eye, alpha, p.Size * 1.5f, strength);
            return;
        }

        acrossD /= acrossLength;
        float half = (p.Size * 0.5f) + ((float)distance * SizeBoostPerMetre);
        Vector3f across = new(
            (float)(acrossD.X * half), (float)(acrossD.Y * half), (float)(acrossD.Z * half));
        Vector3f headF = new((float)headMetres.X, (float)headMetres.Y, (float)headMetres.Z);
        Vector3f tailF = new((float)tailMetres.X, (float)tailMetres.Y, (float)tailMetres.Z);
        Vector3f normal = new(
            (float)(-headMetres.X / distance), (float)(-headMetres.Y / distance), (float)(-headMetres.Z / distance));

        if (!IsFinite(headF) || !IsFinite(tailF) || !IsFinite(across) || !IsFinite(normal))
        {
            return;
        }

        _vertices[_used] = new Vertex3D(headF + across, normal, 0.5f, 0f, colour: headColour);
        _vertices[_used + 1] = new Vertex3D(headF - across, normal, 0.5f, 1f, colour: headColour);
        _vertices[_used + 2] = new Vertex3D(tailF - across, normal, 1f, 1f, colour: tailColour);
        _vertices[_used + 3] = new Vertex3D(tailF + across, normal, 1f, 0f, colour: tailColour);
        _used += 4;
    }

    private void AddBillboard(
        ref Particle p,
        Vector3d eye,
        float alpha,
        float sizeMetres,
        float strength,
        float u0 = 0f,
        float v0 = 0f,
        float u1 = 1f,
        float v1 = 1f)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d position = PresentationBlend.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d centre = (position / WorldScene.UnitsPerMetre) - eye;
        double distance = centre.Length;
        float nearFade = NearFade(distance);
        if (!(distance > 1e-6) || !(nearFade > 0f))
        {
            return;
        }

        strength *= nearFade;

        Vector3d toEye = centre / -distance;
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = new(
            (reference.Y * toEye.Z) - (reference.Z * toEye.Y),
            (reference.Z * toEye.X) - (reference.X * toEye.Z),
            (reference.X * toEye.Y) - (reference.Y * toEye.X));
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return;
        }

        rightD /= rightLength;
        Vector3d upD = new(
            (toEye.Y * rightD.Z) - (toEye.Z * rightD.Y),
            (toEye.Z * rightD.X) - (toEye.X * rightD.Z),
            (toEye.X * rightD.Y) - (toEye.Y * rightD.X));

        float half = (sizeMetres * 0.5f) + ((float)distance * SizeBoostPerMetre);
        Vector3f right = new((float)(rightD.X * half), (float)(rightD.Y * half), (float)(rightD.Z * half));
        Vector3f up = new((float)(upD.X * half), (float)(upD.Y * half), (float)(upD.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        if (!IsFinite(centreF) || !IsFinite(right) || !IsFinite(up) || !IsFinite(normal))
        {
            return;
        }

        Bgra32 colour = Tint(p.Red * strength, p.Green * strength, p.Blue * strength, strength);

        _vertices[_used] = new Vertex3D(centreF - right - up, normal, u0, v0, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, u1, v0, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, u1, v1, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, u0, v1, colour: colour);
        _used += 4;
    }

    private void SelectRetailSpriteFrame(ref Particle particle, bool smoke, int frameIndex)
    {
        RetailSpriteFrame[] frames = smoke ? _dustFrames : _sparkFrames;
        if (frames.Length == 0)
        {
            particle.RetailU0 = 0f;
            particle.RetailV0 = 0f;
            particle.RetailU1 = 1f;
            particle.RetailV1 = 1f;
            return;
        }

        RetailSpriteFrame frame = frames[Math.Clamp(frameIndex, 0, frames.Length - 1)];
        particle.RetailU0 = frame.U0;
        particle.RetailV0 = frame.V0;
        particle.RetailU1 = frame.U1;
        particle.RetailV1 = frame.V1;
    }

    private static float NearFade(double distanceMetres) =>
        Math.Clamp(((float)distanceMetres - NearCullMetres) / NearFadeMetres, 0f, 1f);

    private static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static Bgra32 Tint(float r, float g, float b, float a)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

        // Alpha floored at one: Vertex3D reads an all-zero colour as "no tint" and substitutes
        // white, which a faded particle must never round to.
        return new Bgra32(Channel(b), Channel(g), Channel(r), Math.Max((byte)1, Channel(a)));
    }

    private static ushort[] BuildQuadIndices()
    {
        ushort[] indices = new ushort[MaximumParticles * 6];
        for (int quad = 0; quad < MaximumParticles; quad++)
        {
            int b = quad * 4;
            int i = quad * 6;
            indices[i] = (ushort)b;
            indices[i + 1] = (ushort)(b + 1);
            indices[i + 2] = (ushort)(b + 2);
            indices[i + 3] = (ushort)b;
            indices[i + 4] = (ushort)(b + 2);
            indices[i + 5] = (ushort)(b + 3);
        }

        return indices;
    }

    // The spark sprite: a hot radial glow. Profile split as its square root across colour and alpha
    // so the additive multiply restores it linearly and the rim's ALPHA is zero -- the shaped-alpha
    // rule the tracer's black-square hunt established.
    private TextureHandle GetSparkTexture()
    {
        if (_sparkTexture.IsValid)
        {
            return _sparkTexture;
        }

        _sparkTexture = BuildTexture((dx, dy, distance) =>
        {
            float glow = Math.Clamp(1f - distance, 0f, 1f);
            glow = MathF.Pow(glow, 1.4f);
            byte value = (byte)(MathF.Sqrt(glow) * 255f);
            return new Bgra32(value, value, value, value);
        });
        return _sparkTexture;
    }

    // The dust sprite: a soft blob with a little procedural mottle, alpha-shaped, colour solid so
    // the vertex tint decides the hue and the texture only the shape.
    private TextureHandle GetDustTexture()
    {
        if (_dustTexture.IsValid)
        {
            return _dustTexture;
        }

        _dustTexture = BuildTexture((dx, dy, distance) =>
        {
            float falloff = Math.Clamp(1f - distance, 0f, 1f);
            falloff *= falloff;

            // A cheap deterministic mottle so a plume is a cloud rather than a disc.
            float noise = 0.75f + (0.25f * MathF.Sin((dx * 12.9898f) + (dy * 78.233f))
                * MathF.Sin((dx * 39.4227f) - (dy * 27.1717f)));

            byte alpha = (byte)(Math.Clamp(falloff * noise, 0f, 1f) * 255f);
            return new Bgra32(255, 255, 255, alpha);
        });
        return _dustTexture;
    }

    private TextureHandle GetGlassTexture()
    {
        if (_glassTexture.IsValid)
        {
            return _glassTexture;
        }

        _glassTexture = BuildTexture((dx, dy, _) =>
        {
            // A triangular alpha mask reads as a shard even though the batch remains a quad mesh.
            // The broad translucent edge is the retail pane's light-catching fragment, not a round
            // smoke billboard accidentally tinted blue.
            float x = (dx + 1f) * 0.5f;
            float y = (dy + 1f) * 0.5f;
            float left = 0.08f + (0.84f * y);
            float right = 0.92f - (0.84f * y);
            float edge = MathF.Min(x - left, right - x);
            float alpha = Math.Clamp(edge * 80f, 0f, 1f) * Math.Clamp(1f - (y * 0.35f), 0f, 1f);
            return new Bgra32(255, 255, 255, (byte)(alpha * 220f));
        });
        return _glassTexture;
    }

    private TextureHandle BuildTexture(Func<float, float, float, Bgra32> texel)
    {
        Bgra32[] pixels = new Bgra32[TextureSize * TextureSize];
        float centre = (TextureSize - 1) * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                pixels[(y * TextureSize) + x] = texel(dx, dy, distance);
            }
        }

        return _device.CreateTexture(
            new TextureDescription(
                TextureSize,
                TextureSize,
                MipLevels: 1,
                TextureFilter.Linear,
                TextureAddressMode.Clamp),
            pixels);
    }

    private RetailSpriteAsset LoadRetailSprite(VirtualFileSystem fileSystem, string spriteName)
    {
        const string archiveName = "LOCAL:common/sprites/sprites.res";
        try
        {
            if (!fileSystem.Exists(archiveName))
            {
                return RetailSpriteAsset.Empty;
            }

            IresArchive archive = IresArchive.Read(fileSystem.ReadAllBytes(archiveName));
            IresEntry? entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.Kind == IresEntryKind.Body
                && Path.GetFileName(candidate.Name).Equals(spriteName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return RetailSpriteAsset.Empty;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, spriteName);
            if (asset.Frames.Count == 0 || asset.Textures.Count == 0)
            {
                return RetailSpriteAsset.Empty;
            }

            List<(Bgra32[] Pixels, int Width, int Height)> crops = [];
            foreach (SpriteFrame frame in asset.Frames)
            {
                if (frame.IsEmpty || (uint)frame.TextureIndex >= (uint)asset.Textures.Count)
                {
                    continue;
                }

                TextureImage source = asset.GetTexture(frame);
                ReadOnlySpan<Bgra32> pixels = source.GetLevel(0);
                int left = Math.Clamp((int)MathF.Floor(frame.U0 * source.Width), 0, source.Width - 1);
                int top = Math.Clamp((int)MathF.Floor(frame.V0 * source.Height), 0, source.Height - 1);
                int right = Math.Clamp((int)MathF.Ceiling(frame.U1 * source.Width), left + 1, source.Width);
                int bottom = Math.Clamp((int)MathF.Ceiling(frame.V1 * source.Height), top + 1, source.Height);
                int width = right - left;
                int height = bottom - top;
                Bgra32[] cropped = new Bgra32[width * height];
                for (int y = 0; y < height; y++)
                {
                    pixels.Slice(((top + y) * source.Width) + left, width)
                        .CopyTo(cropped.AsSpan(y * width, width));
                }

                crops.Add((cropped, width, height));
            }

            if (crops.Count == 0)
            {
                return RetailSpriteAsset.Empty;
            }

            int atlasWidth = crops.Sum(crop => crop.Width);
            int atlasHeight = crops.Max(crop => crop.Height);
            Bgra32[] atlas = new Bgra32[checked(atlasWidth * atlasHeight)];
            RetailSpriteFrame[] frames = new RetailSpriteFrame[crops.Count];
            int offsetX = 0;
            for (int i = 0; i < crops.Count; i++)
            {
                (Bgra32[] pixels, int width, int height) = crops[i];
                for (int y = 0; y < height; y++)
                {
                    pixels.AsSpan(y * width, width)
                        .CopyTo(atlas.AsSpan((y * atlasWidth) + offsetX, width));
                }

                frames[i] = new RetailSpriteFrame(
                    (float)offsetX / atlasWidth,
                    0f,
                    (float)(offsetX + width) / atlasWidth,
                    (float)height / atlasHeight);
                offsetX += width;
            }

            TextureHandle texture = _device.CreateTexture(
                new TextureDescription(
                    atlasWidth,
                    atlasHeight,
                    MipLevels: 1,
                    TextureFilter.Linear,
                    TextureAddressMode.Clamp),
                atlas);
            return new RetailSpriteAsset(texture, frames);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or PackFormatException or GraphicsDeviceException)
        {
            _ = ex;
            return RetailSpriteAsset.Empty;
        }
    }

    private struct Particle
    {
        public Vector3d Position;
        public Vector3d PreviousPosition;
        public Vector3f Velocity;
        public int LifeTicks;
        public int AgeTicks;
        public float Size;
        public float Growth;
        public float GravityScale;
        public float Drag;
        public float Red;
        public float Green;
        public float Blue;
        public bool IsSpark;
        public bool IsGlassShard;
        public bool IsRetailSprite;
        public bool IsRetailSmoke;
        public float RetailU0;
        public float RetailV0;
        public float RetailU1;
        public float RetailV1;
        public Vector3d GlassA;
        public Vector3d GlassB;
        public Vector3d GlassC;
        public Vector3d GlassNormal;
    }

    private readonly record struct RetailExplosion(
        Vector3d PositionUnits,
        float Scale,
        int LifeTicks,
        Vector3f Velocity,
        Vector3f RotationAngles,
        int AgeTicks = 0);

    private readonly record struct RetailFireball(
        Vector3d PositionUnits,
        float Scale,
        int LifeTicks,
        Vector3f RotationAngles,
        int AgeTicks = 0);

    private readonly record struct RetailSmoke(
        Vector3d PositionUnits,
        float Scale,
        int TotalTicks,
        int SecondSpawnAge,
        int AgeTicks = 0,
        bool SecondEmitted = false);

    private readonly record struct GlassShard(
        Vector3d Centre,
        Vector3d A,
        Vector3d B,
        Vector3d C,
        Vector3d Normal,
        float SizeMetres,
        Vector3f Velocity,
        int LifeTicks);

    private readonly record struct RetailSpriteFrame(float U0, float V0, float U1, float V1);

    private readonly record struct RetailSpriteAsset(TextureHandle Texture, RetailSpriteFrame[] Frames)
    {
        public static RetailSpriteAsset Empty => new(TextureHandle.None, []);
    }
}

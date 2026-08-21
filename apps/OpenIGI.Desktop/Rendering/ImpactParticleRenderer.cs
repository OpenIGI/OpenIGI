using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
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
    private readonly IgiRandom _random = new();
    private readonly Particle[] _particles = new Particle[MaximumParticles];
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private int _count;
    private int _used;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _sparkTexture = TextureHandle.None;
    private TextureHandle _dustTexture = TextureHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImpactParticleRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ImpactParticleRenderer(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets how many particles are alive.</summary>
    public int LiveCount => _count;

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

    /// <summary>Spawns a grenade blast's fireball, sparks and smoke column.</summary>
    /// <param name="positionUnits">The blast's centre, in world units.</param>
    /// <param name="intensity">The particle-count scale; one is the authored look.</param>
    /// <remarks>
    /// A stand-in for the retail <c>1009_01_1</c> fireball model and its fragments, built from the
    /// same particle pieces the impacts use: a core of hot fast sparks thrown in every direction, a
    /// ring of embers that arc under gravity, and a slow dark column that climbs and thins. The
    /// upward bias is retail's own — the explosion task jitters its bursts −0.3 to +0.7 in Z.
    /// </remarks>
    public void SpawnExplosion(Vector3d positionUnits, float intensity)
        => SpawnExplosion(positionUnits, intensity, fragmentCount: 0, fireballCount: 0);

    /// <summary>Spawns an explosion using authored GenericTBA fragment and fireball counts.</summary>
    /// <param name="positionUnits">The blast's centre, in world units.</param>
    /// <param name="intensity">The particle-count scale.</param>
    /// <param name="fragmentCount">The authored debris count, or zero for the grenade fallback.</param>
    /// <param name="fireballCount">The authored fireball count, or zero for the grenade fallback.</param>
    public void SpawnExplosion(
        Vector3d positionUnits,
        float intensity,
        int fragmentCount,
        int fireballCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!(intensity > 0f))
        {
            return;
        }

        int embers = (int)((fireballCount > 0 ? fireballCount * 6 : 26) * Math.Min(intensity, 2f));
        int smoke = (int)((fragmentCount > 0 ? fragmentCount * 3 : 14) * Math.Min(intensity, 2f));
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

    /// <summary>Advances every particle by one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        }
    }

    /// <summary>Draws every particle: the dust haze first, the sparks' light over it.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
            if (p.IsSpark)
            {
                continue;
            }

            AddDustQuad(ref p, eye, alpha);
        }

        int dustCount = (_used / 4) - dustFirst;

        int sparkFirst = _used / 4;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.IsSpark)
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
    public void Clear() => _count = 0;

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

    private void AddDustQuad(ref Particle p, Vector3d eye, float alpha)
    {
        // Thins as it ages and as it grows -- a plume spreads its matter, it does not mint more.
        // The 0.55 is the third tuning of this number: 0.5 read as a hint, 0.7 as a smoke grenade.
        float life = 1f - ((float)p.AgeTicks / p.LifeTicks);
        float opacity = life * life * 0.55f;
        AddBillboard(ref p, eye, alpha, p.Size, opacity);
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

    private void AddBillboard(ref Particle p, Vector3d eye, float alpha, float sizeMetres, float strength)
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

        _vertices[_used] = new Vertex3D(centreF - right - up, normal, 0f, 0f, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, 1f, 0f, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, 1f, 1f, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, 0f, 1f, colour: colour);
        _used += 4;
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
    }
}

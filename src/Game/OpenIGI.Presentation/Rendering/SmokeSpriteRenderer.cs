using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Draws drifting puffs of the retail <c>smoke2.spr</c> — the rocket's exhaust trail and the grey column
/// an explosion leaves.
/// </summary>
/// <remarks>
/// <para>
/// <c>smoke2.spr</c> (<c>dword_5C1160</c>, registered in <c>sub_4801B0</c>) is the sprite the
/// <c>ExplosionSmoke</c> task and the missile's own trail (<c>physicsobj/missiles/cgm2/missile.qvm</c>)
/// draw. It is a 128×128 sprite with a soft transparent edge, so unlike the fireball it blends cleanly.
/// A puff is dropped where the rocket was and left behind to drift, expand and grey out.
/// </para>
/// <para>
/// Loaded from the global <c>common/sprites/sprites.res</c> the way <c>CameraStatusLightRenderer</c>
/// loads <c>alarmlight.spr</c>. Presentation only, on the fixed tick.
/// </para>
/// </remarks>
public sealed class SmokeSpriteRenderer : IDisposable
{
    /// <summary>The retail smoke sprite (<c>dword_5C1160</c> in <c>sub_4801B0</c>).</summary>
    public const string SpriteName = "smoke2.spr";

    /// <summary>The most live puffs; older ones are spared when a spawn would pass it.</summary>
    public const int MaximumPuffs = 512;

    /// <summary>Inside this distance from the eye a puff is not drawn — the retail 2048-unit near cut.</summary>
    public const float NearCullMetres = 2048f / (float)WorldScene.UnitsPerMetre;

    /// <summary>Over how many metres past the cut a puff fades up to full, to avoid a hard pop.</summary>
    public const float NearFadeMetres = 2f;

    /// <summary>Beyond this distance a puff is not drawn — the retail ~195 m far cut (800000 units).</summary>
    public const float FarCullMetres = 800000f / (float)WorldScene.UnitsPerMetre;

    private const int MaximumVertices = MaximumPuffs * 4;

    // Alpha-blended smoke is a lit surface's opposite — a self-shaded haze the forward shader must not
    // relight, so it is emissive like the other effect billboards.
    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly VirtualFileSystem _fileSystem;
    private readonly string _spriteArchive;
    private readonly IOpenIgiLogger _logger;
    private readonly IgiRandom _random = new();
    private readonly Puff[] _puffs = new Puff[MaximumPuffs];
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private SpriteResource? _sprite;
    private SpriteFrame _frame;
    private int _count;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private bool _loadTried;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SmokeSpriteRenderer"/> class.</summary>
    public SmokeSpriteRenderer(IGraphicsDevice device, VirtualFileSystem fileSystem, string spriteArchive, IOpenIgiLogger? logger = null)
    {
        _spriteArchive = spriteArchive ?? throw new ArgumentNullException(nameof(spriteArchive));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the real smoke sprite is available to draw with.</summary>
    public bool IsAvailable => EnsureLoaded();

    /// <summary>Gets how many puffs are alive.</summary>
    public int LiveCount => _count;

    /// <summary>Leaves one smoke puff in the air.</summary>
    /// <param name="positionUnits">Where the puff is dropped, in world units.</param>
    /// <param name="driftUnits">The puff's initial drift velocity, in world units per tick.</param>
    /// <param name="sizeMetres">The puff's starting diameter, in metres.</param>
    /// <param name="growthMetresPerTick">How fast the puff expands.</param>
    /// <param name="lifeTicks">How many ticks the puff lives.</param>
    /// <param name="darkness">Nought (pale) to one (dark) grey.</param>
    /// <returns><see langword="true"/> when the sprite is present and the puff was queued.</returns>
    public bool SpawnPuff(
        Vector3d positionUnits,
        Vector3f driftUnits,
        float sizeMetres,
        float growthMetresPerTick,
        int lifeTicks,
        float darkness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!EnsureLoaded() || _count >= MaximumPuffs)
        {
            return false;
        }

        _puffs[_count++] = new Puff
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            Velocity = driftUnits,
            LifeTicks = Math.Max(1, lifeTicks),
            AgeTicks = 0,
            SizeMetres = sizeMetres,
            GrowthMetresPerTick = growthMetresPerTick,
            Roll = (float)_random.NextDouble() * 6.2831855f,
            Shade = Math.Clamp(1f - (0.35f * darkness), 0.55f, 1f),
        };
        return true;
    }

    /// <summary>Advances every puff by one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = _count - 1; i >= 0; i--)
        {
            ref Puff p = ref _puffs[i];
            if (++p.AgeTicks >= p.LifeTicks)
            {
                p = _puffs[--_count];
                continue;
            }

            p.PreviousPosition = p.Position;
            p.Velocity = new Vector3f(p.Velocity.X * 0.92f, p.Velocity.Y * 0.92f, (p.Velocity.Z * 0.92f) + 8f);
            p.Position = new Vector3d(
                p.Position.X + p.Velocity.X,
                p.Position.Y + p.Velocity.Y,
                p.Position.Z + p.Velocity.Z);
            p.SizeMetres += p.GrowthMetresPerTick;
        }
    }

    /// <summary>Draws every live puff as an alpha-blended camera-facing billboard.</summary>
    public void Draw(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sprite is null || _count == 0 || _frame.IsEmpty)
        {
            return;
        }

        Vector3d eye = camera.Position;
        int used = 0;
        for (int i = 0; i < _count; i++)
        {
            used = AddCard(ref _puffs[i], eye, alpha, used);
        }

        if (used == 0)
        {
            return;
        }

        if (_lastUsed > used)
        {
            Array.Clear(_vertices, used, _lastUsed - used);
        }

        _lastUsed = used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, QuadIndices.Build(MaximumPuffs));
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);
            _device.ClearPoseInterpolation();
            _device.DrawMesh(
                _mesh,
                0,
                (used / 4) * 6,
                camera.CreateModelView(camera.Position, Matrix3f.Identity),
                _sprite.Textures[_frame.TextureIndex],
                TextureHandle.None,
                BlendMode.AlphaBlend);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <summary>Removes every puff, as changing level does.</summary>
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

        _sprite?.Dispose();
        _sprite = null;
    }

    private int AddCard(ref Puff p, Vector3d eye, float alpha, int used)
    {
        if (used + 4 > MaximumVertices)
        {
            return used;
        }

        // Fade in over the first fifth, then out over the rest, so a fresh puff is not a hard edge.
        // Peak opacity is kept low: retail's smoke is a thin translucent haze, not an opaque wall.
        float life = (float)p.AgeTicks / p.LifeTicks;
        float opacity = life < 0.2f ? life / 0.2f : 1f - ((life - 0.2f) / 0.8f);
        opacity = Math.Clamp(opacity, 0f, 1f) * 0.3f;
        if (!(opacity > 0f))
        {
            return used;
        }

        Vector3d position = Vector3d.Lerp(p.PreviousPosition, p.Position, alpha);
        Vector3d centre = (position / WorldScene.UnitsPerMetre) - eye;
        double distance = centre.Length;
        if (!(distance > 1e-6))
        {
            return used;
        }

        // The retail 2D particle render (0x004FD020) draws a particle only when its distance to the eye
        // is over 2048 units (half a metre) and under ~195 m. Skipping the near ones is what keeps the
        // player's own smoke out of their face — without it a blast at arm's length fills the screen. A
        // short fade above the cut avoids a hard pop as a puff crosses it.
        if (distance is <= NearCullMetres or >= FarCullMetres)
        {
            return used;
        }

        float nearFade = Math.Clamp(((float)distance - NearCullMetres) / NearFadeMetres, 0f, 1f);
        opacity *= nearFade;
        if (!(opacity > 0f))
        {
            return used;
        }

        float half = p.SizeMetres * 0.5f;

        Vector3d toEye = centre / -distance;
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = Vector3d.Cross(reference, toEye);
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return used;
        }

        rightD /= rightLength;
        Vector3d upD = Vector3d.Cross(toEye, rightD);

        float cos = MathF.Cos(p.Roll);
        float sin = MathF.Sin(p.Roll);
        Vector3d rr = new(
            (rightD.X * cos) + (upD.X * sin),
            (rightD.Y * cos) + (upD.Y * sin),
            (rightD.Z * cos) + (upD.Z * sin));
        Vector3d ru = new(
            (upD.X * cos) - (rightD.X * sin),
            (upD.Y * cos) - (rightD.Y * sin),
            (upD.Z * cos) - (rightD.Z * sin));

        Vector3f right = new((float)(rr.X * half), (float)(rr.Y * half), (float)(rr.Z * half));
        Vector3f up = new((float)(ru.X * half), (float)(ru.Y * half), (float)(ru.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        if (!FiniteChecks.IsFinite(centreF) || !FiniteChecks.IsFinite(right) || !FiniteChecks.IsFinite(up) || !FiniteChecks.IsFinite(normal))
        {
            return used;
        }

        float g = p.Shade;
        Bgra32 colour = new(
            (byte)Math.Clamp((int)(g * 255f), 0, 255),
            (byte)Math.Clamp((int)(g * 255f), 0, 255),
            (byte)Math.Clamp((int)(g * 255f), 0, 255),
            (byte)Math.Clamp((int)(opacity * 255f), 1, 255));

        _vertices[used] = new Vertex3D(centreF - right - up, normal, _frame.U0, _frame.V0, colour: colour);
        _vertices[used + 1] = new Vertex3D(centreF + right - up, normal, _frame.U1, _frame.V0, colour: colour);
        _vertices[used + 2] = new Vertex3D(centreF + right + up, normal, _frame.U1, _frame.V1, colour: colour);
        _vertices[used + 3] = new Vertex3D(centreF - right + up, normal, _frame.U0, _frame.V1, colour: colour);
        return used + 4;
    }

    private bool EnsureLoaded()
    {
        if (_loadTried)
        {
            return _sprite is not null;
        }

        _loadTried = true;

        try
        {
            if (!_fileSystem.Exists(_spriteArchive))
            {
                _logger.Warning("Smoke", $"No sprite archive at {_spriteArchive}; rockets fly without a trail.");
                return false;
            }

            IresArchive archive = IresArchive.Read(_fileSystem.ReadAllBytes(_spriteArchive));
            IresEntry? entry = archive.FindBody(SpriteName);
            if (entry is null)
            {
                _logger.Warning("Smoke", $"'{SpriteName}' is missing from {_spriteArchive}; rockets fly without a trail.");
                return false;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, SpriteName);
            if (asset.Frames.Count == 0 || asset.Textures.Count == 0)
            {
                _logger.Warning("Smoke", $"'{SpriteName}' has no drawable frame; rockets fly without a trail.");
                return false;
            }

            _frame = asset.Frames[0];
            _sprite = SpriteResource.Upload(_device, asset, TextureFilter.Linear);
            _logger.Information("Smoke", $"Loaded '{SpriteName}'.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TextureFormatException or GraphicsDeviceException)
        {
            _logger.Warning("Smoke", $"Could not load '{SpriteName}': {ex.Message}; rockets fly without a trail.");
            return false;
        }
    }

    private struct Puff
    {
        public Vector3d Position;
        public Vector3d PreviousPosition;
        public Vector3f Velocity;
        public int LifeTicks;
        public int AgeTicks;
        public float SizeMetres;
        public float GrowthMetresPerTick;
        public float Roll;
        public float Shade;
    }
}

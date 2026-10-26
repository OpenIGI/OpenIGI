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
/// The glow at a launcher rocket's nozzle: an additive card of <c>fireball.spr</c> laid every powered tick.
/// </summary>
/// <remarks>
/// The sprite lives in the global <c>common/sprites/sprites.res</c>, loaded the way <c>CameraStatusLightRenderer</c> loads
/// <c>alarmlight.spr</c>. A blast is not drawn here: every explosion is the retail <c>Explosion0</c>, drawn by
/// <see cref="RetailExplosionEffects"/>.
/// </remarks>
public sealed class ExplosionEffectRenderer : IDisposable
{
    /// <summary>The retail fireball sprite (<c>dword_5C1158</c> in <c>sub_4801B0</c>).</summary>
    public const string SpriteName = "fireball.spr";

    /// <summary>The most live fireball cards; older ones are spared when a spawn would pass it.</summary>
    public const int MaximumPuffs = 256;

    /// <summary>Inside this distance from the eye a card is not drawn — the retail 2048-unit near cut.</summary>
    public const float NearCullMetres = 2048f / (float)WorldScene.UnitsPerMetre;

    /// <summary>Over how many metres past the cut a card fades up to full, to avoid a hard pop.</summary>
    public const float NearFadeMetres = 2f;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;
    private const int MaximumVertices = MaximumPuffs * 4;

    // Fully emissive so the forward shader treats the fireball as light, not a lit surface.
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
    private SpriteFrame[] _frames = [];
    private int _count;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private bool _loadTried;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ExplosionEffectRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="fileSystem">The virtual file system the common sprite archive is mounted in.</param>
    /// <param name="spriteArchive">The archive in <paramref name="fileSystem"/> the sprite ships in.</param>
    /// <param name="logger">The logger for load diagnostics.</param>
    public ExplosionEffectRenderer(IGraphicsDevice device, VirtualFileSystem fileSystem, string spriteArchive, IOpenIgiLogger? logger = null)
    {
        _spriteArchive = spriteArchive ?? throw new ArgumentNullException(nameof(spriteArchive));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the real fireball sprite is available to draw with.</summary>
    public bool IsAvailable => EnsureLoaded();

    /// <summary>Gets how many fireball cards are alive.</summary>
    public int LiveCount => _count;

    /// <summary>Ages every live glow by one game tick.</summary>
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

            // The exhaust flare stays where it was laid — the rocket has flown on.
            p.PreviousPosition = p.Position;
        }
    }

    /// <summary>Draws every live fireball, its current sprite frame billboarded and blended additively.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sprite is null || _count == 0 || _frames.Length == 0)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);
        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);
        Vector3d eye = camera.Position;

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, QuadIndices.Build(MaximumPuffs));
            }

            // Each fireball may be on a different animation frame — a distinct texture — so draw one
            // batch per frame in use rather than one for the whole pool.
            for (int frameIndex = 0; frameIndex < _frames.Length; frameIndex++)
            {
                SpriteFrame frame = _frames[frameIndex];
                if (frame.IsEmpty || frame.U0 == frame.U1 || frame.V0 == frame.V1)
                {
                    continue;
                }

                int used = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref Puff p = ref _puffs[i];
                    if (FrameOf(ref p) != frameIndex)
                    {
                        continue;
                    }

                    used = AddCard(ref p, frame, eye, alpha, used);
                }

                if (used == 0)
                {
                    continue;
                }

                if (_lastUsed > used)
                {
                    Array.Clear(_vertices, used, _lastUsed - used);
                }

                _lastUsed = used;
                _device.UpdateAnimatedMesh(_mesh, _vertices);
                _device.ClearPoseInterpolation();
                _device.DrawMesh(
                    _mesh,
                    0,
                    (used / 4) * 6,
                    modelView,
                    _sprite.Textures[frame.TextureIndex],
                    TextureHandle.None,
                    BlendMode.Additive);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <summary>Removes every card, as changing level does.</summary>
    public void Clear()
    {
        _count = 0;
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

        _sprite?.Dispose();
        _sprite = null;
    }

    private int FrameOf(ref Puff p)
    {
        // The FireBall tick's frame progression: play the sprite's frames across the puff's life.
        int frame = (int)((float)p.AgeTicks / p.LifeTicks * _frames.Length);
        return Math.Clamp(frame, 0, _frames.Length - 1);
    }

    /// <summary>Emits one tick of the rocket's exhaust glow at its nozzle.</summary>
    /// <param name="positionUnits">The nozzle position, in world units.</param>
    /// <param name="sizeMetres">The glow's diameter, in metres.</param>
    /// <remarks>
    /// The retail rocket's engine light (<c>0x004CABF0</c>) bakes onto nearby vertices through the world
    /// octree — a lighting path this clean-room renderer has no equivalent for (I.G.I. declares no point
    /// lights). This is the visible half: the bright flare at the tube's mouth, an additive spot of the
    /// <c>fireball.spr</c> laid every powered tick so the exhaust reads as steady.
    /// </remarks>
    public void SpawnEngineGlow(Vector3d positionUnits, float sizeMetres)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!EnsureLoaded() || _count >= MaximumPuffs)
        {
            return;
        }

        _puffs[_count++] = new Puff
        {
            Position = positionUnits,
            PreviousPosition = positionUnits,
            LifeTicks = 4,
            AgeTicks = 0,
            SizeMetres = sizeMetres,
            Roll = (float)_random.NextDouble() * 6.2831855f,
        };
    }

    private int AddCard(ref Puff p, in SpriteFrame frame, Vector3d eye, float alpha, int used)
    {
        if (used + 4 > MaximumVertices)
        {
            return used;
        }

        // A glow stays hot and eases off at the end; the additive multiply restores the frame linearly.
        float life = (float)p.AgeTicks / p.LifeTicks;
        float brightness = 1f - (life * life);
        brightness = Math.Clamp(brightness, 0f, 1f);
        if (!(brightness > 0f))
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

        // The retail 2D particle render (0x004FD020) skips a particle under 2048 units (half a metre)
        // from the eye, which keeps a point-blank blast off the whole screen. Fade in briefly above it.
        if (distance <= NearCullMetres)
        {
            return used;
        }

        brightness *= Math.Clamp(((float)distance - NearCullMetres) / NearFadeMetres, 0f, 1f);
        if (!(brightness > 0f))
        {
            return used;
        }

        float sizeMetres = p.SizeMetres;

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
        float half = sizeMetres * 0.5f;
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

        // The glow burns hot: warm, not the sprite's own grey.
        Bgra32 colour = Tint(brightness, brightness * 0.78f, brightness * 0.45f, brightness);

        // fireball.spr carries a one-pixel opaque dark frame; sampled full, additive blending sums it
        // into a faint rectangle around every card, and linear filtering at the clamped edge widens it.
        // Inset the sampled rectangle past that border so only the fire disc is drawn.
        float insetU = 1.5f / MathF.Max(1f, frame.Width);
        float insetV = 1.5f / MathF.Max(1f, frame.Height);
        float u0 = frame.U0 + ((frame.U1 - frame.U0) * insetU);
        float u1 = frame.U1 - ((frame.U1 - frame.U0) * insetU);
        float v0 = frame.V0 + ((frame.V1 - frame.V0) * insetV);
        float v1 = frame.V1 - ((frame.V1 - frame.V0) * insetV);

        _vertices[used] = new Vertex3D(centreF - right - up, normal, u0, v0, colour: colour);
        _vertices[used + 1] = new Vertex3D(centreF + right - up, normal, u1, v0, colour: colour);
        _vertices[used + 2] = new Vertex3D(centreF + right + up, normal, u1, v1, colour: colour);
        _vertices[used + 3] = new Vertex3D(centreF - right + up, normal, u0, v1, colour: colour);
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
                _logger.Warning("Explosion", $"No sprite archive at {_spriteArchive}; using the procedural burst.");
                return false;
            }

            IresArchive archive = _fileSystem.ReadArchive(_spriteArchive);
            IresEntry? entry = archive.FindBody(SpriteName);
            if (entry is null)
            {
                _logger.Warning("Explosion", $"'{SpriteName}' is missing from {_spriteArchive}; using the procedural burst.");
                return false;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, SpriteName);
            if (asset.Frames.Count == 0 || asset.Textures.Count == 0)
            {
                _logger.Warning("Explosion", $"'{SpriteName}' has no drawable frame; using the procedural burst.");
                return false;
            }

            _sprite = SpriteResource.Upload(_device, asset, TextureFilter.Linear);
            _frames = [.. asset.Frames];
            _logger.Information("Explosion", $"Loaded '{SpriteName}' with {_frames.Length} frame(s).");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TextureFormatException or GraphicsDeviceException)
        {
            _logger.Warning("Explosion", $"Could not load '{SpriteName}': {ex.Message}; using the procedural burst.");
            return false;
        }
    }

    private static Bgra32 Tint(float r, float g, float b, float a)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);
        return new Bgra32(Channel(b), Channel(g), Channel(r), Math.Max((byte)1, Channel(a)));
    }

    private struct Puff
    {
        public Vector3d Position;
        public Vector3d PreviousPosition;
        public int LifeTicks;
        public int AgeTicks;
        public float SizeMetres;
        public float Roll;
    }
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Plays the retail explosion: staggered bursts of the animated <c>fireball.spr</c>, each cycling the
/// sprite's frames over its life the way the <c>FireBall</c> task does.
/// </summary>
/// <remarks>
/// <para>
/// The effect system is registered in <c>sub_4801B0</c>: an <c>Explosion0</c> controller fires
/// <c>N</c> bursts with random delays (the missile passes <b>10</b> bursts / delay <b>5</b> at
/// <c>0x00428390</c>), each spawning a <c>FireBall</c> that draws <b>fireball.spr</b> (<c>dword_5C1158</c>).
/// The <c>FireBall</c> tick (<c>0x004806E0</c>) steps the sprite frame by
/// <c>(start − remaining) / total · frameCount % frameCount</c> and destroys itself at the end — an
/// animated sprite, not a model. (<c>1009_01_1</c> is the model the generic <c>Explosion0</c> also
/// carries, and <c>explo_03_l</c> is the large-explosion <i>sound</i>, not a texture.)
/// </para>
/// <para>
/// The sprite lives in the global <c>common/sprites/sprites.res</c>, loaded the same way
/// <c>CameraStatusLightRenderer</c> loads <c>alarmlight.spr</c>. Without it the caller falls back
/// to the procedural burst so a blast is never invisible.
/// </para>
/// </remarks>
public sealed class ExplosionEffectRenderer : IDisposable
{
    /// <summary>The retail fireball sprite (<c>dword_5C1158</c> in <c>sub_4801B0</c>).</summary>
    public const string SpriteName = "fireball.spr";

    /// <summary>Bursts one explosion fires, from <c>a8</c> at <c>0x00428390</c>.</summary>
    public const int BurstCount = 10;

    /// <summary>The largest random delay between bursts, in ticks, from <c>a9</c>.</summary>
    public const int BurstDelayTicks = 5;

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
    private readonly List<Pending> _pending = [];
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

    /// <summary>Schedules one blast's bursts of the fireball sprite.</summary>
    /// <param name="positionUnits">The blast centre, in world units.</param>
    /// <param name="intensity">The size and count scale; one is the grenade/missile fireball.</param>
    /// <returns>
    /// <see langword="true"/> when the sprite is present and the blast was scheduled;
    /// <see langword="false"/> when the caller should fall back to the procedural burst.
    /// </returns>
    public bool Spawn(Vector3d positionUnits, float intensity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!(intensity > 0f) || !EnsureLoaded())
        {
            return false;
        }

        // A grenade/missile fireball spans a few metres; 0.6 shrinks it for a shot-out light.
        float coreMetres = 2.2f + (2.6f * Math.Min(intensity, 2f));
        int bursts = Math.Max(3, (int)(BurstCount * Math.Min(intensity, 1.5f)));

        for (int i = 0; i < bursts; i++)
        {
            // (rand−0.5, rand−0.5, rand−0.3): the up-biased scatter each burst is jittered by.
            Vector3f jitter = new(
                ((float)_random.NextDouble() - 0.5f) * coreMetres * UnitsPerMetre * 0.6f,
                ((float)_random.NextDouble() - 0.5f) * coreMetres * UnitsPerMetre * 0.6f,
                ((float)_random.NextDouble() - 0.3f) * coreMetres * UnitsPerMetre * 0.6f);

            _pending.Add(new Pending
            {
                Position = new Vector3d(
                    positionUnits.X + jitter.X,
                    positionUnits.Y + jitter.Y,
                    positionUnits.Z + jitter.Z),
                DelayTicks = (int)_random.NextBounded(BurstDelayTicks + 1),
                SizeMetres = coreMetres * (0.55f + ((float)_random.NextDouble() * 0.6f)),
            });
        }

        return true;
    }

    /// <summary>Advances the scheduled bursts and every live fireball by one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending pending = _pending[i];
            if (--pending.DelayTicks > 0)
            {
                _pending[i] = pending;
                continue;
            }

            EmitPuff(pending);
            _pending.RemoveAt(i);
        }

        for (int i = _count - 1; i >= 0; i--)
        {
            ref Puff p = ref _puffs[i];
            if (++p.AgeTicks >= p.LifeTicks)
            {
                p = _puffs[--_count];
                continue;
            }

            p.PreviousPosition = p.Position;
            if (p.Glow)
            {
                // The exhaust flare stays where it was laid — the rocket has flown on.
                continue;
            }

            p.Velocity = new Vector3f(p.Velocity.X * 0.9f, p.Velocity.Y * 0.9f, (p.Velocity.Z * 0.9f) + 24f);
            p.Position = new Vector3d(
                p.Position.X + p.Velocity.X,
                p.Position.Y + p.Velocity.Y,
                p.Position.Z + p.Velocity.Z);
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

    /// <summary>Removes every card and pending burst, as changing level does.</summary>
    public void Clear()
    {
        _count = 0;
        _pending.Clear();
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
    /// same <c>fireball.spr</c> the blast uses, laid every powered tick so the exhaust reads as steady.
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
            Velocity = default,
            LifeTicks = 4,
            AgeTicks = 0,
            SizeMetres = sizeMetres,
            Roll = (float)_random.NextDouble() * 6.2831855f,
            Glow = true,
        };
    }

    private void EmitPuff(in Pending pending)
    {
        if (_count >= MaximumPuffs)
        {
            return;
        }

        _puffs[_count++] = new Puff
        {
            Position = pending.Position,
            PreviousPosition = pending.Position,
            Velocity = new Vector3f(
                ((float)_random.NextDouble() - 0.5f) * 220f,
                ((float)_random.NextDouble() - 0.5f) * 220f,
                ((float)_random.NextDouble() * 180f) + 30f),
            LifeTicks = Math.Max(_frames.Length, 14 + (int)_random.NextBounded(10)),
            AgeTicks = 0,
            SizeMetres = pending.SizeMetres,
            Roll = ((float)_random.NextDouble() - 0.5f) * 1.2f,
        };
    }

    private int AddCard(ref Puff p, in SpriteFrame frame, Vector3d eye, float alpha, int used)
    {
        if (used + 4 > MaximumVertices)
        {
            return used;
        }

        // A burst has a brief bright core then a long fade; a glow stays hot and eases off at the end.
        // The additive multiply restores the frame linearly either way.
        float life = (float)p.AgeTicks / p.LifeTicks;
        float brightness = p.Glow
            ? 1f - (life * life)
            : (life < 0.2f ? 0.4f + (life / 0.2f * 0.6f) : 1f - ((life - 0.2f) / 0.8f));
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

        // A burst swells as it plays out, roughly doubling across its life; a glow holds its size.
        float sizeMetres = p.Glow ? p.SizeMetres : p.SizeMetres * (0.6f + (0.6f * life));

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

        if (!IsFinite(centreF) || !IsFinite(right) || !IsFinite(up) || !IsFinite(normal))
        {
            return used;
        }

        // The glow burns hotter and whiter than the smoky fireball tint.
        Bgra32 colour = p.Glow
            ? Tint(brightness, brightness * 0.78f, brightness * 0.45f, brightness)
            : Tint(brightness, brightness, brightness, brightness);

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

            IresArchive archive = IresArchive.Read(_fileSystem.ReadAllBytes(_spriteArchive));
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

    private static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static Bgra32 Tint(float r, float g, float b, float a)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);
        return new Bgra32(Channel(b), Channel(g), Channel(r), Math.Max((byte)1, Channel(a)));
    }

    private struct Pending
    {
        public Vector3d Position;
        public int DelayTicks;
        public float SizeMetres;
    }

    private struct Puff
    {
        public Vector3d Position;
        public Vector3d PreviousPosition;
        public Vector3f Velocity;
        public int LifeTicks;
        public int AgeTicks;
        public float SizeMetres;
        public float Roll;

        // A glow sits still at the rocket nozzle for a couple of ticks, hot and constant-sized, rather
        // than swelling and rising like a burst.
        public bool Glow;
    }
}

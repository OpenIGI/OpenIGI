using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Draws the 2-D particle engine both titles share (<see cref="Particle2DModule"/>): every live particle as its system's sprite,
/// as <c>Particle2DDynCubeObj_EnterHandler</c> (Project I.G.I. <c>0x4FD020</c>) draws it.
/// </summary>
/// <remarks>
/// <para>
/// A particle is a camera-facing quad, its half-extents the system's width and height, <b>times the particle's size</b> where
/// the system grows; turned by its rotation where it spins; of its frame where the sprite is animated. Nothing inside half a
/// metre of the eye, nor past the system's range, is drawn, and a quad's projected half is never under half a pixel.
/// </para>
/// <para>
/// The colour is the system's, on the sprite renderer's scale where 128 is white. A system that fades by opacity
/// (<see cref="Particle2DFlags.FadesByOpacity"/>) draws its particles at their intensity's alpha; one that does not darkens
/// their colour by it at full alpha. The blend is the graphic flags': additive, darkening and multiplying sprites as such,
/// everything else an ordinary alpha blend. A shiny system takes the world's light into its colour in the original; none of
/// either title's systems is shiny, so the system's own colour is drawn.
/// </para>
/// <para>
/// The sprites are the title's own, from the common sprite archive both titles ship. The quads are drawn emissive, over the
/// lit scene, divided by the scene's exposure as the first-person rig is -- without it a dark scene's adaptation clipped the
/// blood's red while it lifted the rest, and the spray came out pink. A particle is drawn between its last two ticks: the step
/// is a plain Euler one, so a tick ago it stood one speed back.
/// </para>
/// </remarks>
public sealed class Particle2DRenderer : IDisposable
{
    /// <summary>Inside this distance from the eye a particle is not drawn, in world units: half a metre.</summary>
    public const float NearCullUnits = 2048f;

    /// <summary>How many quads may be drawn in one frame.</summary>
    public const int MaximumQuads = 2048;

    /// <summary>The smallest a quad's projected half-extent may be, in pixels.</summary>
    public const float MinimumHalfPixels = 0.5f;

    /// <summary>The colour that is white on the sprite renderer's scale.</summary>
    public const float White = 128f;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;
    private const int MaximumVertices = MaximumQuads * 4;

    // Fully emissive, so the forward pass treats these quads as their own light rather than as surfaces to be lit.
    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly VirtualFileSystem? _fileSystem;
    private readonly string _archive;
    private readonly Dictionary<string, SpriteStrip> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private readonly List<Batch> _batches = [];
    private MeshHandle _mesh = MeshHandle.None;
    private int _used;
    private int _lastUsed;
    private float _exposure = 1f;
    private float _metrePixels;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="Particle2DRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="fileSystem">The title's data, which holds its sprites.</param>
    /// <param name="archive">The archive the sprites live in.</param>
    public Particle2DRenderer(IGraphicsDevice device, VirtualFileSystem? fileSystem, string archive = MissionLoader.CommonSpritesArchive)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _fileSystem = fileSystem;
        _archive = archive;
    }

    /// <summary>Gets how many quads the last draw laid.</summary>
    public int LastQuads { get; private set; }

    /// <summary>Gets a sprite's frames, loading it on first ask.</summary>
    /// <param name="sprite">The sprite's name.</param>
    /// <returns>The sprite, empty when it does not resolve.</returns>
    public SpriteStrip Sprite(string sprite)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sprites.TryGetValue(sprite, out SpriteStrip strip))
        {
            strip = _fileSystem is null ? SpriteStrip.Empty : SpriteStrip.Load(_device, _fileSystem, _archive, sprite);
            _sprites[sprite] = strip;
        }

        return strip;
    }

    /// <summary>Draws every live particle of the systems given.</summary>
    /// <param name="systems">The systems, in the order they are drawn.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last two ticks.</param>
    public void Draw(IReadOnlyList<Particle2DSystem> systems, in Camera3D camera, in Matrix4f projection, float blend)
    {
        ArgumentNullException.ThrowIfNull(systems);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _used = 0;
        _batches.Clear();
        _exposure = 1f / MathF.Max(_device.SceneExposure, 1e-3f);

        // Pixels per metre at one metre, for the half-pixel floor: the projection's vertical scale times half the viewport.
        _metrePixels = MathF.Abs(projection.M22) * _device.CurrentMode.Height * 0.5f;
        Vector3d eye = camera.Position;
        foreach (Particle2DSystem system in systems)
        {
            if (system.IsDeleted || system.Count == 0 || Sprite(system.Look.Sprite) is not { Texture.IsValid: true } strip)
            {
                continue;
            }

            int first = _used / 4;
            foreach (Particle2D particle in system.Particles)
            {
                AddQuad(system, strip, particle, eye, blend);
            }

            int count = (_used / 4) - first;
            if (count > 0)
            {
                _batches.Add(new Batch(first, count, strip.Texture, BlendOf(system.Look.Transparency)));
            }
        }

        LastQuads = _used / 4;
        if (_used == 0)
        {
            _lastUsed = 0;
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
                _mesh = _device.CreateAnimatedMesh(_vertices, QuadIndices.Build(MaximumQuads));
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);
            _device.ClearPoseInterpolation();
            foreach (Batch batch in _batches)
            {
                _device.DrawMesh(_mesh, batch.First * 6, batch.Count * 6, modelView, batch.Texture, TextureHandle.None, batch.Blend);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // Presentation only: a backend upload failure must not stop the game.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
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

        foreach (SpriteStrip strip in _sprites.Values)
        {
            if (strip.Texture.IsValid)
            {
                _device.DestroyTexture(strip.Texture);
            }
        }

        _sprites.Clear();
    }

    // The sprite renderer's blend for a system's graphic flags: additive by alpha, darkening, multiplying, or alpha.
    private static BlendMode BlendOf(int flags) =>
        (flags & Particle2DFlags.Additive) != 0 ? BlendMode.Additive
        : (flags & Particle2DFlags.Darkening) != 0 || flags < 0 ? BlendMode.Modulate
        : BlendMode.AlphaBlend;

    private void AddQuad(Particle2DSystem system, in SpriteStrip strip, Particle2D particle, Vector3d eye, float blend)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Particle2DLook look = system.Look;
        Vector3d previous = new(
            particle.Position.X - particle.Speed.X,
            particle.Position.Y - particle.Speed.Y,
            particle.Position.Z - particle.Speed.Z);
        Vector3d position = Vector3d.Lerp(previous, particle.Position, blend);
        Vector3d centre = (position / WorldScene.UnitsPerMetre) - eye;
        double distanceMetres = centre.Length;
        double distanceUnits = distanceMetres * WorldScene.UnitsPerMetre;
        if (distanceUnits <= NearCullUnits || distanceUnits >= look.Range)
        {
            return;
        }

        // The authored half-extents, by the particle's size only where the system grows.
        float scale = system.SizeDelta != 0f ? particle.Size : 1f;
        float halfX = look.Width * scale / UnitsPerMetre, halfY = look.Height * scale / UnitsPerMetre;
        if (!(halfX > 0f) || !(halfY > 0f))
        {
            return;
        }

        if (_metrePixels > 0f)
        {
            float minimum = MinimumHalfPixels * (float)distanceMetres / _metrePixels;
            halfX = MathF.Max(halfX, minimum);
            halfY = MathF.Max(halfY, minimum);
        }

        Vector3d toEye = centre / -distanceMetres;
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

        // Turned about its centre by its rotation, where it spins.
        double sin = 0.0, cos = 1.0;
        if (particle.RotationDelta != 0f)
        {
            sin = Math.Sin(particle.Rotation);
            cos = Math.Cos(particle.Rotation);
        }

        Vector3d rolledRight = new((rightD.X * cos) + (upD.X * sin), (rightD.Y * cos) + (upD.Y * sin), (rightD.Z * cos) + (upD.Z * sin));
        Vector3d rolledUp = new((upD.X * cos) - (rightD.X * sin), (upD.Y * cos) - (rightD.Y * sin), (upD.Z * cos) - (rightD.Z * sin));
        Vector3f right = new((float)(rolledRight.X * halfX), (float)(rolledRight.Y * halfX), (float)(rolledRight.Z * halfX));
        Vector3f up = new((float)(rolledUp.X * halfY), (float)(rolledUp.Y * halfY), (float)(rolledUp.Z * halfY));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);
        if (!IsFinite(centreF) || !IsFinite(right) || !IsFinite(up) || !IsFinite(normal))
        {
            return;
        }

        float intensity = Math.Clamp(particle.Intensity, 0f, 1f);
        bool byOpacity = (look.Transparency & Particle2DFlags.FadesByOpacity) != 0;
        float shade = (byOpacity ? 1f : intensity) * _exposure / White;
        Bgra32 colour = Bgra32.FromRgba(Channel(look.Color.X * shade), Channel(look.Color.Y * shade), Channel(look.Color.Z * shade), Channel(byOpacity ? intensity : 1f));
        SpriteStripFrame uv = strip.Frames.Length == 0
            ? SpriteStripFrame.Full
            : strip.Frames[look.IsAnimated ? Math.Clamp((int)particle.Frame, 0, strip.Frames.Length - 1) : 0];
        _vertices[_used] = new Vertex3D(centreF - right - up, normal, uv.U0, uv.V0, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, uv.U1, uv.V0, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, uv.U1, uv.V1, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, uv.U0, uv.V1, colour: colour);
        _used += 4;
    }

    private static byte Channel(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);

    private static bool IsFinite(Vector3f v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private readonly record struct Batch(int First, int Count, TextureHandle Texture, BlendMode Blend);
}

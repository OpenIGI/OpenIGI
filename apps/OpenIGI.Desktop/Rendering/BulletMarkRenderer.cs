using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws the marks rounds have left on the world.</summary>
/// <remarks>
/// <para>
/// One textured quad per mark, facing along the struck surface's normal and sized by the material.
/// Which texture and how big are <b>not decided here</b> — <see cref="BulletMarks"/> takes both out of
/// the material the collision face names, because in the original the surface decides what a hit on it
/// looks like and the bullet knows nothing about it.
/// </para>
/// <para>
/// <b>Divergence.</b> The original hands each mark to the struck <i>object</i>, which owns it and
/// draws it with its own geometry — so a mark on a moving door moves with the door. Here they are
/// world-space quads in one buffer, which is right for the static scenery every level is mostly made
/// of and wrong for anything that moves. Recorded in <c>TODO.md</c>.
/// </para>
/// </remarks>
public sealed class BulletMarkRenderer : IDisposable
{
    private const string LogCategory = "Decal";

    /// <summary>How far off the surface a mark is lifted, in metres.</summary>
    /// <remarks>
    /// The five millimetres the shot itself steps off a penetrated face, converted — enough to stop the
    /// quad fighting the wall for depth without floating visibly.
    /// </remarks>
    public const float DepthBiasInMetres = 20.48f / 4096f;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Vertex3D> _vertices = [];
    private readonly List<ushort> _indices = [];
    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="BulletMarkRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where the decal textures are read from.</param>
    /// <param name="logger">Where to report a texture that will not load.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public BulletMarkRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets how many quads the last <see cref="Draw"/> submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Draws every mark.</summary>
    /// <param name="marks">The marks to draw.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="marks"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Marks are grouped by texture so each distinct one costs a single draw however many holes are in
    /// the wall. They are drawn <b>alpha-blended and depth-tested but not depth-written</b>: a mark is a
    /// film on a surface, and writing depth would let one hide another that is a millimetre behind it.
    /// </remarks>
    public void Draw(BulletMarks marks, in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(marks);

        LastDrawCount = 0;
        if (marks.Marks.Count == 0)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);

        // CAMERA-RELATIVE, and it has to be. A level sits tens of kilometres from the world origin --
        // level 1's ground is at 42 km -- and a float there resolves to about five millimetres. A
        // three-centimetre decal built in absolute world space has its corners snapped to that grid and
        // visibly crawls as the camera moves. Subtracting the eye in DOUBLE before narrowing puts every
        // vertex within metres of zero, where the same float has micrometre resolution. Passing the
        // camera's own position as the model origin makes the offset exactly zero, so the matrix is a
        // pure rotation and the two halves agree.
        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        foreach (IGrouping<string, BulletMark> group in marks.Marks.GroupBy(m => m.Texture, StringComparer.OrdinalIgnoreCase))
        {
            TextureHandle texture = GetTexture(group.Key);
            if (!texture.IsValid)
            {
                continue;
            }

            Build(group, camera.Position);
            if (_indices.Count == 0)
            {
                continue;
            }

            try
            {
                if (_mesh.IsValid)
                {
                    _device.DestroyMesh(_mesh);
                }

                _mesh = _device.CreateMesh(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices));
                _device.DrawMesh(
                    _mesh,
                    0,
                    _indices.Count,
                    modelView,
                    texture,
                    TextureHandle.None,
                    BlendMode.AlphaBlend);

                LastDrawCount += _indices.Count / 6;
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Marks for '{group.Key}' did not upload: {ex.Message}");
                return;
            }
        }

        _device.SetDepthState(DepthState.TestAndWrite);
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

        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture.IsValid)
            {
                _device.DestroyTexture(texture);
            }
        }

        _textures.Clear();
    }

    private void Build(IEnumerable<BulletMark> marks, Vector3d eye)
    {
        _vertices.Clear();
        _indices.Clear();

        foreach (BulletMark mark in marks)
        {
            Vector3f normal = Normalise(mark.Normal);

            // Any two axes perpendicular to the normal will do -- a bullet hole has no up. Picking the
            // world axis the normal leans on least is what keeps the cross product well conditioned.
            Vector3f reference = MathF.Abs(normal.Z) < 0.9f
                ? new Vector3f(0f, 0f, 1f)
                : new Vector3f(1f, 0f, 0f);

            Vector3f right = Normalise(Cross(reference, normal));
            Vector3f up = Normalise(Cross(normal, right));

            float half = (float)(mark.SizeInUnits / WorldScene.UnitsPerMetre * 0.5);
            Vector3d centre = new(
                (mark.Position.X / WorldScene.UnitsPerMetre) + (normal.X * DepthBiasInMetres) - eye.X,
                (mark.Position.Y / WorldScene.UnitsPerMetre) + (normal.Y * DepthBiasInMetres) - eye.Y,
                (mark.Position.Z / WorldScene.UnitsPerMetre) + (normal.Z * DepthBiasInMetres) - eye.Z);

            // A mesh is one 16-bit index buffer, so a group stops at the last quad that fits.
            if (_vertices.Count + 4 > ushort.MaxValue)
            {
                return;
            }

            ushort b = (ushort)_vertices.Count;
            Add(centre, right, up, normal, -half, -half, 0f, 1f);
            Add(centre, right, up, normal, half, -half, 1f, 1f);
            Add(centre, right, up, normal, half, half, 1f, 0f);
            Add(centre, right, up, normal, -half, half, 0f, 0f);

            _indices.Add(b);
            _indices.Add((ushort)(b + 1));
            _indices.Add((ushort)(b + 2));
            _indices.Add(b);
            _indices.Add((ushort)(b + 2));
            _indices.Add((ushort)(b + 3));
        }
    }

    private void Add(
        Vector3d centre,
        Vector3f right,
        Vector3f up,
        Vector3f normal,
        float x,
        float y,
        float u,
        float v) =>
        _vertices.Add(new Vertex3D(
            new Vector3f(
                (float)centre.X + (right.X * x) + (up.X * y),
                (float)centre.Y + (right.Y * x) + (up.Y * y),
                (float)centre.Z + (right.Z * x) + (up.Z * y)),
            normal,
            u,
            v,
            colour: Bgra32.White));

    private static Vector3f Cross(Vector3f a, Vector3f b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    private static Vector3f Normalise(Vector3f v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return length <= 0f ? new Vector3f(0f, 0f, 1f) : new Vector3f(v.X / length, v.Y / length, v.Z / length);
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        foreach (string candidate in new[]
        {
            $"LOCAL:common/textures/{name}",
            LoadedPack.TextureResourceName(name),
        })
        {
            if (!_resources.TryLoad(candidate, out ReadOnlyMemory<byte> bytes))
            {
                continue;
            }

            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(
                        image.Width,
                        image.Height,
                        image.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Clamp),
                    image.Pixels);
                break;
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Decal texture '{name}' did not load: {ex.Message}");
            }
        }

        _textures[name] = handle;
        return handle;
    }
}

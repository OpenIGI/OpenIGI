using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Draws the marks rounds have left on the world.</summary>
/// <remarks>
/// <para>
/// One textured quad per mark, facing along the struck surface's normal and sized by the material.
/// Which texture and how big are <b>not decided here</b> — <see cref="BulletMarks"/> takes both out of
/// the material the collision face names, because in the original the surface decides what a hit on it
/// looks like and the bullet knows nothing about it.
/// </para>
/// <para>
/// The geometry streams through ONE fixed-capacity mesh whose vertices are replaced in place each
/// frame (<see cref="IGraphicsDevice.UpdateAnimatedMesh"/>), with each texture's marks drawn as an
/// index range of it. The first version destroyed and re-created a mesh per texture group per frame
/// and drew it in the same frame — the create-upload-draw churn that intermittently rasterises a
/// one-frame garbage quad on a threaded driver, which is exactly what the tracer renderer had to be
/// cured of. See <c>TracerTrailRenderer</c>.
/// </para>
/// <para>
/// The original makes each mark a child of the struck <i>object</i> (<c>0x47D380</c>), drawn against
/// its live transform — so a mark on a moving door moves with the door. Here a mark is held in the
/// frame of the placement it landed on and placed each frame by that placement's draw transform, the
/// same one the world renderer draws the door, elevator or cut-scene vehicle with.
/// </para>
/// </remarks>
internal sealed class BulletMarkRenderer : IBulletMarkPresenter
{
    private const string LogCategory = "Decal";

    /// <summary>How far off the surface a mark is lifted, in metres.</summary>
    /// <remarks>
    /// The five millimetres the shot itself steps off a penetrated face, converted — enough to stop the
    /// quad fighting the wall for depth without floating visibly.
    /// </remarks>
    public const float DepthBiasInMetres = 20.48f / 4096f;

    /// <summary>The most marks one frame draws — the store's own bound, so nothing is ever dropped.</summary>
    public const int MaximumMarks = BulletMarks.DefaultCapacity;

    private const int MaximumVertices = MaximumMarks * 4;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private readonly List<(TextureHandle Texture, int FirstQuad, int QuadCount)> _batches = [];

    // One frame's anchor poses. Hundreds of marks share a handful of objects, and resolving a pose walks
    // the door, elevator and cut-scene runtimes.
    private readonly Dictionary<WorldPlacement, (bool Known, Vector3d Position, Matrix3f Orientation)> _poses =
        new(ReferenceEqualityComparer.Instance);
    private readonly PlacementPose _cachedPose;
    private PlacementPose? _framePoses;
    private int _used;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="BulletMarkRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where the decal textures are read from.</param>
    /// <param name="logger">Where to report a texture that will not load.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public BulletMarkRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
        _cachedPose = CachedPose;
    }

    /// <summary>Gets how many quads the last <see cref="Draw"/> submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Draws every mark.</summary>
    /// <param name="marks">The marks to draw.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="poses">Where placements are drawn this frame, for marks held in their frames.</param>
    /// <exception cref="ArgumentNullException"><paramref name="marks"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Marks are grouped by texture so each distinct one costs a single draw however many holes are in
    /// the wall. They are drawn <b>alpha-blended and depth-tested but not depth-written</b>: a mark is a
    /// film on a surface, and writing depth would let one hide another that is a millimetre behind it.
    /// </remarks>
    public void Draw(BulletMarks marks, in Camera3D camera, in Matrix4f projection, PlacementPose? poses = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(marks);

        LastDrawCount = 0;
        if (marks.Marks.Count == 0)
        {
            return;
        }

        _framePoses = poses;
        _poses.Clear();

        // CAMERA-RELATIVE, and it has to be. A level sits tens of kilometres from the world origin --
        // level 1's ground is at 42 km -- and a float there resolves to about five millimetres. A
        // three-centimetre decal built in absolute world space has its corners snapped to that grid and
        // visibly crawls as the camera moves. Subtracting the eye in DOUBLE before narrowing puts every
        // vertex within metres of zero, where the same float has micrometre resolution. Passing the
        // camera's own position as the model origin makes the offset exactly zero, so the matrix is a
        // pure rotation and the two halves agree.
        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        _used = 0;
        _batches.Clear();

        foreach (IGrouping<string, BulletMark> group in marks.Marks.GroupBy(m => m.Texture, StringComparer.OrdinalIgnoreCase))
        {
            TextureHandle texture = GetTexture(group.Key);
            if (!texture.IsValid)
            {
                continue;
            }

            int firstQuad = _used / 4;
            foreach (BulletMark mark in group)
            {
                AddMark(mark, camera.Position);
            }

            int quadCount = (_used / 4) - firstQuad;
            if (quadCount > 0)
            {
                _batches.Add((texture, firstQuad, quadCount));
            }
        }

        if (_used == 0)
        {
            return;
        }

        // Whatever last frame wrote past this frame's count is degenerate-cleared, so the fixed index
        // range never rasterises a stale quad. All-zero vertices are zero-area triangles.
        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, BuildQuadIndices());
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);

            // The animated mesh carries a previous-pose stream a decal has no use for; a stale
            // soldier interpolation would drag every quad toward last frame's buffer.
            _device.ClearPoseInterpolation();

            foreach ((TextureHandle texture, int firstQuad, int quadCount) in _batches)
            {
                _device.DrawMesh(
                    _mesh,
                    firstQuad * 6,
                    quadCount * 6,
                    modelView,
                    texture,
                    TextureHandle.None,
                    BlendMode.AlphaBlend);

                LastDrawCount += quadCount;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"The marks did not upload: {ex.Message}");
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

    // The index pattern is static -- quad n is vertices 4n..4n+3 -- so it is built once, at the size
    // the mesh keeps for its whole life.
    private static ushort[] BuildQuadIndices()
    {
        ushort[] indices = new ushort[MaximumMarks * 6];
        for (int quad = 0; quad < MaximumMarks; quad++)
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

    private bool CachedPose(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        if (!_poses.TryGetValue(placement, out (bool Known, Vector3d Position, Matrix3f Orientation) pose))
        {
            bool known = _framePoses!(placement, out Vector3d position, out Matrix3f turned);
            pose = (known, position, turned);
            _poses[placement] = pose;
        }

        positionMetres = pose.Position;
        orientation = pose.Orientation;
        return pose.Known;
    }

    private void AddMark(in BulletMark mark, Vector3d eye)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        bool anchored = mark.TryResolve(
            _framePoses is null ? null : _cachedPose,
            out Vector3d position,
            out _,
            out Matrix3f orientation);

        // Any two axes perpendicular to the normal will do -- a bullet hole has no up. Picking the
        // axis the normal leans on least is what keeps the cross product well conditioned. An anchored
        // mark picks them in its object's frame and turns them with it, so a hole on a swinging door
        // turns with the door rather than spinning when the normal crosses the world-axis threshold.
        Vector3f normal = Normalise(anchored ? mark.LocalNormal : mark.Normal);
        Vector3f reference = MathF.Abs(normal.Z) < 0.9f
            ? new Vector3f(0f, 0f, 1f)
            : new Vector3f(1f, 0f, 0f);

        Vector3f right = Normalise(Cross(reference, normal));
        Vector3f up = Normalise(Cross(normal, right));
        if (anchored)
        {
            normal = BulletMarks.ToWorld(orientation, normal);
            right = BulletMarks.ToWorld(orientation, right);
            up = BulletMarks.ToWorld(orientation, up);
        }

        // The size is the half-width: 0x47D920 lays the four corners at +-size on both axes.
        float half = (float)(mark.SizeInUnits / WorldScene.UnitsPerMetre);
        Vector3d centre = new(
            (position.X / WorldScene.UnitsPerMetre) + (normal.X * DepthBiasInMetres) - eye.X,
            (position.Y / WorldScene.UnitsPerMetre) + (normal.Y * DepthBiasInMetres) - eye.Y,
            (position.Z / WorldScene.UnitsPerMetre) + (normal.Z * DepthBiasInMetres) - eye.Z);

        _vertices[_used] = Corner(centre, right, up, normal, -half, -half, 0f, 1f);
        _vertices[_used + 1] = Corner(centre, right, up, normal, half, -half, 1f, 1f);
        _vertices[_used + 2] = Corner(centre, right, up, normal, half, half, 1f, 0f);
        _vertices[_used + 3] = Corner(centre, right, up, normal, -half, half, 0f, 0f);
        _used += 4;
    }

    private static Vertex3D Corner(
        Vector3d centre,
        Vector3f right,
        Vector3f up,
        Vector3f normal,
        float x,
        float y,
        float u,
        float v) =>
        new(
            new Vector3f(
                (float)centre.X + (right.X * x) + (up.X * y),
                (float)centre.Y + (right.Y * x) + (up.Y * y),
                (float)centre.Z + (right.Z * x) + (up.Z * y)),
            normal,
            u,
            v,
            colour: Bgra32.White);

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

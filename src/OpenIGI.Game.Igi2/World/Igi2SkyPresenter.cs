using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Draws I.G.I. 2's sky: the graded dome, then each scrolling cloud layer over it.
/// </summary>
/// <remarks>
/// <para>
/// That order is <c>FlatSky_EnterHandler</c>'s (<c>igi2ed 0x653E10</c>): <c>SkyDome_Draw</c> first, then
/// a walk of the task's <c>FlatSkyLayer</c> children. Both are drawn about the viewer with depth off,
/// because the sky is a backdrop — everything in the world comes out in front of it, and the layers come
/// out in front of the dome by being drawn after it.
/// </para>
/// <para>
/// The geometry is <see cref="SkyDomeGeometry"/> and <see cref="SkyCloudPlane"/>, which the two games
/// share. What this class holds is I.G.I. 2's half: its task types, its texture lookup, and its scroll,
/// which advances once a tick rather than by elapsed time.
/// </para>
/// </remarks>
public sealed class Igi2SkyPresenter : IDisposable
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.sky";

    /// <summary>
    /// How many engine units one unit of the space this presenter draws in is worth.
    /// </summary>
    /// <remarks>
    /// One: I.G.I. 2's world is drawn in engine units directly, so the sky's authored distances need no
    /// conversion. I.G.I. 1's renderer draws in metres and passes 4,096 for the same parameter.
    /// </remarks>
    public const float UnitsPerPosition = 1f;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Layer[] _layers;
    private readonly Vertex3D[] _grid = new Vertex3D[SkyCloudPlane.VertexCount];
    private readonly ushort[] _gridIndices;
    private readonly MeshHandle _dome;
    private readonly int _domeIndexCount;
    private bool _disposed;

    /// <summary>Builds one level's sky.</summary>
    /// <param name="device">The device to upload through and draw with.</param>
    /// <param name="sky">The level's <c>FlatSky</c>.</param>
    /// <param name="textures">Resolves a layer's texture resource to a loaded texture.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The texture lookup is a delegate rather than a file system because what a layer names is a
    /// resource — <c>MISSION:textures/&lt;bare name&gt;</c> — and the host already owns the cache that
    /// turns one of those into a handle. A layer whose texture is missing draws untextured rather than
    /// not at all, which is what the original does with a failed <c>Texture_Load</c>.
    /// </remarks>
    public Igi2SkyPresenter(
        IGraphicsDevice device,
        FlatSkyTask sky,
        Func<string, TextureHandle>? textures = null,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sky);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        Sky = sky;

        (Vector3f[] positions, Bgra32[] colours) = Igi2SkyDome.Build(
            Igi2SkyDome.ColourTableFor(sky),
            Igi2SkyDome.HalfAngleOf(sky.DomeAngle));

        Vertex3D[] vertices = new Vertex3D[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            // Untextured: the dome is pure vertex colour, and the clouds are the layers over it.
            vertices[i] = new Vertex3D(positions[i], Vector3f.Zero, 0f, 0f, 0f, 0f, colours[i]);
        }

        ushort[] indices = SkyDomeGeometry.BuildIndices();
        _dome = _device.CreateMesh(vertices, indices);
        _domeIndexCount = indices.Length;

        _gridIndices = SkyCloudPlane.BuildIndices();
        _layers = [.. CollectLayers(sky, textures)];

        _logger.Information(
            LogCategory,
            $"Sky: a dome of {vertices.Length} vertices over {sky.DomeAngle:0.#} degrees, "
                + $"{_layers.Length} cloud layer(s) at {sky.LayerHeight} m, fading by {sky.Distance} m.");
    }

    /// <summary>Gets the level's sky.</summary>
    public FlatSkyTask Sky { get; }

    /// <summary>Gets how many cloud layers the sky has.</summary>
    public int LayerCount => _layers.Length;

    /// <summary>Gets the colour the frame clears to, which the sky's fog names.</summary>
    /// <remarks>
    /// <c>FlatSky_UpdateInternalDataHandler</c> passes <c>Fog Colour × 255</c> to
    /// <c>Display_SetBackgroundColourFn</c>, so the level's fog colour is literally what is behind
    /// everything.
    /// </remarks>
    public Vector3f BackgroundColour => Sky.FogColour;

    /// <summary>Advances every layer's scroll by one simulation tick.</summary>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    /// <remarks>
    /// Per tick, not per second: <c>FlatSkyLayer_RunHandler</c> (<c>igi2ed 0x654820</c>) adds
    /// <c>X Speed</c> and <c>Y Speed</c> to the running offsets once each time it runs, then wraps each
    /// back into <c>[0, 1)</c>. So a layer's speed is authored in texture widths per tick and the sky
    /// scrolls with the simulation rather than with the frame rate.
    /// </remarks>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Advance();
        }
    }

    /// <summary>Draws the sky behind everything.</summary>
    /// <param name="camera">Where the world is seen from.</param>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    /// <remarks>
    /// Centred on the camera, so the sky never moves relative to the viewer and the horizon stays at the
    /// horizon however far the player walks.
    /// </remarks>
    public void Draw(in Camera3D camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.Disabled);

        // The sky follows the camera, so its model position is the camera's and only the rotation
        // remains in the model-view.
        Matrix4f modelView = camera.CreateModelView(camera.Position);
        _device.DrawMesh(
            _dome,
            0,
            _domeIndexCount,
            modelView,
            TextureHandle.None,
            TextureHandle.None,
            BlendMode.Opaque);

        Vector3f forward = camera.Orientation.Row3;
        for (int i = 0; i < _layers.Length; i++)
        {
            DrawLayer(_layers[i], forward, modelView);
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
        _device.DestroyMesh(_dome);

        foreach (Layer layer in _layers)
        {
            layer.Release(_device);
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 sky, {_domeIndexCount / 3} dome triangles and {_layers.Length} cloud layer(s)";

    private static List<Layer> CollectLayers(FlatSkyTask sky, Func<string, TextureHandle>? textures)
    {
        List<Layer> layers = [];
        foreach (Engine.Tasks.GameTask child in sky.Children)
        {
            if (child is FlatSkyLayerTask layer)
            {
                layers.Add(new Layer(layer, textures?.Invoke(layer.TextureResource) ?? TextureHandle.None));
            }
        }

        return layers;
    }

    private void DrawLayer(Layer layer, Vector3f forward, in Matrix4f modelView)
    {
        SkyCloudPlane.Fill(_grid, layer.Describe(Sky), forward);

        try
        {
            layer.Upload(_device, _grid, _gridIndices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"The cloud grid did not upload: {ex.Message}");
            return;
        }

        _device.DrawMesh(
            layer.Mesh,
            0,
            _gridIndices.Length,
            modelView,
            layer.Texture,
            TextureHandle.None,
            BlendMode.AlphaBlend);
    }

    /// <summary>One <c>FlatSkyLayer</c>, its texture, its scroll and the mesh it is drawn through.</summary>
    private sealed class Layer(FlatSkyLayerTask task, TextureHandle texture)
    {
        private float _scrollU;
        private float _scrollV;

        public TextureHandle Texture { get; } = texture;

        public MeshHandle Mesh { get; private set; } = MeshHandle.None;

        public void Advance()
        {
            _scrollU = SkyCloudPlane.AdvanceScroll(_scrollU, task.SpeedX);
            _scrollV = SkyCloudPlane.AdvanceScroll(_scrollV, task.SpeedY);
        }

        public SkyCloudLayerSample Describe(FlatSkyTask sky) => new(
            sky.LayerHeight * (float)Game.World.WorldScene.UnitsPerMetre,
            sky.Distance * (float)Game.World.WorldScene.UnitsPerMetre,
            sky.FogAmount,
            UnitsPerPosition,
            task.Scale,
            _scrollU,
            _scrollV,
            task.Alpha,
            task.Colour);

        public void Upload(IGraphicsDevice device, Vertex3D[] vertices, ushort[] indices)
        {
            if (Mesh.IsValid)
            {
                device.UpdateAnimatedMesh(Mesh, vertices);
                return;
            }

            // Animated, because that is the device's "vertices replaced every frame" mesh: the update
            // orphans the stale buffer instead of stalling behind the previous frame's draw.
            Mesh = device.CreateAnimatedMesh(vertices, indices);
        }

        public void Release(IGraphicsDevice device)
        {
            if (Mesh.IsValid)
            {
                device.DestroyMesh(Mesh);
                Mesh = MeshHandle.None;
            }

            if (Texture.IsValid)
            {
                device.DestroyTexture(Texture);
            }
        }
    }
}

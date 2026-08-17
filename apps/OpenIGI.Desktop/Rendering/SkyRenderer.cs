using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Draws a level's sky: the gradient dome, then the scrolling cloud layers over it.
/// </summary>
/// <remarks>
/// <para>
/// The sky is script data, not level geometry: a <c>FlatSky</c> task carries the dome colours, the fog
/// colour and the cloud plane's shape, and each <c>FlatSkyLayer</c> child names a texture with a scale,
/// a scroll speed, a tint and an alpha. Everything here is read from those tasks.
/// </para>
/// <para>
/// The dome is the original's exactly (<c>sub_52CB30</c>/<c>sub_52CC40</c>): an apex, fifteen rings six
/// degrees apart, twenty vertices per ring, a skirt half a radius below the horizon, over a sphere of
/// <c>0.8 ×</c> the far plane. Colours run top → middle across <c>[angle, 90°]</c> and middle → bottom
/// across <c>[angle/2, angle]</c> (<c>sub_52CE90</c> is the bilinear blend, with the azimuth picking
/// within each colour pair). It is drawn first, with no depth, centred on the camera.
/// </para>
/// <para>
/// A cloud layer is the plane <c>Z Pos</c> metres above the camera (<c>sub_51E1F0</c>): each vertex is
/// textured by its camera-relative world position times the layer's scale plus the scroll, and faded by
/// view depth — opaque nearer than <c>d0 = D − (D − 0.1m) × fogAmount</c>, then
/// <c>d0 × (1 − D/d) / (d0 − D)</c> beyond it, clamped to <c>[0, 0.999]</c>, where <c>D</c> is the
/// task's <c>Distance</c>. The original evaluates that on a screen-space grid re-projected every frame;
/// here it is a camera-centred polar grid rebuilt every frame, which is the same plane sampled the same
/// way. <b>Divergence:</b> the scroll's time base is the simulation tick assumed at 30 Hz; the original
/// advances it per tick and the exact step has not been read.
/// </para>
/// <para>
/// Mission 1 declares no <c>Lensflare</c> task, so dome, clouds and the fog colour are everything its
/// original sky draws. Levels that do declare one will need the flare ported.
/// </para>
/// </remarks>
public sealed class SkyRenderer : IDisposable
{
    private const string LogCategory = "Sky";

    /// <summary>Rings between the apex and the horizon, six degrees apart.</summary>
    private const int DomeRings = 15;

    /// <summary>Vertices around each ring, eighteen degrees apart.</summary>
    private const int DomeSegments = 20;

    /// <summary>The dome's radius as a fraction of the far plane, as <c>sub_52CC40</c> scales it.</summary>
    private const float DomeRadiusOfFar = 0.8f;

    /// <summary>Radial rows of the cloud grid, camera outwards.</summary>
    private const int CloudRows = 16;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly List<CloudLayer> _layers = [];
    private readonly MeshHandle _dome = MeshHandle.None;

    /// <summary>One mesh per cloud layer, created on first draw and updated in place after.</summary>
    /// <remarks>
    /// The grid's topology never changes and its vertex count is fixed, so rebuilding the mesh per
    /// frame — the first version's approach — spent a buffer create, a buffer delete and three managed
    /// allocations per layer per frame for data that only ever changes in place.
    /// </remarks>
    private MeshHandle[] _cloudMeshes = [];
    private Vertex3D[] _cloudVertices = [];
    private ushort[] _cloudIndices = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SkyRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="fileSystem">Where the layer textures are read from.</param>
    /// <param name="levelRoot">The level task tree holding the <c>FlatSky</c>.</param>
    /// <param name="farPlane">The projection's far plane in metres, which sizes the dome.</param>
    /// <param name="logger">Where to report what was found.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SkyRenderer(
        IGraphicsDevice device,
        VirtualFileSystem fileSystem,
        GameTask levelRoot,
        float farPlane,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(levelRoot);

        _device = device;
        _logger = logger ?? NullLogger.Instance;

        DeclaredTask? sky = FindFlatSky(levelRoot);
        if (sky is null)
        {
            _logger.Information(LogCategory, "The level declares no FlatSky; the sky stays the clear colour.");
            return;
        }

        FogAmount = GetFloat(sky, "Fog Amount", 0.5f);
        PlaneHeight = GetFloat(sky, "Z Pos", 5f);
        Distance = GetFloat(sky, "Distance", 100f);
        FogColour = GetColour(sky, "Fog Color", new Vector3f(0.18f, 0.28f, 0.42f));

        float angle = GetFloat(sky, "SkyDome Angle", 22.5f) * MathF.PI / 180f;
        bool snap = GetBool(sky, "SkyDome Snap Colours");
        Vector3f top = GetColour(sky, "SkyDome Top Colour", default);
        Vector3f middle1 = GetColour(sky, "SkyDome Middle Colour 1", default);
        Vector3f middle2 = GetColour(sky, "SkyDome Middle Colour 2", default);
        Vector3f bottom1 = GetColour(sky, "SkyDome Bottom Colour 1", default);
        Vector3f bottom2 = GetColour(sky, "SkyDome Bottom Colour 2", default);

        // Snapping copies the second colour of each pair over the first, as sub_51EB40 does.
        if (snap)
        {
            middle1 = middle2;
            bottom1 = bottom2;
        }

        _dome = BuildDome(farPlane * DomeRadiusOfFar, angle, top, middle2, middle1, bottom2, bottom1);

        foreach (GameTask child in sky.Children)
        {
            if (child is not DeclaredTask declared
                || !string.Equals(declared.Type.Name, "FlatSkyLayer", StringComparison.Ordinal))
            {
                continue;
            }

            string name = declared.TryGetValue("Texture File Name", out object? value) && value is string s ? s : string.Empty;
            TextureHandle texture = LoadLayerTexture(fileSystem, name);
            _layers.Add(new CloudLayer(
                texture,
                GetFloat(declared, "Scale", 0f),
                GetFloat(declared, "X Speed", 0f),
                GetFloat(declared, "Y Speed", 0f),
                GetFloat(declared, "Alpha", 1f),
                GetColour(declared, "Color", new Vector3f(1f, 1f, 1f))));
        }

        _logger.Information(
            LogCategory,
            $"FlatSky: dome over {angle * 180.0 / Math.PI:0.#} degrees, {_layers.Count} cloud layer(s), "
                + $"plane at {PlaneHeight} m, fades by {Distance} m.");
    }

    /// <summary>Gets the fog colour the frame should clear to; the task's <c>Fog Color</c>.</summary>
    public Vector3f FogColour { get; } = new(0f, 0f, 0f);

    /// <summary>Gets a value indicating whether the level declared a sky at all.</summary>
    public bool HasSky => _dome.IsValid;

    /// <summary>Gets the level's authored fog amount, from its <c>FlatSky</c> task.</summary>
    /// <remarks>Public so the volumetric fog can be seeded from what the level actually asked for.</remarks>
    public float FogAmount { get; }

    /// <summary>Gets the level's authored sky-plane height in metres.</summary>
    public float PlaneHeight { get; }

    /// <summary>Gets the distance in metres over which the level's authored fog reaches full strength.</summary>
    public float Distance { get; }

    /// <summary>Draws the sky behind everything: the dome, then each cloud layer.</summary>
    /// <param name="camera">The camera, positioned in metres.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <param name="seconds">The level's elapsed time, which scrolls the clouds.</param>
    /// <exception cref="ObjectDisposedException">The renderer has been disposed.</exception>
    public void Draw(in Camera3D camera, in Matrix4f projection, float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!HasSky)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.Disabled);

        // The dome follows the camera, so its model position is the camera's and only the rotation
        // remains in the model-view.
        Matrix4f modelView = camera.CreateModelView(camera.Position);
        _device.DrawMesh(_dome, 0, DomeIndexCount, modelView, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

        for (int i = 0; i < _layers.Count; i++)
        {
            DrawCloudLayer(_layers[i], i, camera, modelView, seconds);
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
        if (_dome.IsValid)
        {
            _device.DestroyMesh(_dome);
        }

        foreach (MeshHandle mesh in _cloudMeshes)
        {
            if (mesh.IsValid)
            {
                _device.DestroyMesh(mesh);
            }
        }

        _cloudMeshes = [];

        foreach (CloudLayer layer in _layers)
        {
            if (layer.Texture.IsValid)
            {
                _device.DestroyTexture(layer.Texture);
            }
        }

        _layers.Clear();
    }

    private static int DomeIndexCount =>
        ((DomeSegments + (DomeSegments * 2 * (DomeRings - 1)) + (DomeSegments * 2)) * 3);

    /// <summary>Builds the dome exactly as <c>sub_52CC40</c> lays it out.</summary>
    private MeshHandle BuildDome(
        float radius,
        float angle,
        Vector3f top,
        Vector3f middleA,
        Vector3f middleB,
        Vector3f bottomA,
        Vector3f bottomB)
    {
        const float RingStep = MathF.PI / 30f;
        const float SegmentStep = MathF.PI / 10f;

        var vertices = new List<Vertex3D>(1 + (DomeRings * DomeSegments) + DomeSegments)
        {
            new(new Vector3f(0f, 0f, radius), Vector3f.Zero, 0f, 0f, 0f, 0f, ToColour(top)),
        };

        for (int ring = 1; ring <= DomeRings; ring++)
        {
            float elevation = (MathF.PI / 2f) - (ring * RingStep);
            for (int segment = 0; segment < DomeSegments; segment++)
            {
                float azimuth = segment * SegmentStep;
                Vector3f position = new(
                    MathF.Cos(azimuth) * radius * MathF.Cos(elevation),
                    MathF.Sin(azimuth) * radius * MathF.Cos(elevation),
                    radius * MathF.Sin(elevation));

                // The azimuth parameter runs 1 → 0 → 0.9 around the ring, selecting within each
                // colour pair; the elevation parameter blends the pairs.
                float across = MathF.Abs(1f - (segment * 0.1f));
                Vector3f colour = elevation >= angle
                    ? Bilerp(top, top, middleA, middleB, across, 1f - ((elevation - angle) / ((MathF.PI / 2f) - angle)))
                    : Bilerp(
                        middleA,
                        middleB,
                        bottomA,
                        bottomB,
                        across,
                        1f - (elevation < angle * 0.5f ? 0f : (elevation / (angle * 0.5f)) - 1f));

                vertices.Add(new Vertex3D(position, Vector3f.Zero, 0f, 0f, 0f, 0f, ToColour(colour)));
            }
        }

        // The skirt: the horizon ring's positions and colours again, half a radius down, so turning the
        // camera below the horizon shows sky rather than the void.
        int horizonStart = 1 + ((DomeRings - 1) * DomeSegments);
        for (int segment = 0; segment < DomeSegments; segment++)
        {
            Vertex3D horizon = vertices[horizonStart + segment];
            vertices.Add(new Vertex3D(
                new Vector3f(horizon.Position.X, horizon.Position.Y, radius * -0.5f),
                Vector3f.Zero,
                0f,
                0f,
                0f,
                0f,
                horizon.Colour));
        }

        var indices = new List<ushort>(DomeIndexCount);
        for (int segment = 0; segment < DomeSegments; segment++)
        {
            indices.Add(0);
            indices.Add((ushort)(1 + segment));
            indices.Add((ushort)(1 + ((segment + 1) % DomeSegments)));
        }

        for (int ring = 0; ring < DomeRings; ring++)
        {
            int upper = 1 + ((ring - 1) * DomeSegments);
            int lower = 1 + (ring * DomeSegments);
            if (ring == 0)
            {
                continue;
            }

            AddBand(indices, upper, lower);
        }

        AddBand(indices, horizonStart, horizonStart + DomeSegments);

        return _device.CreateMesh([.. vertices], [.. indices]);

        static void AddBand(List<ushort> indices, int upper, int lower)
        {
            for (int segment = 0; segment < DomeSegments; segment++)
            {
                int next = (segment + 1) % DomeSegments;
                indices.Add((ushort)(upper + segment));
                indices.Add((ushort)(lower + segment));
                indices.Add((ushort)(upper + next));
                indices.Add((ushort)(upper + next));
                indices.Add((ushort)(lower + segment));
                indices.Add((ushort)(lower + next));
            }
        }
    }

    /// <summary>Recomputes and draws one cloud layer for this frame's camera.</summary>
    /// <remarks>
    /// The vertices are a function of view depth, exactly as the original re-projects its grid per
    /// frame — but the buffers are persistent: the vertex array is reused, the index topology is built
    /// once, and the GPU mesh is updated in place rather than destroyed and recreated.
    /// </remarks>
    private void DrawCloudLayer(
        in CloudLayer layer,
        int layerIndex,
        in Camera3D camera,
        in Matrix4f modelView,
        float seconds)
    {
        // The fade window, in engine units: opaque out to nearDepth, gone by the task's Distance.
        float distanceUnits = Distance * (float)WorldScene.UnitsPerMetre;
        float nearDepth = distanceUnits - ((distanceUnits - 409.6f) * FogAmount);
        float inverse = 1f / (nearDepth - distanceUnits);

        Vector3f forward = camera.Orientation.Row3;

        // The scroll advances per simulation tick; 30 Hz is the engine's rate.
        float scrollX = seconds * layer.SpeedX * 30f;
        float scrollY = seconds * layer.SpeedY * 30f;

        int columns = DomeSegments;
        if (_cloudVertices.Length == 0)
        {
            _cloudVertices = new Vertex3D[(CloudRows + 1) * columns];
        }

        if (_cloudMeshes.Length != _layers.Count)
        {
            _cloudMeshes = new MeshHandle[_layers.Count];
        }

        Vertex3D[] vertices = _cloudVertices;
        float maxRadius = Distance * 1.5f;

        for (int row = 0; row <= CloudRows; row++)
        {
            // Quadratic spacing keeps the rows dense overhead where the texture is sharpest.
            float t = row / (float)CloudRows;
            float radial = maxRadius * t * t;

            for (int column = 0; column < columns; column++)
            {
                float azimuth = column * (MathF.Tau / columns);
                Vector3f position = new(
                    MathF.Cos(azimuth) * radial,
                    MathF.Sin(azimuth) * radial,
                    PlaneHeight);

                float depthUnits = ((forward.X * position.X) + (forward.Y * position.Y) + (forward.Z * position.Z))
                    * (float)WorldScene.UnitsPerMetre;

                float fade = depthUnits < nearDepth
                    ? 1f
                    : Math.Clamp((nearDepth - (nearDepth * distanceUnits / depthUnits)) * inverse, 0f, 0.999f);

                float u = (position.X * (float)WorldScene.UnitsPerMetre * layer.Scale) + scrollX;
                float v = (position.Y * (float)WorldScene.UnitsPerMetre * layer.Scale) + scrollY;

                vertices[(row * columns) + column] = new Vertex3D(
                    position,
                    Vector3f.Zero,
                    u,
                    v,
                    0f,
                    0f,
                    ToColour(layer.Colour, fade * layer.Alpha));
            }
        }

        if (_cloudIndices.Length == 0)
        {
            _cloudIndices = BuildCloudIndices(columns);
        }

        try
        {
            if (_cloudMeshes[layerIndex].IsValid)
            {
                _device.UpdateAnimatedMesh(_cloudMeshes[layerIndex], vertices);
            }
            else
            {
                // Animated, because that is the device's "vertices replaced every frame" mesh: the
                // update orphans the stale buffer instead of stalling behind the previous frame's draw.
                _cloudMeshes[layerIndex] = _device.CreateAnimatedMesh(vertices, _cloudIndices);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"The cloud grid did not upload: {ex.Message}");
            return;
        }

        _device.DrawMesh(
            _cloudMeshes[layerIndex],
            0,
            _cloudIndices.Length,
            modelView,
            layer.Texture,
            TextureHandle.None,
            BlendMode.AlphaBlend);
    }

    /// <summary>Builds the cloud grid's triangle list, which never changes after that.</summary>
    private static ushort[] BuildCloudIndices(int columns)
    {
        ushort[] indices = new ushort[CloudRows * columns * 6];
        int cursor = 0;
        for (int row = 0; row < CloudRows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int next = (column + 1) % columns;
                int a = (row * columns) + column;
                int b = (row * columns) + next;
                int c = ((row + 1) * columns) + column;
                int d = ((row + 1) * columns) + next;
                indices[cursor++] = (ushort)a;
                indices[cursor++] = (ushort)c;
                indices[cursor++] = (ushort)b;
                indices[cursor++] = (ushort)b;
                indices[cursor++] = (ushort)c;
                indices[cursor++] = (ushort)d;
            }
        }

        return indices;
    }

    private TextureHandle LoadLayerTexture(VirtualFileSystem fileSystem, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TextureHandle.None;
        }

        string path = "LOCAL:" + name;
        try
        {
            if (!fileSystem.Exists(path))
            {
                _logger.Warning(LogCategory, $"Cloud texture '{name}' is not in the installation.");
                return TextureHandle.None;
            }

            TextureImage image = TexFile.Load(fileSystem.ReadAllBytes(path));
            return _device.CreateTexture(
                new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
                image.Pixels);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or GraphicsDeviceException or TextureFormatException)
        {
            _logger.Warning(LogCategory, $"Cloud texture '{name}' did not load: {ex.Message}");
            return TextureHandle.None;
        }
    }

    private static DeclaredTask? FindFlatSky(GameTask task)
    {
        if (task is DeclaredTask declared && string.Equals(declared.Type.Name, "FlatSky", StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (FindFlatSky(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The blend of <c>sub_52CE90</c>: within each pair by <paramref name="across"/>, then between pairs.</summary>
    private static Vector3f Bilerp(Vector3f a, Vector3f b, Vector3f c, Vector3f d, float across, float between)
    {
        Vector3f upper = Lerp(a, b, across);
        Vector3f lower = Lerp(c, d, across);
        return Lerp(upper, lower, between);

        static Vector3f Lerp(Vector3f from, Vector3f to, float t) => new(
            from.X + ((to.X - from.X) * t),
            from.Y + ((to.Y - from.Y) * t),
            from.Z + ((to.Z - from.Z) * t));
    }

    private static Bgra32 ToColour(Vector3f colour, float alpha = 1f) => new(
        (byte)Math.Clamp((int)(colour.Z * 255f), 0, 255),
        (byte)Math.Clamp((int)(colour.Y * 255f), 0, 255),
        (byte)Math.Clamp((int)(colour.X * 255f), 0, 255),
        (byte)Math.Clamp((int)(alpha * 255f), 0, 255));

    private static float GetFloat(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float f ? f : fallback;

    private static bool GetBool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool b && b;

    private static Vector3f GetColour(DeclaredTask task, string name, Vector3f fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3f v ? v : fallback;

    /// <summary>One <c>FlatSkyLayer</c>: a texture scrolled across the cloud plane.</summary>
    private readonly record struct CloudLayer(
        TextureHandle Texture,
        float Scale,
        float SpeedX,
        float SpeedY,
        float Alpha,
        Vector3f Colour);
}

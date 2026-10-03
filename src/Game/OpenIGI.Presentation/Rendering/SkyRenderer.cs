using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

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
/// The dome follows the observed retail topology: an apex, fifteen rings six degrees apart, twenty
/// vertices per ring, and a skirt half a radius below the horizon. The paired native listing confirms
/// the <c>0.8</c> radius multiplier at <c>0x52CC40</c>; the source of its base radius remains deliberately
/// unnamed here until its renderer type is recovered. Colours run top → middle across <c>[angle, 90°]</c>
/// and middle → bottom across <c>[angle/2, angle]</c>. It is drawn first, with no depth, centred on the camera.
/// </para>
/// <para>
/// A cloud layer is the plane <c>Z Pos</c> metres above the camera (<c>sub_51E1F0</c>): each vertex is
/// textured by its camera-relative world position times the layer's scale plus the scroll, and faded by
/// view depth — opaque nearer than <c>d0 = D − (D − 0.1m) × fogAmount</c>, then
/// <c>d0 × (1 − D/d) / (d0 − D)</c> beyond it, clamped to <c>[0, 0.999]</c>, where <c>D</c> is the
/// task's <c>Distance</c>. The grid it is drawn on is the original's and is laid out across the SCREEN,
/// not around the camera -- see <see cref="TryBuildCloudGrid"/>, which is also where the two artefacts a
/// camera-centred disc printed instead are written down. <b>Divergence:</b> the scroll's time base is the
/// simulation tick assumed at 30 Hz; the original advances it per tick and the exact step has not been
/// read.
/// </para>
/// <para>
/// Mission 1 declares no <c>Lensflare</c> task, so dome, clouds and the fog colour are everything its
/// original sky draws. Levels that do declare one will need the flare ported.
/// </para>
/// </remarks>
public sealed class SkyRenderer : ISkyPresenter
{
    private const string LogCategory = "Sky";

    /// <summary>Rings between the apex and the horizon, six degrees apart.</summary>
    private const int DomeRings = 15;

    /// <summary>Vertices around each ring, eighteen degrees apart.</summary>
    private const int DomeSegments = 20;

    /// <summary>The paired-listing-confirmed multiplier applied to the dome's base radius.</summary>
    private const float DomeRadiusOfFar = 0.8f;

    /// <summary>Rows of quads in the cloud strip, as <c>sub_51E1F0</c> steps it across the screen.</summary>
    private const int CloudRows = 12;

    /// <summary>Vertices across each row of the strip, which the original spans in one quad.</summary>
    /// <remarks>
    /// Two is exact rather than coarse for an upright camera, which is what the game plays in. The
    /// texture coordinates are affine in the plane, so perspective interpolation across a quad reproduces
    /// them however wide it is; and the fade is by view depth, which along a screen line parallel to the
    /// horizon does not vary at all until the camera rolls. Rolled -- a cutscene, say -- the fade across
    /// a row becomes the linear approximation it is in the original too.
    /// </remarks>
    private const int CloudColumns = 2;

    /// <summary>The original's near plane, in engine units: <c>0.1 m</c>, as <c>sub_51E1F0</c> tests it.</summary>
    private const float NearDepthUnits = 409.6f;

    /// <summary>The original's overscan on the strip, so its edges clear the viewport's.</summary>
    private const float StripOverscan = 1.001f;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly List<CloudLayer> _layers = [];
    private MeshHandle _dome = MeshHandle.None;

    // The authored dome gradient, kept so the dome can be rebuilt with a sun tint. The sky an interior sees
    // reddens as the sun sinks, and this dome is what a window shows and what the screen-space bounce reads
    // off it -- so warming it at a low sun is what carries the horizon's colour into the room.
    private readonly Vector3f _domeTop;
    private readonly Vector3f _domeMiddle1;
    private readonly Vector3f _domeMiddle2;
    private readonly Vector3f _domeBottom1;
    private readonly Vector3f _domeBottom2;
    private readonly float _domeAngle;
    private readonly float _domeRadius;
    private Vector3f _sunTint = Vector3f.One;

    /// <summary>One mesh per cloud layer, created on first draw and updated in place after.</summary>
    /// <remarks>
    /// The grid's topology never changes and its vertex count is fixed, so rebuilding the mesh per
    /// frame — the first version's approach — spent a buffer create, a buffer delete and three managed
    /// allocations per layer per frame for data that only ever changes in place.
    /// </remarks>
    private MeshHandle[] _cloudMeshes = [];
    private Vertex3D[] _cloudVertices = [];
    private ushort[] _cloudIndices = [];

    /// <summary>This frame's strip, cast onto the plane: shared by every layer, which differ only in shading.</summary>
    private Vector3f[] _cloudPositions = [];
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
        IOpenIgiLogger? logger = null)
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

        _domeRadius = farPlane * DomeRadiusOfFar;
        _domeAngle = angle;
        _domeTop = top;
        _domeMiddle1 = middle1;
        _domeMiddle2 = middle2;
        _domeBottom1 = bottom1;
        _domeBottom2 = bottom2;
        RebuildDome();

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

    /// <inheritdoc/>
    public void Draw(in SkyFrame frame) => Draw(frame.Camera, frame.Projection, frame.Seconds);

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

        // The sky is isolated from world fog so its authored gradient remains visible. Retail's sky draw
        // performs a state sequence at 0x52D060, but naming an individual call FOGENABLE needs recovered
        // Direct3D types; do not claim that mapping before then. Clouds use their authored distance ramp.
        SceneFog fog = _device.Fog;
        Vector3f ambient = _device.ModelAmbient;
        _device.SetFog(SceneFog.None);

        // Nor is the sky a model: its gradient is in its own vertices and the level's flat model light
        // has no business multiplying it. The original keeps the dome in its own state block for the
        // same reason it keeps it out of the depth buffer -- it is the backdrop, not the world.
        _device.SetModelAmbient(Vector3f.One);

        // The dome follows the camera, so its model position is the camera's and only the rotation
        // remains in the model-view.
        Matrix4f modelView = camera.CreateModelView(camera.Position);
        _device.DrawMesh(_dome, 0, DomeIndexCount, modelView, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

        // One strip for every layer: they share the geometry and differ only in texture, tint and scroll.
        if (_layers.Count > 0 && TryBuildCloudGrid(camera, projection))
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                DrawCloudLayer(_layers[i], i, camera, modelView, seconds);
            }
        }

        _device.SetFog(fog);
        _device.SetModelAmbient(ambient);
    }

    /// <summary>Tints the dome to the sun's warmth, rebuilding it when the tint moves enough to matter.</summary>
    /// <param name="tint">A per-channel multiplier, one for the authored sky, warmer as the sun sinks.</param>
    /// <remarks>
    /// The dome's gradient is baked into its vertices, so a tint means rebuilding the mesh -- cheap for a
    /// dome of a few hundred vertices, but not worth doing every frame as the sun creeps. A small dead-band
    /// rebuilds only when the tint has shifted enough to see, so a still sun rebuilds once and a moving one a
    /// handful of times across the day.
    /// </remarks>
    public void SetSunTint(Vector3f tint)
    {
        if (_disposed || !HasSky)
        {
            return;
        }

        Vector3f delta = tint - _sunTint;
        if ((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z) < 0.0004f)
        {
            return;
        }

        _sunTint = tint;
        RebuildDome();
    }

    private void RebuildDome()
    {
        if (_dome.IsValid)
        {
            _device.DestroyMesh(_dome);
            _dome = MeshHandle.None;
        }

        _dome = BuildDome(
            _domeRadius,
            _domeAngle,
            _domeTop * _sunTint,
            _domeMiddle2 * _sunTint,
            _domeMiddle1 * _sunTint,
            _domeBottom2 * _sunTint,
            _domeBottom1 * _sunTint);
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

    /// <summary>Builds the paired-listing-confirmed dome topology.</summary>
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

    /// <summary>
    /// Lays this frame's cloud strip across the screen and casts each of its vertices onto the plane,
    /// exactly as <c>sub_51E1F0</c> does.
    /// </summary>
    /// <param name="camera">The camera the strip is laid out for.</param>
    /// <param name="projection">The view-to-clip transform the strip is laid out in.</param>
    /// <returns><see langword="true"/> when a strip was built; <see langword="false"/> when the plane is
    /// not in front of the camera and nothing should be drawn.</returns>
    /// <remarks>
    /// <para>
    /// The original builds this grid in screen space, and that detail is the whole of it. Two points on
    /// the plane -- the fade distance <c>D</c> ahead, and <c>D</c> to either side of that -- are
    /// projected, and the line through them is the strip's first row; twelve more rows step from there
    /// out to the edge of the viewport, each spanning the viewport's whole width. Every vertex is then
    /// the ray through its own screen position cast onto the plane, which is the same surface the plane
    /// always was, sampled where it is actually seen. Because the lateral axis is perpendicular to the
    /// view axis, that first row is parallel to the horizon whatever the camera does, and lies on the
    /// plane's side of it -- so no vertex can fall behind the camera, and the strip's outer edge sits
    /// exactly where the fade has reached zero.
    /// </para>
    /// <para>
    /// The version before this drew a disc of <c>1.5 x D</c> around the camera instead, and printed two
    /// artefacts for it. The disc has a rim, at <c>atan(ZPos / 1.5D)</c> above the horizon, with cloud on
    /// one side of it and bare sky on the other: missions 3 and 9 ask for <c>ZPos 6 / D 40</c>, which put
    /// that rim five and a half degrees up, and it was photographed there as a lens hanging in the sky.
    /// Every other mission asks for a plane 1.3 to 4 m up against 100 to 250 m of fade, which puts the
    /// same rim within a degree of the horizon. And the fade is by view depth, which runs to zero abreast
    /// of the camera and negative behind it, so the whole band of the disc level with the camera took the
    /// formula's "nearer than the fade starts" branch and came out at full opacity however far away it
    /// was. A screen-space strip has neither defect available to it: there is no rim on the screen to
    /// see, and every vertex is in front of the camera by construction.
    /// </para>
    /// </remarks>
    private bool TryBuildCloudGrid(in Camera3D camera, in Matrix4f projection)
    {
        if (PlaneHeight <= 0f || Distance <= 0f)
        {
            return false;
        }

        Vector3f right = camera.Orientation.Row1;
        Vector3f up = camera.Orientation.Row2;
        Vector3f forward = camera.Orientation.Row3;

        // The view axis flattened onto the ground and the lateral axis across it. The original
        // normalises the forward row's horizontal half for the same pair, and substitutes +X when the
        // camera looks straight up or down and there is no horizontal half to normalise.
        float horizontal = MathF.Sqrt((forward.X * forward.X) + (forward.Y * forward.Y));
        float cos = horizontal > 1e-6f ? forward.X / horizontal : 1f;
        float sin = horizontal > 1e-6f ? forward.Y / horizontal : 0f;
        Vector3f lateral = new(-sin, cos, 0f);
        Vector3f ahead = new(cos, sin, 0f);
        Vector3f overhead = new(0f, 0f, PlaneHeight);

        Vector3f leftEnd = (ahead * Distance) + (lateral * Distance) + overhead;
        Vector3f rightEnd = (ahead * Distance) - (lateral * Distance) + overhead;

        if (!TryProject(projection, right, up, forward, leftEnd, out float ax, out float ay)
            || !TryProject(projection, right, up, forward, rightEnd, out float bx, out float by))
        {
            // Both ends must clear the near plane, as the original requires before it lays anything out.
            return false;
        }

        float alongX = bx - ax;
        float alongY = by - ay;
        float alongLength = MathF.Sqrt((alongX * alongX) + (alongY * alongY));
        if (alongLength < 1e-8f)
        {
            return false;
        }

        alongX /= alongLength;
        alongY /= alongLength;
        float acrossX = alongY;
        float acrossY = -alongX;

        // Which way off that line the plane lies. A ray's height above the camera is affine in its
        // screen position, so this gradient is exact, and the plane is overhead -- so the side where
        // the height climbs is the side with cloud on it.
        float climbX = right.Z / projection.M11;
        float climbY = up.Z / projection.M22;
        float climb = (acrossX * climbX) + (acrossY * climbY);
        if (climb < 0f)
        {
            acrossX = -acrossX;
            acrossY = -acrossY;
            climb = -climb;
        }

        float midX = (ax + bx) * 0.5f;
        float midY = (ay + by) * 0.5f;

        float minAlong = float.MaxValue;
        float maxAlong = float.MinValue;
        float minAcross = float.MaxValue;
        float maxAcross = float.MinValue;
        for (int corner = 0; corner < 4; corner++)
        {
            float dx = ((corner & 1) == 0 ? -1f : 1f) - midX;
            float dy = ((corner & 2) == 0 ? -1f : 1f) - midY;
            float along = (dx * alongX) + (dy * alongY);
            float across = (dx * acrossX) + (dy * acrossY);
            minAlong = MathF.Min(minAlong, along);
            maxAlong = MathF.Max(maxAlong, along);
            minAcross = MathF.Min(minAcross, across);
            maxAcross = MathF.Max(maxAcross, across);
        }

        if (maxAcross < 0f)
        {
            // The plane's side of its own vanishing line is off the screen entirely.
            return false;
        }

        // The strip starts on that line, as the original starts it: between the line and the horizon the
        // fade has already reached zero, and starting there is what keeps every vertex off the horizon.
        // Aimed straight up there is no vanishing line on the screen to start from -- the gradient goes
        // flat -- and then the strip has to cover the viewport instead.
        float startAcross = climb > 1e-9f ? 0f : minAcross;
        float step = (maxAcross - startAcross) * StripOverscan / CloudRows;

        // Bounds the crossing distance for a vertex floating point puts on the horizon rather than just
        // above it. The fade reached zero long before here, so the clamp cannot be seen.
        float minClimb = PlaneHeight / (Distance * 4f);

        if (_cloudPositions.Length == 0)
        {
            _cloudPositions = new Vector3f[(CloudRows + 1) * CloudColumns];
        }

        for (int row = 0; row <= CloudRows; row++)
        {
            float across = startAcross + (step * row);
            for (int column = 0; column < CloudColumns; column++)
            {
                float along = (column == 0 ? minAlong : maxAlong) * StripOverscan;
                float ndcX = midX + (alongX * along) + (acrossX * across);
                float ndcY = midY + (alongY * along) + (acrossY * across);

                // The ray through that screen position, at a view depth of one so the crossing
                // parameter is the view depth itself, and where it crosses the cloud plane.
                float viewX = (ndcX - projection.M13) / projection.M11;
                float viewY = (ndcY - projection.M23) / projection.M22;
                Vector3f ray = (right * viewX) + (up * viewY) + forward;
                _cloudPositions[(row * CloudColumns) + column] =
                    ray * (PlaneHeight / MathF.Max(ray.Z, minClimb));
            }
        }

        return true;
    }

    /// <summary>Projects a camera-relative position, in metres, to normalised device coordinates.</summary>
    /// <returns><see langword="false"/> when the position is nearer than the original's near plane.</returns>
    private static bool TryProject(
        in Matrix4f projection,
        Vector3f right,
        Vector3f up,
        Vector3f forward,
        Vector3f position,
        out float ndcX,
        out float ndcY)
    {
        ndcX = 0f;
        ndcY = 0f;

        float depth = Vector3f.Dot(forward, position);
        if (depth * (float)WorldScene.UnitsPerMetre < NearDepthUnits)
        {
            return false;
        }

        ndcX = (projection.M11 * (Vector3f.Dot(right, position) / depth)) + projection.M13;
        ndcY = (projection.M22 * (Vector3f.Dot(up, position) / depth)) + projection.M23;
        return true;
    }

    /// <summary>Shades this frame's strip for one layer and draws it.</summary>
    /// <remarks>
    /// The buffers are persistent: the vertex array is reused, the index topology is built once, and the
    /// GPU mesh is updated in place rather than destroyed and recreated.
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
        float nearDepth = distanceUnits - ((distanceUnits - NearDepthUnits) * FogAmount);
        float inverse = 1f / (nearDepth - distanceUnits);

        Vector3f forward = camera.Orientation.Row3;

        // The scroll advances per simulation tick; 30 Hz is the engine's rate.
        float scrollX = seconds * layer.SpeedX * 30f;
        float scrollY = seconds * layer.SpeedY * 30f;

        if (_cloudVertices.Length != _cloudPositions.Length)
        {
            _cloudVertices = new Vertex3D[_cloudPositions.Length];
        }

        if (_cloudMeshes.Length != _layers.Count)
        {
            _cloudMeshes = new MeshHandle[_layers.Count];
        }

        Vertex3D[] vertices = _cloudVertices;

        for (int i = 0; i < _cloudPositions.Length; i++)
        {
            Vector3f position = _cloudPositions[i];
            float depthUnits = Vector3f.Dot(forward, position) * (float)WorldScene.UnitsPerMetre;

            float fade = depthUnits < nearDepth
                ? 1f
                : Math.Clamp((nearDepth - (nearDepth * distanceUnits / depthUnits)) * inverse, 0f, 0.999f);

            float u = (position.X * (float)WorldScene.UnitsPerMetre * layer.Scale) + scrollX;
            float v = (position.Y * (float)WorldScene.UnitsPerMetre * layer.Scale) + scrollY;

            vertices[i] = new Vertex3D(
                position,
                Vector3f.Zero,
                u,
                v,
                0f,
                0f,
                ToColour(layer.Colour, fade * layer.Alpha));
        }

        if (_cloudIndices.Length == 0)
        {
            _cloudIndices = BuildCloudIndices();
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

    /// <summary>Builds the strip's triangle list, which never changes after that.</summary>
    private static ushort[] BuildCloudIndices()
    {
        ushort[] indices = new ushort[CloudRows * (CloudColumns - 1) * 6];
        int cursor = 0;
        for (int row = 0; row < CloudRows; row++)
        {
            for (int column = 0; column < CloudColumns - 1; column++)
            {
                int a = (row * CloudColumns) + column;
                int b = a + 1;
                int c = a + CloudColumns;
                int d = c + 1;
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

            // Mipped for the same reason the world and the ground are: a cloud layer seen edge-on at the
            // horizon is minified hard, and with one level it sparkles along the whole skyline.
            TextureImage image = TexFile.Load(fileSystem.ReadAllBytes(path)).WithMipChain();
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

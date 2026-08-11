using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Renders procedural falling rain particle streaks for levels featuring rain weather.
/// </summary>
/// <remarks>
/// <para>
/// Direct port of the reference implementation in <c>renderer_rain.cpp</c> / <c>renderer_rain.h</c>
/// from <c>project-igi-editor</c>.
/// </para>
/// <para>
/// Reads parameters from the level's <c>RainEffect</c> task (such as <c>Traceline start</c>, <c>Traceline end</c>,
/// and <c>Rain Alpha</c>). When active, rain particle streaks are dynamically positioned in a bounding volume
/// surrounding the camera.
/// </para>
/// </remarks>
public sealed class RainRenderer : IDisposable
{
    private const string LogCategory = "Rain";

    /// <summary>The default number of small rain droplets to simulate.</summary>
    public const int DefaultParticleCount = 1200;

    /// <summary>The bounding volume box size in meters around the camera.</summary>
    public const float DefaultBoxSizeMeters = 50.0f;

    /// <summary>The vertical length of each small rain droplet in meters.</summary>
    public const float StreakLengthMeters = 0.08f;

    /// <summary>The slowest droplet speed as a fraction of the authored rain band.</summary>
    public const float MinimumSpeedMultiplier = 0.08f;

    /// <summary>The fastest droplet speed as a fraction of the authored rain band.</summary>
    public const float MaximumSpeedMultiplier = 0.18f;

    /// <summary>The maximum per-droplet alpha used by the subtle rain pass.</summary>
    public const float MaximumStreakAlpha = 0.28f;

    private const float StreakHalfWidthMeters = 0.006f;

    // The reference rain fragment shader writes its colour directly. Keep the forward mesh on the
    // equivalent unlit path so level lighting cannot turn the pale streaks into nearly-black geometry.
    private static readonly SurfaceMaterial RainMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    /// <summary>The task-name prefix used by level-authored building shells.</summary>
    public const string BuildingTaskPrefix = "Building_";

    /// <summary>An axis-aligned world-space box one building's shell occupies, in meters.</summary>
    /// <remarks>
    /// The reference editor's indoor test is exactly this: the camera is inside the building's mesh
    /// bounding box, so rain is suppressed (<c>Renderer_Rain::SetIndoors</c>). One volume per
    /// building placement, precomputed from the model geometry.
    /// </remarks>
    public readonly record struct BuildingVolume(Vector3d Minimum, Vector3d Maximum)
    {
        /// <summary>Reports whether a world position (in meters) lies inside the shell's box.</summary>
        public bool Contains(Vector3d position) =>
            position.X >= Minimum.X && position.X <= Maximum.X
            && position.Y >= Minimum.Y && position.Y <= Maximum.Y
            && position.Z >= Minimum.Z && position.Z <= Maximum.Z;
    }

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Vertex3D[] _vertices;
    private readonly ushort[] _indices;
    private readonly (float X, float Y, float Z)[] _seeds;

    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    private bool _active;
    private bool _authoredAvailable;
    private float _speedMultiplier = 1f;
    private bool _indoors;
    private float _startMeters = 10.0f;
    private float _endMeters = 2.0f;
    private float _alpha = 0.5f;
    private readonly int _numDrops;

    /// <summary>Initializes a new instance of the <see cref="RainRenderer"/> class.</summary>
    /// <param name="device">The graphics device to draw with.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="numDrops">Number of rain particle drops to simulate (default 1200).</param>
    public RainRenderer(IGraphicsDevice device, IIgiLogger? logger = null, int numDrops = DefaultParticleCount)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _numDrops = Math.Max(1, numDrops);

        _vertices = new Vertex3D[_numDrops * 4];
        _indices = new ushort[_numDrops * 6];
        _seeds = new (float X, float Y, float Z)[_numDrops];

        GenerateSeeds();
        BuildIndices();
    }

    /// <summary>Gets a value indicating whether rain rendering is active and visible.</summary>
    public bool IsActive => _active && !_indoors;

    /// <summary>Gets or sets whether the player/camera is currently indoors (suppressing rain rendering).</summary>
    public bool Indoors
    {
        get => _indoors;
        set => _indoors = value;
    }

    /// <summary>Gets the rain height start in meters above camera.</summary>
    public float StartMeters => _startMeters;

    /// <summary>Gets the authored rain lower distance in meters below the camera.</summary>
    public float EndMeters => _endMeters;

    /// <summary>Gets the rain alpha transparency level.</summary>
    public float Alpha => _alpha;

    /// <summary>Gets the total simulated particle drop count.</summary>
    public int DropCount => _numDrops;

    /// <summary>Gets the user-selected speed multiplier.</summary>
    public float SpeedMultiplier => _speedMultiplier;

    /// <summary>Configures the rain particle renderer parameters.</summary>
    /// <param name="active">Whether rain rendering is active.</param>
    /// <param name="startMeters">Height above ground in meters where rain begins falling.</param>
    /// <param name="endMeters">Height above ground in meters where rain disappears.</param>
    /// <param name="alpha">Alpha transparency factor for rain streaks.</param>
    public void SetParams(bool active, float startMeters, float endMeters, float alpha)
    {
        _authoredAvailable = active;
        _active = active;
        _startMeters = startMeters;
        _endMeters = endMeters;
        _alpha = alpha;
    }

    /// <summary>Applies the live weather settings without changing the level-authored particle band.</summary>
    public void ApplySettings(bool enabled, WeatherKind kind, float speedMultiplier)
    {
        _active = enabled && _authoredAvailable && kind == WeatherKind.Rain;
        _speedMultiplier = Math.Clamp(speedMultiplier, 0f, 2f);
    }

    /// <summary>Recursively searches the task tree for a <c>RainEffect</c> task.</summary>
    /// <param name="task">Root task to search.</param>
    /// <returns>The <c>RainEffect</c> declared task, or <see langword="null"/> if not found.</returns>
    public static DeclaredTask? FindRainEffect(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task is DeclaredTask declared && string.Equals(declared.Type.Name, "RainEffect", StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (FindRainEffect(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Reads and applies rain parameters from a level's task tree if a <c>RainEffect</c> task exists.</summary>
    /// <param name="levelRoot">The level's task tree root.</param>
    /// <returns><see langword="true"/> when rain was parsed and enabled by the level's data; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Port of the reference fix in <c>app_level.cpp</c>: a <c>RainEffect</c> task only produces rain when
    /// its <c>Is Rain</c> and <c>Is Active</c> flags are both true. Levels ship the flags in different
    /// shapes — boolean, integer or quoted string — so each is read tolerantly, and the <c>Traceline</c>
    /// heights are reused straight from the level's <c>objects.qvm</c>.
    /// </remarks>
    public bool ReadParams(GameTask levelRoot)
    {
        DeclaredTask? rainTask = FindRainEffect(levelRoot);
        if (rainTask is null)
        {
            _authoredAvailable = false;
            _active = false;
            return false;
        }

        bool isRain = ReadFlag(rainTask, "Is Rain");
        bool isActive = ReadFlag(rainTask, "Is Active");
        _authoredAvailable = isActive;

        float start = ReadFloat(rainTask, "Traceline start", _startMeters);
        float end = ReadFloat(rainTask, "Traceline end", _endMeters);
        float alpha = ReadFloat(rainTask, "Rain Alpha", _alpha);

        SetParams(active: isRain && isActive, startMeters: start, endMeters: end, alpha: alpha);
        _logger.Information(
            LogCategory,
            isRain && isActive
                ? $"RainEffect configured: start={_startMeters}m, end={_endMeters}m, alpha={_alpha}."
                : $"RainEffect present but disabled by level data (Is Rain={isRain}, Is Active={isActive}).");
        return isRain && isActive;
    }

    /// <summary>Reads one RainEffect flag, tolerating boolean, integer and quoted-string spellings.</summary>
    internal static bool ReadFlag(DeclaredTask task, string name)
    {
        if (!task.TryGetValue(name, out object? raw))
        {
            return true;
        }

        return raw switch
        {
            bool flag => flag,
            int value => value != 0,
            string text => IsTrueText(text),
            _ => true,
        };
    }

    internal static bool IsTrueText(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(trimmed, out int numeric) && numeric != 0;
    }

    /// <summary>Calculates the top and bottom Z world coordinates for rain particle bounds around camera Z.</summary>
    /// <param name="cameraZ">Camera Z world position in meters.</param>
    /// <returns>A tuple containing (heightStart, heightEnd) in meters.</returns>
    /// <remarks>
    /// Mirrors the reference <c>Renderer_Rain::Draw</c>: the RainEffect's <c>Traceline start</c> is the
    /// distance the rain spawns above the camera and <c>Traceline end</c> is the positive lower distance
    /// below the camera, so the band always hangs from the sky down past the player.
    /// </remarks>
    public (float HeightStart, float HeightEnd) CalculateHeightBounds(double cameraZ)
    {
        float heightStart = (float)cameraZ + _startMeters;
        float heightEnd = (float)cameraZ - _endMeters;
        return (heightStart, heightEnd);
    }

    /// <summary>Collects the world-space shells of every authored building placement.</summary>
    /// <param name="placements">The level's placed objects, in world meters.</param>
    /// <returns>The buildings' axis-aligned world boxes; empty when the level places none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Port of the reference indoor test in <c>renderer_draw.cpp</c>, which scans the level's buildings
    /// and suppresses rain while the camera is inside one. The mesh box is computed once here rather than
    /// per frame; only buildings (the shells a player walks into) participate.
    /// </remarks>
    public static IReadOnlyList<BuildingVolume> BuildBuildingVolumes(IEnumerable<WorldPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        List<BuildingVolume> volumes = [];
        foreach (WorldPlacement placement in placements)
        {
            if (!IsBuildingPlacement(placement) || placement.Model.IsEmpty)
            {
                continue;
            }

            // The model's axis-aligned box in its own engine-unit space.
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (MefVertex vertex in placement.Model.Vertices)
            {
                minX = MathF.Min(minX, vertex.Position.X);
                minY = MathF.Min(minY, vertex.Position.Y);
                minZ = MathF.Min(minZ, vertex.Position.Z);
                maxX = MathF.Max(maxX, vertex.Position.X);
                maxY = MathF.Max(maxY, vertex.Position.Y);
                maxZ = MathF.Max(maxZ, vertex.Position.Z);
            }

            if (minX > maxX)
            {
                continue;
            }

            volumes.Add(FromLocalBounds(
                placement.Position,
                placement.Orientation,
                new Vector3f(minX, minY, minZ),
                new Vector3f(maxX, maxY, maxZ)));
        }

        return volumes;
    }

    /// <summary>Reports whether a scene placement belongs to a level-authored building shell.</summary>
    /// <remarks>
    /// The QVM task type is commonly <c>EditRigidObj</c>; the semantic building classification is carried
    /// by the task instance name (<c>Building_...</c>), which is also the convention used by the menu's
    /// building selection code. Accepting an exact <c>Building</c> name keeps custom levels compatible.
    /// </remarks>
    internal static bool IsBuildingPlacement(WorldPlacement placement) =>
        IsBuildingTaskName(placement.TaskName)
        || placement.Owner is DeclaredTask owner && IsBuildingTaskName(owner.Name);

    internal static bool IsBuildingTaskName(string name) =>
        name.StartsWith(BuildingTaskPrefix, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Building", StringComparison.OrdinalIgnoreCase);

    /// <summary>Transforms a model-local axis-aligned box into its axis-aligned world box, in meters.</summary>
    /// <param name="positionMetres">The placement's world position, in meters.</param>
    /// <param name="orientation">The placement's world orientation.</param>
    /// <param name="localMinimumUnits">The local box's lower corner, in engine units.</param>
    /// <param name="localMaximumUnits">The local box's upper corner, in engine units.</param>
    /// <returns>The world-space box the rotated shell sweeps.</returns>
    public static BuildingVolume FromLocalBounds(
        Vector3d positionMetres,
        Matrix3f orientation,
        Vector3f localMinimumUnits,
        Vector3f localMaximumUnits)
    {
        float unitsToMetres = 1.0f / (float)WorldScene.UnitsPerMetre;
        Vector3f centreLocal = ((localMinimumUnits + localMaximumUnits) * 0.5f) * unitsToMetres;
        Vector3f halfLocal = ((localMaximumUnits - localMinimumUnits) * 0.5f) * unitsToMetres;

        // Local-to-world is the column-vector product M·v — the same convention the collision index and
        // renderer use — which here means rotating the local offset by the transposed stored basis.
        Matrix3f rotation = orientation.Transposed();

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3f cornerLocal = new(
                        centreLocal.X + (sx * halfLocal.X),
                        centreLocal.Y + (sy * halfLocal.Y),
                        centreLocal.Z + (sz * halfLocal.Z));
                    Vector3f offset = rotation.Transform(cornerLocal);
                    double x = positionMetres.X + offset.X;
                    double y = positionMetres.Y + offset.Y;
                    double z = positionMetres.Z + offset.Z;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    minZ = Math.Min(minZ, z);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    maxZ = Math.Max(maxZ, z);
                }
            }
        }

        return new BuildingVolume(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ));
    }

    /// <summary>Draws falling rain particle streaks around the camera.</summary>
    /// <param name="camera">The camera position and orientation.</param>
    /// <param name="projection">The view projection matrix.</param>
    /// <param name="seconds">Total elapsed time in seconds.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsActive)
        {
            return;
        }

        (float heightStart, float heightEnd) = CalculateHeightBounds(camera.Position.Z);
        float fallRange = MathF.Max(heightStart - heightEnd, 1.0f);
        if (heightStart <= heightEnd)
        {
            return;
        }

        float boxSize = DefaultBoxSizeMeters;
        float streakLen = StreakLengthMeters;
        double camX = camera.Position.X;
        double camY = camera.Position.Y;

        // The reference uses thin GL lines; this renderer uses quads, so preserve the authored alpha
        // at a restrained scale rather than multiplying it into a solid sheet of white streaks.
        float streakAlpha = Math.Clamp(_alpha * 1.25f, 0.0f, MaximumStreakAlpha);
        Bgra32 color = new(
            (byte)(0.8f * 255f),
            (byte)(0.85f * 255f),
            (byte)(0.9f * 255f),
            (byte)(streakAlpha * 255f));

        float halfBox = boxSize * 0.5f;

        for (int i = 0; i < _numDrops; i++)
        {
            (float sx, float sy, float sz) = _seeds[i];
            float speed = CalculateFallSpeed(fallRange, sz) * _speedMultiplier;

            float z = heightStart - Mod(seconds * speed + (sy * fallRange) + (float)camera.Position.Z, fallRange);
            float cellX = Mod((sx * boxSize) - (float)camX, boxSize) - halfBox;
            float cellY = Mod((sy * boxSize) - (float)camY, boxSize) - halfBox;

            // Mesh3D's camera-relative model transform is identity when the model origin is the camera;
            // keep the streamed vertices in that same local frame instead of handing it absolute level
            // coordinates (which would be transformed as though the camera were at world origin).
            Vector3f bottomPos = new(cellX, cellY, z - (float)camera.Position.Z);
            Vector3f topPos = new(bottomPos.X, bottomPos.Y, bottomPos.Z + streakLen);
            Vector3f normal = new(0f, 0f, 1f);

            // Thin streak quad offset
            Vector3f widthOffset = new(StreakHalfWidthMeters, StreakHalfWidthMeters, 0f);

            int vIdx = i * 4;
            _vertices[vIdx] = new Vertex3D(bottomPos - widthOffset, normal, 0f, 0f, colour: color);
            _vertices[vIdx + 1] = new Vertex3D(bottomPos + widthOffset, normal, 1f, 0f, colour: color);
            _vertices[vIdx + 2] = new Vertex3D(topPos + widthOffset, normal, 1f, 1f, colour: color);
            _vertices[vIdx + 3] = new Vertex3D(topPos - widthOffset, normal, 0f, 1f, colour: color);
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(RainMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position);

        if (!_mesh.IsValid)
        {
            _mesh = _device.CreateAnimatedMesh(_vertices, _indices);
        }

        _device.UpdateAnimatedMesh(_mesh, _vertices);
        // This stream is regenerated in world space every frame; a prior skinned/particle draw must not
        // interpolate it toward an unrelated previous pose, or the streaks can shear as the camera moves.
        _device.ClearPoseInterpolation();
        try
        {
            _device.DrawMesh(
                _mesh,
                0,
                _indices.Length,
                modelView,
                TextureHandle.None,
                TextureHandle.None,
                BlendMode.AlphaBlend);
        }
        finally
        {
            _device.SetMaterialOverride(SurfaceMaterial.Default);
            _device.SetDepthState(DepthState.TestAndWrite);
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
    }

    private void GenerateSeeds()
    {
        Random rng = new(12345);
        for (int i = 0; i < _numDrops; i++)
        {
            _seeds[i] = ((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
        }
    }

    private void BuildIndices()
    {
        for (int i = 0; i < _numDrops; i++)
        {
            int v = i * 4;
            int idx = i * 6;
            _indices[idx] = (ushort)v;
            _indices[idx + 1] = (ushort)(v + 1);
            _indices[idx + 2] = (ushort)(v + 2);
            _indices[idx + 3] = (ushort)v;
            _indices[idx + 4] = (ushort)(v + 2);
            _indices[idx + 5] = (ushort)(v + 3);
        }
    }

    private static float Mod(float x, float y)
    {
        float result = x % y;
        return result < 0f ? result + y : result;
    }

    internal static float CalculateFallSpeed(float fallRange, float seed) =>
        (MinimumSpeedMultiplier + (Math.Clamp(seed, 0f, 1f) *
            (MaximumSpeedMultiplier - MinimumSpeedMultiplier))) * fallRange;

    internal static float ReadFloat(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float f ? f : fallback;
}

using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

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

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
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
    public RainRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null, int numDrops = DefaultParticleCount)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _numDrops = Math.Max(1, numDrops);

        _vertices = new Vertex3D[_numDrops * 4];
        _indices = QuadIndices.Build(_numDrops);
        _seeds = new (float X, float Y, float Z)[_numDrops];

        GenerateSeeds();
    }

    /// <summary>Gets a value indicating whether rain rendering is active and visible.</summary>
    public bool IsActive => _active && !_indoors;

    /// <summary>Gets whether rain is enabled before the indoor visibility test.</summary>
    public bool IsWeatherEnabled => _active;

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
    public void ApplySettings(bool enabled, WeatherKind kind, float speedMultiplier, bool force = false)
    {
        // Forcing skips only the level's own answer, not the player's: r_weather_enabled still turns it
        // off, and asking for snow still means no rain. A forced level keeps this renderer's default
        // band, because a level with no RainEffect task has no authored band to read.
        _active = enabled && (_authoredAvailable || force) && kind == WeatherKind.Rain;
        _speedMultiplier = Math.Clamp(speedMultiplier, 0f, 2f);
    }

    /// <summary>Recursively searches the task tree for a <c>RainEffect</c> task.</summary>
    /// <param name="task">Root task to search.</param>
    /// <returns>The <c>RainEffect</c> declared task, or <see langword="null"/> if not found.</returns>
    public static GameTask? FindRainEffect(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        // Declared from level data, or typed by a title that runs it: either answers its parameters by name.
        if (string.Equals(task.Type.Name, "RainEffect", StringComparison.Ordinal))
        {
            return task;
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
        GameTask? rainTask = FindRainEffect(levelRoot);
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
    internal static bool ReadFlag(GameTask task, string name)
    {
        if (!task.TryGetScriptValue(name, out object? raw))
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

    private static float Mod(float x, float y)
    {
        float result = x % y;
        return result < 0f ? result + y : result;
    }

    internal static float CalculateFallSpeed(float fallRange, float seed) =>
        (MinimumSpeedMultiplier + (Math.Clamp(seed, 0f, 1f) *
            (MaximumSpeedMultiplier - MinimumSpeedMultiplier))) * fallRange;

    internal static float ReadFloat(GameTask task, string name, float fallback) =>
        task.TryGetScriptValue(name, out object? value) && value is float f ? f : fallback;
}

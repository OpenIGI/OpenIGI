using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
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

    /// <summary>The default number of rain particle drops to simulate (4000).</summary>
    public const int DefaultParticleCount = 4000;

    /// <summary>The bounding volume box size in meters around the camera.</summary>
    public const float DefaultBoxSizeMeters = 50.0f;

    /// <summary>The vertical length of each rain drop streak quad in meters.</summary>
    public const float StreakLengthMeters = 0.35f;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Vertex3D[] _vertices;
    private readonly ushort[] _indices;
    private readonly (float X, float Y, float Z)[] _seeds;

    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    private bool _active;
    private bool _indoors;
    private float _startMeters = 10.0f;
    private float _endMeters = -2.0f;
    private float _alpha = 0.5f;
    private readonly int _numDrops;

    /// <summary>Initializes a new instance of the <see cref="RainRenderer"/> class.</summary>
    /// <param name="device">The graphics device to draw with.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="numDrops">Number of rain particle drops to simulate (default 4000).</param>
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

    /// <summary>Gets the rain height end in meters below camera.</summary>
    public float EndMeters => _endMeters;

    /// <summary>Gets the rain alpha transparency level.</summary>
    public float Alpha => _alpha;

    /// <summary>Gets the total simulated particle drop count.</summary>
    public int DropCount => _numDrops;

    /// <summary>Configures the rain particle renderer parameters.</summary>
    /// <param name="active">Whether rain rendering is active.</param>
    /// <param name="startMeters">Height above ground in meters where rain begins falling.</param>
    /// <param name="endMeters">Height above ground in meters where rain disappears.</param>
    /// <param name="alpha">Alpha transparency factor for rain streaks.</param>
    public void SetParams(bool active, float startMeters, float endMeters, float alpha)
    {
        _active = active;
        _startMeters = startMeters;
        _endMeters = endMeters;
        _alpha = alpha;
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
    /// <returns><see langword="true"/> if a <c>RainEffect</c> task was found and parsed; otherwise <see langword="false"/>.</returns>
    public bool ReadParams(GameTask levelRoot)
    {
        DeclaredTask? rainTask = FindRainEffect(levelRoot);
        if (rainTask is null)
        {
            _active = false;
            return false;
        }

        float start = GetFloat(rainTask, "Traceline start", 10.0f);
        float end = GetFloat(rainTask, "Traceline end", -2.0f);
        float alpha = GetFloat(rainTask, "Rain Alpha", 0.13f);

        SetParams(active: true, startMeters: start, endMeters: end, alpha: alpha);
        _logger.Information(LogCategory, $"RainEffect configured: start={_startMeters}m, end={_endMeters}m, alpha={_alpha}.");
        return true;
    }

    /// <summary>Calculates the top and bottom Z world coordinates for rain particle bounds around camera Z.</summary>
    /// <param name="cameraZ">Camera Z world position in meters.</param>
    /// <returns>A tuple containing (heightStart, heightEnd) in meters.</returns>
    public (float HeightStart, float HeightEnd) CalculateHeightBounds(double cameraZ)
    {
        float heightStart = (float)cameraZ + _startMeters;
        float heightEnd = (float)cameraZ + _endMeters;
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

        // Boost rain alpha into a clearly visible streak while scaling by level's authored alpha
        float streakAlpha = Math.Clamp(_alpha * 4.0f, 0.0f, 0.85f);
        Bgra32 color = new(
            (byte)(0.9f * 255f),
            (byte)(0.85f * 255f),
            (byte)(0.8f * 255f),
            (byte)(streakAlpha * 255f));

        float halfBox = boxSize * 0.5f;

        for (int i = 0; i < _numDrops; i++)
        {
            (float sx, float sy, float sz) = _seeds[i];
            float speed = (0.6f + (sz * 0.8f)) * fallRange;

            float z = heightStart - Mod(seconds * speed + (sy * fallRange) + (float)camera.Position.Z, fallRange);
            float cellX = Mod((sx * boxSize) - (float)camX, boxSize) - halfBox;
            float cellY = Mod((sy * boxSize) - (float)camY, boxSize) - halfBox;

            Vector3f bottomPos = new((float)(camX + cellX), (float)(camY + cellY), z);
            Vector3f topPos = new(bottomPos.X, bottomPos.Y, z + streakLen);
            Vector3f normal = new(0f, 0f, 1f);

            // Thin streak quad offset
            float halfWidth = 0.015f;
            Vector3f widthOffset = new(halfWidth, halfWidth, 0f);

            int vIdx = i * 4;
            _vertices[vIdx] = new Vertex3D(bottomPos - widthOffset, normal, 0f, 0f, colour: color);
            _vertices[vIdx + 1] = new Vertex3D(bottomPos + widthOffset, normal, 1f, 0f, colour: color);
            _vertices[vIdx + 2] = new Vertex3D(topPos + widthOffset, normal, 1f, 1f, colour: color);
            _vertices[vIdx + 3] = new Vertex3D(topPos - widthOffset, normal, 0f, 1f, colour: color);
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);

        Matrix4f modelView = camera.CreateModelView(camera.Position);

        if (!_mesh.IsValid)
        {
            _mesh = _device.CreateAnimatedMesh(_vertices, _indices);
        }

        _device.UpdateAnimatedMesh(_mesh, _vertices);
        _device.DrawMesh(_mesh, 0, _indices.Length, modelView, TextureHandle.None, TextureHandle.None, BlendMode.AlphaBlend);
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

    private static float GetFloat(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float f ? f : fallback;
}

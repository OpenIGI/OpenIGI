using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Renders small, slowly falling snow flakes for an authored snow weather task.</summary>
/// <remarks>
/// Retail levels reuse <c>RainEffect</c> as the weather descriptor. A task with <c>Is Rain=FALSE</c>
/// and <c>Is Active=TRUE</c> is snowfall; this keeps the decision data-driven and also preserves the
/// authored traceline band and alpha from <c>objects.qvm</c>.
/// </remarks>
public sealed class SnowRenderer : IDisposable
{
    private const string LogCategory = "Snow";
    private const int DefaultParticleCount = 900;
    private const float BoxSizeMeters = 50f;
    private const float FlakeSizeMeters = 0.045f;
    private const float MinimumSpeedMultiplier = 0.025f;
    private const float MaximumSpeedMultiplier = 0.065f;
    private const float MaximumAlpha = 0.42f;
    private const float DriftMeters = 0.35f;

    private static readonly SurfaceMaterial SnowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Vertex3D[] _vertices;
    private readonly ushort[] _indices;
    private readonly (float X, float Y, float Z)[] _seeds;
    private readonly int _particleCount;

    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;
    private bool _active;
    private bool _authoredAvailable;
    private float _speedMultiplier = 1f;
    private bool _indoors;
    private float _startMeters = 10f;
    private float _endMeters = 2f;
    private float _alpha = 0.25f;

    /// <summary>Initializes the snowfall renderer.</summary>
    public SnowRenderer(IGraphicsDevice device, IIgiLogger? logger = null, int particleCount = DefaultParticleCount)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _particleCount = Math.Clamp(particleCount, 1, ushort.MaxValue / 4);
        _vertices = new Vertex3D[_particleCount * 4];
        _indices = new ushort[_particleCount * 6];
        _seeds = new (float X, float Y, float Z)[_particleCount];

        Random random = new(271828);
        for (int i = 0; i < _particleCount; i++)
        {
            _seeds[i] = ((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            int vertex = i * 4;
            int index = i * 6;
            _indices[index] = (ushort)vertex;
            _indices[index + 1] = (ushort)(vertex + 1);
            _indices[index + 2] = (ushort)(vertex + 2);
            _indices[index + 3] = (ushort)vertex;
            _indices[index + 4] = (ushort)(vertex + 2);
            _indices[index + 5] = (ushort)(vertex + 3);
        }
    }

    /// <summary>Gets whether snow is enabled and visible outdoors.</summary>
    public bool IsActive => _active && !_indoors;

    /// <summary>Gets or sets whether the camera is inside a building.</summary>
    public bool Indoors
    {
        get => _indoors;
        set => _indoors = value;
    }

    /// <summary>Gets the authored upper traceline distance.</summary>
    public float StartMeters => _startMeters;

    /// <summary>Gets the authored lower traceline distance.</summary>
    public float EndMeters => _endMeters;

    /// <summary>Gets the authored snowfall alpha.</summary>
    public float Alpha => _alpha;

    /// <summary>Gets the number of simulated flakes.</summary>
    public int ParticleCount => _particleCount;

    /// <summary>Gets the user-selected speed multiplier.</summary>
    public float SpeedMultiplier => _speedMultiplier;

    /// <summary>Reads the retail RainEffect weather descriptor and enables snow when rain is false.</summary>
    public bool ReadParams(GameTask levelRoot)
    {
        DeclaredTask? task = RainRenderer.FindRainEffect(levelRoot);
        if (task is null)
        {
            _authoredAvailable = false;
            _active = false;
            return false;
        }

        bool isRain = RainRenderer.ReadFlag(task, "Is Rain");
        bool isActive = RainRenderer.ReadFlag(task, "Is Active");
        _authoredAvailable = isActive;
        _startMeters = RainRenderer.ReadFloat(task, "Traceline start", _startMeters);
        _endMeters = RainRenderer.ReadFloat(task, "Traceline end", _endMeters);
        _alpha = RainRenderer.ReadFloat(task, "Rain Alpha", _alpha);
        _active = !isRain && isActive;

        _logger.Information(
            LogCategory,
            _active
                ? $"Snow weather configured: start={_startMeters}m, end={_endMeters}m, alpha={_alpha}."
                : $"Snow weather not active (Is Rain={isRain}, Is Active={isActive}).");
        return _active;
    }

    /// <summary>Applies the live weather settings without changing the level-authored particle band.</summary>
    public void ApplySettings(bool enabled, WeatherKind kind, float speedMultiplier)
    {
        _active = enabled && _authoredAvailable && kind == WeatherKind.Snow;
        _speedMultiplier = Math.Clamp(speedMultiplier, 0f, 2f);
    }

    /// <summary>Draws small flakes in a camera-relative volume.</summary>
    public void Draw(in Camera3D camera, in Matrix4f projection, float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
        {
            return;
        }

        float heightStart = (float)camera.Position.Z + _startMeters;
        float heightEnd = (float)camera.Position.Z - _endMeters;
        float fallRange = MathF.Max(heightStart - heightEnd, 1f);
        if (heightStart <= heightEnd)
        {
            return;
        }

        float halfBox = BoxSizeMeters * 0.5f;
        // Snow is rendered over white terrain in level 7; retain the authored ordering while keeping
        // its smallest retail alpha visible instead of disappearing into the ground colour.
        float alpha = Math.Clamp(_alpha * 1.75f, 0.10f, MaximumAlpha);
        Bgra32 color = new(
            (byte)(0.92f * 255f),
            (byte)(0.97f * 255f),
            255,
            (byte)(alpha * 255f));

        for (int i = 0; i < _particleCount; i++)
        {
            (float sx, float sy, float sz) = _seeds[i];
            float speed = CalculateFallSpeed(fallRange, sz) * _speedMultiplier;
            float z = heightStart - Mod(seconds * speed + (sz * fallRange), fallRange);
            float drift = MathF.Sin((seconds * 0.45f) + (sx * 17f)) * DriftMeters;
            float cellX = Mod((sx * BoxSizeMeters) - (float)camera.Position.X, BoxSizeMeters) - halfBox + drift;
            float cellY = Mod((sy * BoxSizeMeters) - (float)camera.Position.Y, BoxSizeMeters) - halfBox
                + (MathF.Cos((seconds * 0.35f) + (sy * 19f)) * DriftMeters);

            Vector3f centre = new(cellX, cellY, z - (float)camera.Position.Z);
            Vector3f half = new(FlakeSizeMeters, FlakeSizeMeters, 0f);
            Vector3f bottom = new(centre.X - half.X, centre.Y - half.Y, centre.Z - FlakeSizeMeters);
            Vector3f top = new(centre.X + half.X, centre.Y + half.Y, centre.Z + FlakeSizeMeters);
            Vector3f normal = new(0f, 0f, 1f);
            int vertex = i * 4;
            _vertices[vertex] = new Vertex3D(bottom, normal, 0f, 0f, colour: color);
            _vertices[vertex + 1] = new Vertex3D(new Vector3f(top.X, bottom.Y, top.Z), normal, 1f, 0f, colour: color);
            _vertices[vertex + 2] = new Vertex3D(top, normal, 1f, 1f, colour: color);
            _vertices[vertex + 3] = new Vertex3D(new Vector3f(bottom.X, top.Y, bottom.Z), normal, 0f, 1f, colour: color);
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(SnowMaterial);
        if (!_mesh.IsValid)
        {
            _mesh = _device.CreateAnimatedMesh(_vertices, _indices);
        }

        _device.UpdateAnimatedMesh(_mesh, _vertices);
        _device.ClearPoseInterpolation();
        try
        {
            _device.DrawMesh(
                _mesh,
                0,
                _indices.Length,
                camera.CreateModelView(camera.Position),
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

    /// <inheritdoc />
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

    internal static float CalculateFallSpeed(float fallRange, float seed) =>
        (MinimumSpeedMultiplier + (Math.Clamp(seed, 0f, 1f) *
            (MaximumSpeedMultiplier - MinimumSpeedMultiplier))) * fallRange;

    private static float Mod(float value, float modulus)
    {
        float result = value % modulus;
        return result < 0f ? result + modulus : result;
    }
}

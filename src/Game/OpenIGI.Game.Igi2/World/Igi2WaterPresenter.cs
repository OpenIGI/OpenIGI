using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>The light a level's water is drawn in: its sun and the objects' ambient.</summary>
/// <param name="SunDirection">Towards the sun, unit length; zero where the level has none.</param>
/// <param name="SunColour">The sun's front colour; black where the level has none.</param>
/// <param name="Ambient">The objects' ambient light, <c>Mesh3D_atAmbientColor[1]</c>.</param>
public readonly record struct Igi2WaterLight(Vector3f SunDirection, Vector3f SunColour, Vector3f Ambient);

/// <summary>
/// Draws I.G.I. 2's water: a moving surface per sheet, reflecting the level's baked cube map.
/// </summary>
/// <remarks>
/// <para>
/// The surface itself is exact. Every vertex is <see cref="Igi2WaterWaveField"/>, which is
/// <c>Water_VertexFunction</c> (<c>igi2ed 0x645B30</c>) read term for term: the sum of the sheet's wave
/// trains, flattened with the vertex's depth in front of the eye, with the normal differenced from two
/// neighbouring samples a hundredth of a wavelength away. The vertex the original builds is seven floats — a
/// position, that normal, and how shallow the water is there — which is the same vertex this writes.
/// </para>
/// <para>
/// <b>A sheet is a square.</b> <c>Water_IsUnderWater</c> (<c>0x6477D0</c>) tests X and Y against
/// <c>Size</c> independently rather than testing a distance, so what <c>Size</c> describes is a
/// half-extent.
/// </para>
/// <para>
/// <b>It is cut up again every frame, around the eye</b>, by <see cref="Igi2WaterTessellation"/>, which is
/// <c>WaterRender_Draw</c>'s subdivision. That matters for more than the shape: the water's light is computed
/// at its vertices, so a sheet kilometres wide cut into an even grid shows every vertex as far away, and reads
/// as a mirror to the bank.
/// </para>
/// <para>
/// <b>It is drawn as the original's "High Water Quality".</b> <see cref="IGraphicsDevice.DrawWater"/> is
/// <c>WaterRender_azVertexShader[2]</c> and its two texture stages: a Fresnel blend of the cube and the
/// water's own light, the scene behind kept by how shallow the water is, and no surface texture at all.
/// </para>
/// </remarks>
public sealed class Igi2WaterPresenter : IWaterPresenter
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.water";

    // A sheet's mesh is refilled every frame, so it is three: one being written while the device may still be
    // drawing the two before it.
    private const int MeshRing = 3;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly Func<int, int>? _envmapSlot;
    private readonly Func<double, double, double?>? _ground;
    private readonly Igi2WaterLight _light;
    private readonly Igi2WaterTessellation _tessellation = new();
    private readonly Sheet[] _sheets;
    private bool _disposed;

    /// <summary>Builds one level's water.</summary>
    /// <param name="device">The device to upload through and draw with.</param>
    /// <param name="surface">The sheets the level places.</param>
    /// <param name="cubes">Resolves a sheet's baked reflections to cube maps, one a slot of its resource list; empty for none.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <param name="envmapSlot">The slot a water task reflects now, by its id; the first without.</param>
    /// <param name="ground">The ground's height under a world point, in world units, as the water's depth reads it; none for no ground.</param>
    /// <param name="light">The level's sun and ambient.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2WaterPresenter(
        IGraphicsDevice device,
        Igi2WaterSurface surface,
        Func<Igi2WaterSheet, IReadOnlyList<TextureHandle>>? cubes = null,
        IOpenIgiLogger? logger = null,
        Func<int, int>? envmapSlot = null,
        Func<double, double, double?>? ground = null,
        Igi2WaterLight light = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _envmapSlot = envmapSlot;
        _ground = ground;
        _light = light;
        _sheets = [.. surface.Sheets.Select(sheet => new Sheet(sheet, Igi2WaterWaveField.For(sheet), cubes?.Invoke(sheet) ?? []))];
        _logger.Information(
            LogCategory,
            $"Water: {_sheets.Length} sheet(s), {_sheets.Count(static s => s.Cube.IsValid)} reflecting a baked cube.");
    }

    /// <inheritdoc/>
    public int SheetCount => _sheets.Length;

    /// <inheritdoc/>
    public string Summary => ToString();

    /// <summary>How shallow the water is over a point, the vertex's delta (<c>Water_VertexFunction</c>).</summary>
    /// <param name="surface">The surface's world height there, in world units.</param>
    /// <param name="ground">The ground's, or <see langword="null"/> where there is none.</param>
    /// <param name="absorptionLength">The sheet's absorption length, in metres.</param>
    /// <returns>One at the ground, falling to nought an absorption length from it, and nought beyond.</returns>
    public static float ShallownessAt(double surface, double? ground, float absorptionLength)
    {
        if (ground is not { } floor)
        {
            return 0f;
        }

        double depth = Math.Abs(surface - floor);
        double reach = absorptionLength * WorldScene.UnitsPerMetre;
        return depth <= reach && reach > 0.0 ? (float)(1.0 - (depth / reach)) : 0f;
    }

    /// <inheritdoc/>
    public void Draw(in WaterFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sheets.Length == 0)
        {
            return;
        }

        _device.SetProjection(frame.Projection);
        foreach (Sheet sheet in _sheets)
        {
            Draw(sheet, frame);
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
        foreach (Sheet sheet in _sheets)
        {
            sheet.Release(_device);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 water, {_sheets.Length} sheet(s)";

    private void Draw(Sheet sheet, in WaterFrame frame)
    {
        // The eye in the sheet's own space: the frame's camera is in metres, the sheet's waves in engine units.
        Igi2WaterSheet definition = sheet.Definition;
        Vector3d centre = WorldScene.ToMetres(definition.Centre);
        Vector3f eye = new(
            (float)(frame.Camera.Position.X - centre.X),
            (float)(frame.Camera.Position.Y - centre.Y),
            (float)(frame.Camera.Position.Z - centre.Z));
        Vector3f eyeUnits = eye * (float)WorldScene.UnitsPerMetre;

        _tessellation.Build(definition, sheet.Field, frame.Seconds, eyeUnits, frame.Camera.Orientation, frame.Projection, _ground);

        MeshHandle mesh;
        try
        {
            mesh = sheet.NextMesh(_device);
            _device.UploadMeshRange(mesh, 0, _tessellation.Vertices, 0, _tessellation.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"A water sheet did not upload: {ex.Message}");
            return;
        }

        // WaterRender_Draw: the eye is above the water when the surface over it, at full height, is below it.
        bool above = sheet.Field.HeightAt(eyeUnits.X, eyeUnits.Y, frame.Seconds) < eyeUnits.Z;

        WaterShading shading = new(
            eye,
            _light.SunDirection,
            _light.SunColour,
            _light.Ambient,
            definition.Colour,
            definition.Specular,
            definition.MaxReflectionAngle,
            above);

        // Water_EnterHandler reflects the slot its index expression chose; one that did not load falls back to the
        // first, and a sheet with none is not drawn.
        _device.DrawWater(
            mesh,
            0,
            _tessellation.IndexCount,
            // In metres, like the ground the sheet lies on: a Water task's Position is authored in
            // engine units and the frame's camera is not.
            frame.Camera.CreateModelView(centre, Matrix3f.Identity),
            sheet.CubeFor(_envmapSlot?.Invoke(definition.TaskId) ?? 0),
            shading);
    }

    /// <summary>One sheet: its waves, its reflections, and the meshes it is drawn from in turn.</summary>
    private sealed class Sheet(Igi2WaterSheet definition, Igi2WaterWaveField field, IReadOnlyList<TextureHandle> cubes)
    {
        private readonly TextureHandle[] _cubes = [.. cubes];
        private readonly MeshHandle[] _meshes = [.. Enumerable.Repeat(MeshHandle.None, MeshRing)];
        private int _next;

        public Igi2WaterSheet Definition { get; } = definition;

        public Igi2WaterWaveField Field { get; } = field;

        public TextureHandle Cube => _cubes.Length > 0 ? _cubes[0] : TextureHandle.None;

        public TextureHandle CubeFor(int slot) =>
            slot >= 0 && slot < _cubes.Length && _cubes[slot].IsValid ? _cubes[slot] : Cube;

        public MeshHandle NextMesh(IGraphicsDevice device)
        {
            _next = (_next + 1) % MeshRing;
            if (!_meshes[_next].IsValid)
            {
                _meshes[_next] = device.CreateMeshStorage(Igi2WaterTessellation.MaximumVertices, Igi2WaterTessellation.MaximumIndices);
            }

            return _meshes[_next];
        }

        public void Release(IGraphicsDevice device)
        {
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i].IsValid)
                {
                    device.DestroyMesh(_meshes[i]);
                    _meshes[i] = MeshHandle.None;
                }
            }
        }
    }
}

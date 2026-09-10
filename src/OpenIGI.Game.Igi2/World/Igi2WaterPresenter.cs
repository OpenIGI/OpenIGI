using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Draws I.G.I. 2's water: a moving surface per sheet, reflecting the level's baked cube map.
/// </summary>
/// <remarks>
/// <para>
/// The surface itself is exact. Every vertex is <see cref="Igi2WaterWaveField"/>, which is
/// <c>Water_VertexFunction</c> (<c>igi2ed 0x645B30</c>) read term for term: the sum of the sheet's wave
/// trains, attenuated outwards, with the normal differenced from two neighbouring samples a hundredth
/// of a wavelength away. The vertex the original builds is seven floats — a position, that normal, and
/// a shore fade — which is the same vertex this writes.
/// </para>
/// <para>
/// <b>A sheet is a square.</b> <c>Water_IsUnderWater</c> (<c>0x6477D0</c>) tests X and Y against
/// <c>Size</c> independently rather than testing a distance, so what <c>Size</c> describes is a
/// half-extent and the grid below covers a square of twice it.
/// </para>
/// <para>
/// <b>What is not the original's is how finely it is cut up.</b> <c>WaterRender_Draw</c>
/// (<c>0x577950</c>) is a stack-driven adaptive subdivision — the same shape as the terrain's, down to
/// morphing a vertex toward the midpoint of its two neighbours as an edge splits, which
/// <c>WaterRender_CalculateRenderVertex</c> (<c>0x579420</c>) does in its first ten lines. That walk is
/// forty-five kilobytes of decompilation and is not ported. The grid here is uniform, and its spacing is
/// chosen from the data rather than picked: <see cref="SamplesPerWavelength"/> samples across the
/// <em>shortest</em> wave the sheet carries, which is what it takes to represent that wave at all. A
/// sheet whose waves are long is cut coarsely and a sheet with a short one finely, and neither is a
/// number anyone chose by eye.
/// </para>
/// <para>
/// <b>The shore fade is not wired.</b> The seventh float is
/// <c>1 - |surface - ground| / (Absorption length × 4096)</c>, which needs the terrain's height at each
/// vertex; this pass is handed the level and the registry and not the ground. Every vertex therefore
/// carries a fade of one, which is the value the original itself uses below quality zero — so the water
/// is opaque to its own edge instead of thinning where it meets the bank.
/// </para>
/// </remarks>
public sealed class Igi2WaterPresenter : IWaterPresenter
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.water";

    /// <summary>How many vertices the grid puts across the shortest wave a sheet carries.</summary>
    /// <remarks>
    /// Eight. Four is the theoretical floor for a sine and looks like a sawtooth; eight is where the
    /// crest stops being visibly faceted. This is the one number here chosen rather than read, and it is
    /// a sampling rate rather than a fact about the game — the original picks its own per frame, from
    /// the camera.
    /// </remarks>
    public const int SamplesPerWavelength = 8;

    /// <summary>The most vertices the grid puts along one edge of a sheet.</summary>
    /// <remarks>
    /// 129, which squares to 16,641 — comfortably inside the 16-bit indices a mesh is built with, with
    /// room for the sheet that is bigger than any shipped one.
    /// </remarks>
    public const int MaximumEdgeVertices = 129;

    /// <summary>The fewest, so a still sheet is still a surface.</summary>
    public const int MinimumEdgeVertices = 2;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Sheet[] _sheets;
    private bool _disposed;

    /// <summary>Builds one level's water.</summary>
    /// <param name="device">The device to upload through and draw with.</param>
    /// <param name="surface">The sheets the level places.</param>
    /// <param name="textures">Resolves a sheet's texture to a loaded texture.</param>
    /// <param name="cubes">Resolves a sheet's baked reflection to a cube map, or returns the null handle.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2WaterPresenter(
        IGraphicsDevice device,
        Igi2WaterSurface surface,
        Func<string, TextureHandle> textures,
        Func<Igi2WaterSheet, TextureHandle>? cubes = null,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        _logger = logger ?? NullLogger.Instance;

        List<Sheet> sheets = [];
        long vertices = 0;
        foreach (Igi2WaterSheet sheet in surface.Sheets)
        {
            Sheet built = new(
                sheet,
                Igi2WaterWaveField.For(sheet),
                EdgeVerticesFor(sheet),
                textures(sheet.Texture),
                cubes?.Invoke(sheet) ?? TextureHandle.None);

            sheets.Add(built);
            vertices += built.VertexCount;
        }

        _sheets = [.. sheets];
        _logger.Information(
            LogCategory,
            $"Water: {_sheets.Length} sheet(s), {vertices} vertices, "
                + $"{_sheets.Count(static s => s.Cube.IsValid)} reflecting a baked cube.");
    }

    /// <inheritdoc/>
    public int SheetCount => _sheets.Length;

    /// <inheritdoc/>
    public string Summary => ToString();

    /// <summary>Gets how many vertices the whole level's water is cut into.</summary>
    public int VertexCount => _sheets.Sum(static s => s.VertexCount);

    /// <summary>Gets how many vertices one sheet's grid puts along each edge.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The count, between <see cref="MinimumEdgeVertices"/> and <see cref="MaximumEdgeVertices"/>.</returns>
    /// <remarks>
    /// Enough to put <see cref="SamplesPerWavelength"/> vertices across the shortest wave the sheet
    /// carries. A sheet with no waves is flat and takes the minimum, which is the four corners.
    /// </remarks>
    public static int EdgeVerticesFor(Igi2WaterSheet sheet)
    {
        float shortest = float.MaxValue;
        foreach (Igi2WaterWave wave in sheet.Waves)
        {
            // Frequency is a wavelength in metres, and a train with no length and no height is not a
            // wave -- see Igi2WaterWaveTrain, where the engine divides by this number.
            if (wave.Frequency > 0f && wave.Amplitude != 0f)
            {
                shortest = MathF.Min(shortest, wave.Frequency);
            }
        }

        if (shortest == float.MaxValue)
        {
            return MinimumEdgeVertices;
        }

        double spacing = shortest * WorldScene.UnitsPerMetre / SamplesPerWavelength;
        double span = sheet.Radius * 2.0;
        int edge = (int)Math.Ceiling(span / Math.Max(spacing, 1.0)) + 1;
        return Math.Clamp(edge, MinimumEdgeVertices, MaximumEdgeVertices);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Depth tested but not written, and both facings drawn: water is translucent, so it sorts against
    /// the world without occluding itself, and a camera under the surface has to see it from below.
    /// </remarks>
    public void Draw(in WaterFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sheets.Length == 0)
        {
            return;
        }

        _device.SetProjection(frame.Projection);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetCullMode(CullMode.None);

        foreach (Sheet sheet in _sheets)
        {
            Draw(sheet, frame);
        }

        // Cleared afterwards: a cube left bound would have the next thing drawn reflecting this sheet's
        // sky, and the next thing drawn is not water.
        _device.SetEnvironmentCube(TextureHandle.None);
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
    public override string ToString() =>
        $"I.G.I. 2 water, {_sheets.Length} sheet(s), {VertexCount} vertices";

    private void Draw(Sheet sheet, in WaterFrame frame)
    {
        sheet.Fill(frame.Seconds);

        try
        {
            sheet.Upload(_device);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"A water sheet did not upload: {ex.Message}");
            return;
        }

        // The sheet's own reflection, or none — which leaves the draw unreflective rather than
        // reflecting whatever the last sheet used.
        _device.SetEnvironmentCube(sheet.Cube);
        _device.DrawMesh(
            sheet.Mesh,
            0,
            sheet.IndexCount,
            frame.Camera.CreateModelView(sheet.Definition.Centre, Matrix3f.Identity),
            sheet.Texture,
            TextureHandle.None,
            BlendMode.AlphaBlend);
    }

    /// <summary>One sheet: its grid, the mesh behind it, and what it is drawn with.</summary>
    private sealed class Sheet
    {
        private readonly Igi2WaterWaveField _field;
        private readonly int _edge;
        private readonly Vertex3D[] _vertices;
        private readonly ushort[] _indices;
        private readonly float _step;
        private readonly float _origin;
        private readonly float _uvScale;
        private readonly Bgra32 _colour;

        public Sheet(
            Igi2WaterSheet definition,
            Igi2WaterWaveField field,
            int edge,
            TextureHandle texture,
            TextureHandle cube)
        {
            Definition = definition;
            Texture = texture;
            Cube = cube;
            _field = field;
            _edge = edge;
            _vertices = new Vertex3D[edge * edge];
            _indices = BuildIndices(edge);

            _origin = -(float)definition.Radius;
            _step = (float)(definition.Radius * 2.0) / (edge - 1);

            // water[212] = 1 / (UV Scale * 4096): the tiling, expressed as repeats per engine unit. A
            // sheet that authored nought would divide by nothing, so it tiles once across itself.
            float tile = definition.UvScale * (float)WorldScene.UnitsPerMetre;
            _uvScale = tile > 0f ? 1f / tile : 1f / MathF.Max((float)definition.Radius * 2f, 1f);

            _colour = new Bgra32(
                Channel(definition.Colour.Z),
                Channel(definition.Colour.Y),
                Channel(definition.Colour.X),
                Channel(definition.Alpha));
        }

        public Igi2WaterSheet Definition { get; }

        public TextureHandle Texture { get; }

        public TextureHandle Cube { get; }

        public MeshHandle Mesh { get; private set; } = MeshHandle.None;

        public int VertexCount => _vertices.Length;

        public int IndexCount => _indices.Length;

        public void Fill(double seconds)
        {
            for (int row = 0; row < _edge; row++)
            {
                float y = _origin + (row * _step);
                for (int column = 0; column < _edge; column++)
                {
                    float x = _origin + (column * _step);
                    float radius = MathF.Sqrt((x * x) + (y * y));

                    float z = _field.HeightAt(x, y, seconds, radius);
                    Vector3f normal = _field.NormalAt(x, y, seconds, radius);

                    _vertices[(row * _edge) + column] = new Vertex3D(
                        new Vector3f(x, y, z),
                        normal,
                        x * _uvScale,
                        y * _uvScale,
                        0f,
                        0f,
                        _colour);
                }
            }
        }

        public void Upload(IGraphicsDevice device)
        {
            if (Mesh.IsValid)
            {
                device.UpdateAnimatedMesh(Mesh, _vertices);
                return;
            }

            Mesh = device.CreateAnimatedMesh(_vertices, _indices);
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

        private static ushort[] BuildIndices(int edge)
        {
            ushort[] indices = new ushort[(edge - 1) * (edge - 1) * 6];
            int cursor = 0;

            for (int row = 0; row < edge - 1; row++)
            {
                for (int column = 0; column < edge - 1; column++)
                {
                    int a = (row * edge) + column;
                    int b = a + 1;
                    int c = a + edge;
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

        private static byte Channel(float value) =>
            (byte)(value <= 0f ? 0 : value > 1f ? 255 : (int)(value * 255f));
    }
}

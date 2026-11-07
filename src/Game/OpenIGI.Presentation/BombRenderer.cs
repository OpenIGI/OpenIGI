using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>
/// Draws the bomb the bomb modes play for where it lies -- planted on the ground at a site, or dropped where its carrier
/// fell -- from the model built into the game (<see cref="BombKit"/>), either title alike.
/// </summary>
/// <remarks>
/// The bomb lies flat on the ground, as I.G.I. 2 lays one it finds no surface in front of the planter for: at the planter's
/// feet, turned the way the planter faced. It is never put on a wall or an object. The finest model is drawn near, the
/// coarser ones farther off, as the original swaps a model's details by distance.
/// </remarks>
/// <param name="device">The device drawn through.</param>
/// <param name="logger">Where problems loading the bomb go.</param>
public sealed class BombRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null) : IDisposable
{
    private const string LogCategory = "Bomb";

    // How far, in metres, each detail is drawn to; past the last the bomb is too small to see.
    private static readonly double[] DetailMetres = [12, 30, 60, 120, 250];

    private readonly IOpenIgiLogger _logger = logger ?? NullLogger.Instance;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private Detail[]? _details;

    // The kit's other models -- the charge carried, in a hand, being set down -- each built the first time it is drawn.
    private readonly Dictionary<string, Detail?> _loose = new(StringComparer.OrdinalIgnoreCase);

    // Where the carried charge rides in its bone's frame, in metres: behind the chest, as a pack on the back.
    private static readonly Vector3f CarriedOffsetMetres = new(0f, -0.17f, -0.02f);
    private bool _disposed;

    /// <summary>Gets or sets the level's light the bomb is lit by, as any loose model is.</summary>
    public FixedFunctionSun? Sun { get; set; }

    /// <summary>Gets the number of bombs the last frame drew.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Draws the bombs.</summary>
    /// <param name="bombs">Each bomb: where it lies (on the ground, in world units) and the way it is turned, in radians.</param>
    /// <param name="camera">The eye, placed in metres.</param>
    /// <param name="projection">The projection.</param>
    public void Draw(IReadOnlyList<(Vector3d At, float Yaw)> bombs, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(bombs);
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastDrawCount = 0;
        if (bombs.Count == 0)
        {
            return;
        }

        _details ??= Load();
        if (_details.Length == 0)
        {
            return;
        }

        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        device.SetDepthState(DepthState.TestAndWrite);
        device.SetMaterialOverride(SurfaceMaterial.Default);
        foreach ((Vector3d at, float yaw) in bombs)
        {
            Vector3d metres = at / WorldUnits.PerMetre;
            double away = Vector3d.Distance(metres, camera.Position);
            int detail = Array.FindIndex(DetailMetres, d => away <= d);
            if (detail < 0)
            {
                continue;
            }

            DrawDetail(_details[Math.Min(detail, _details.Length - 1)], metres, Matrix3f.RotationZ(yaw), camera);
            LastDrawCount++;
        }

        device.SetVertexSun(Vector3f.Zero);
    }

    /// <summary>Draws the charge carried on a player's back.</summary>
    /// <param name="chest">The carrier's chest bone as it is drawn this frame.</param>
    /// <param name="camera">The eye, placed in metres.</param>
    /// <param name="projection">The projection.</param>
    public void DrawCarried(BoneAttachment chest, in Camera3D camera, in Matrix4f projection)
    {
        // The bone's own frame, the palette's M . v as a held gun's part is placed.
        Vector3f offset = chest.Rotation.Transposed().Transform(CarriedOffsetMetres * (float)WorldUnits.PerMetre);
        DrawModel(BombKit.HeldModel, new BoneAttachment(chest.Position + new Vector3d(offset.X, offset.Y, offset.Z), chest.Rotation), camera, projection);
    }

    /// <summary>Draws one of the kit's models where it stands.</summary>
    /// <param name="model">The model, such as <see cref="BombKit.HeldModel"/>.</param>
    /// <param name="at">Where, in world units, and how it is turned.</param>
    /// <param name="camera">The eye, placed in metres.</param>
    /// <param name="projection">The projection.</param>
    public void DrawModel(string model, BoneAttachment at, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loose.TryGetValue(model, out Detail? drawn))
        {
            Detail[] loaded = Load([model]);
            drawn = loaded.Length > 0 ? loaded[0] : null;
            _loose[model] = drawn;
        }

        Vector3d metres = at.Position / WorldUnits.PerMetre;
        if (drawn is null || Vector3d.Distance(metres, camera.Position) > DetailMetres[^1])
        {
            return;
        }

        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        device.SetDepthState(DepthState.TestAndWrite);
        device.SetMaterialOverride(SurfaceMaterial.Default);
        DrawDetail(drawn, metres, at.Rotation, camera);
        device.SetVertexSun(Vector3f.Zero);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Detail detail in (Detail[])[.. _details ?? [], .. _loose.Values.OfType<Detail>()])
        {
            device.DestroyMesh(detail.Mesh);
        }

        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture.IsValid)
            {
                device.DestroyTexture(texture);
            }
        }

        _details = null;
        _loose.Clear();
        _textures.Clear();
    }

    // One model, lit and textured from the kit, at a place and a turn.
    private void DrawDetail(Detail drawn, Vector3d metres, Matrix3f orientation, in Camera3D camera)
    {
        device.SetVertexSun(Sun is { } sun ? orientation.Transform(sun.Direction) : Vector3f.Zero);
        Matrix4f modelView = camera.CreateModelView(metres, orientation);
        foreach (MeshSubset subset in drawn.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = subset.HasMaterial && BombKit.TextureOf(drawn.Model, subset.MaterialSlot) is { } texture
                ? Texture(texture)
                : TextureHandle.None;
            device.DrawMesh(drawn.Mesh, subset.IndexOffset, subset.IndexCount, modelView, diffuse, TextureHandle.None, BlendMode.Opaque);
        }
    }

    // Models of the kit, built and uploaded once -- the planted bomb and its coarser details unless told others; none that
    // cannot be read.
    private Detail[] Load(string[]? only = null)
    {
        List<Detail> details = [];
        foreach (string model in only ?? [BombKit.PlantedModel, .. BombKit.PlantedDetails])
        {
            try
            {
                if (BombKit.Model(model) is not { } bytes
                    || MefMeshBuilder.Build(MefModel.Read(bytes, AssetFormatProfile.Igi2), (float)(1.0 / WorldUnits.PerMetre)) is not { } geometry)
                {
                    continue;
                }

                details.Add(new Detail(model, geometry, device.CreateMesh(geometry.Vertices, geometry.Indices)));
            }
            catch (Exception error) when (error is FormatException or ArgumentException or GraphicsDeviceException or InvalidDataException)
            {
                _logger.Warning(LogCategory, $"The bomb's model '{model}' did not load: {error.Message}");
            }
        }

        return [.. details];
    }

    private TextureHandle Texture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        try
        {
            if (BombKit.Texture(name) is { } bytes)
            {
                TextureImage image = TexFile.Load(bytes);
                handle = device.CreateTexture(new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat), image.Pixels);
            }
        }
        catch (Exception error) when (error is TextureFormatException or ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"The bomb's texture '{name}' did not load: {error.Message}");
        }

        _textures[name] = handle;
        return handle;
    }

    private sealed record Detail(string Model, MeshGeometry Geometry, MeshHandle Mesh);
}

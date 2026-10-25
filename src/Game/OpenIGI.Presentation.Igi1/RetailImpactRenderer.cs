using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Draws the retail impact effects: the dust a round kicks off a wall, the chips it knocks loose, and
/// the blood a body throws.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is the original's own asset — <c>concrete1.spr</c>, <c>blood1.spr</c>,
/// <c>blood2.spr</c> out of <c>common/sprites/sprites.res</c>, and the four impact models
/// (<c>1009_01_1</c>, <c>1011_01_1</c>, <c>1012_01_1</c>, <c>1013_01_1</c>) the material table names —
/// so what appears is what the game shipped rather than a procedural stand-in. The motion is
/// <see cref="ImpactEffectRuntime"/>'s, stepped on the fixed tick; this class only draws it.
/// </para>
/// <para>
/// The dust and the blood are the 2-D particle engine both titles share, drawn by its one drawer
/// (<see cref="Particle2DRenderer"/>, <c>0x4FD020</c>); this class adds the chips.
/// </para>
/// </remarks>
public sealed class RetailImpactRenderer : IImpactEffectPresenter, IDisposable
{
    /// <summary>The archive the three sprites live in.</summary>
    public const string SpriteArchive = MissionLoader.CommonSpritesArchive;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    /// <summary>The four models the retail material table names for impact chips.</summary>
    private static readonly string[] FragmentModels = ["1009_01_1", "1011_01_1", "1012_01_1", "1013_01_1"];

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private readonly VirtualFileSystem? _fileSystem;
    private readonly Particle2DRenderer _particles;
    private readonly Dictionary<string, FragmentModel> _fragmentModels = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RetailImpactRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">The mounted resource registry, for the chip models' textures.</param>
    /// <param name="fileSystem">The retail VFS, for the sprite archive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public RetailImpactRenderer(
        IGraphicsDevice device,
        ResourceRegistry? resources = null,
        VirtualFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _resources = resources;
        _fileSystem = fileSystem;
        _particles = new Particle2DRenderer(device, fileSystem, SpriteArchive);
    }

    /// <inheritdoc/>
    public int MistFrameCount => Math.Max(1, _particles.Sprite(BloodEffects.MistSprite).Frames.Length);

    /// <inheritdoc/>
    public bool SpritesAvailable => EnsureSprites();

    /// <inheritdoc/>
    public int Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReleaseFragmentModels();
        _ = EnsureSprites();

        if (_resources is null)
        {
            return 0;
        }

        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (_fragmentModels.ContainsKey(instance.Name)
                    || !FragmentModels.Contains(instance.Name, StringComparer.OrdinalIgnoreCase)
                    || !pack.TryGetModel(instance, out MefModel? model)
                    || model is null)
                {
                    continue;
                }

                MeshGeometry? geometry = MefMeshBuilder.Build(model!, 1f / UnitsPerMetre);
                if (geometry is null || !pack.TryGetTextureName(instance, 0, out string textureName))
                {
                    continue;
                }

                TextureHandle texture = SpriteStrip.LoadTexture(_device, _resources, textureName);
                if (!texture.IsValid)
                {
                    continue;
                }

                _fragmentModels[instance.Name] = new FragmentModel(geometry, texture);
            }
        }

        return _fragmentModels.Count;
    }

    /// <inheritdoc/>
    public void Draw(
        ImpactEffectRuntime effects,
        in Camera3D camera,
        in Matrix4f projection,
        float blend)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ObjectDisposedException.ThrowIf(_disposed, this);

        DrawFragments(effects, camera, projection, blend);
        if (EnsureSprites())
        {
            _particles.Draw(effects.Particles.Systems, camera, projection, blend);
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

        _particles.Dispose();
        ReleaseFragmentModels();
    }

    private void DrawFragments(
        ImpactEffectRuntime effects,
        in Camera3D camera,
        in Matrix4f projection,
        float blend)
    {
        if (effects.Explosions.FragmentGroups.Count == 0 || _fragmentModels.Count == 0)
        {
            return;
        }

        try
        {
            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(DepthState.TestAndWrite);

            // Every Explosion0's pieces, a round's chips and a blast's alike, each group in its own model (0x47F0D0).
            foreach (ExplosionFragments group in effects.Explosions.FragmentGroups)
            {
                if (group.Flying.Count == 0 || !_fragmentModels.TryGetValue(group.Model, out FragmentModel? model))
                {
                    continue;
                }

                if (!model.Mesh.IsValid)
                {
                    model.Mesh = _device.CreateMesh(model.Geometry.Vertices, model.Geometry.Indices);
                }

                foreach (ExplosionFragment fragment in group.Flying)
                {
                    DrawFragment(model, fragment, camera, blend);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }
    }

    private void DrawFragment(FragmentModel model, ExplosionFragment fragment, in Camera3D camera, float blend)
    {
        Vector3d position = Vector3d.Lerp(fragment.PreviousPosition, fragment.Position, blend);
        float[] o = fragment.Orientation;
        Matrix4f rigid = camera.CreateModelView(
            position / WorldScene.UnitsPerMetre,
            Matrix3f.FromRows(new Vector3f(o[0], o[1], o[2]), new Vector3f(o[3], o[4], o[5]), new Vector3f(o[6], o[7], o[8])));
        Matrix4f modelView = rigid.WithAxesScaled(fragment.Scale);
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            _device.DrawMesh(
                model.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                model.Texture,
                TextureHandle.None,
                BlendMode.Opaque);
        }
    }

    // Whether the dust's and the blood's sprites resolve in the title's archive.
    private bool EnsureSprites() =>
        _particles.Sprite(GunShotImpactEffects.PuffSprite).Texture.IsValid
        && _particles.Sprite(BloodEffects.MistSprite).Texture.IsValid
        && _particles.Sprite(BloodEffects.DropletSprite).Texture.IsValid;

    private void ReleaseFragmentModels()
    {
        foreach (FragmentModel model in _fragmentModels.Values)
        {
            if (model.Mesh.IsValid)
            {
                _device.DestroyMesh(model.Mesh);
            }

            if (model.Texture.IsValid)
            {
                _device.DestroyTexture(model.Texture);
            }
        }

        _fragmentModels.Clear();
    }

    private static Bgra32 Tint(float r, float g, float b, float a) => new(
        (byte)Math.Clamp(r * 255f, 0f, 255f),
        (byte)Math.Clamp(g * 255f, 0f, 255f),
        (byte)Math.Clamp(b * 255f, 0f, 255f),
        (byte)Math.Clamp(a * 255f, 0f, 255f));

    private static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private sealed class FragmentModel(MeshGeometry geometry, TextureHandle texture)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public TextureHandle Texture { get; } = texture;

        public MeshHandle Mesh { get; set; } = MeshHandle.None;
    }
}

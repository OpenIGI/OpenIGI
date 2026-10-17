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
/// The draw follows <c>0x4FD020</c>: a quad of the system's authored size <b>times the particle's
/// growth</b> (and only where the system declares a growth rate), rotated by the particle's own
/// accumulated roll, tinted a flat neutral and faded by the particle's alpha. The one thing the
/// original does that is not reproduced is its half-pixel floor on the projected size, which only ever
/// keeps a degenerate quad from vanishing.
/// </para>
/// <para>
/// Two culls are the original's own: nothing inside <see cref="NearCullUnits"/> of the eye and nothing
/// past <see cref="FarCullUnits"/> is drawn at all.
/// </para>
/// </remarks>
public sealed class RetailImpactRenderer : IDisposable
{
    /// <summary>The archive the three sprites live in.</summary>
    public const string SpriteArchive = MissionLoader.CommonSpritesArchive;

    /// <summary>Inside this distance from the eye a particle is not drawn, in world units.</summary>
    /// <remarks><c>0x4FD1A2</c> — half a metre.</remarks>
    public const float NearCullUnits = 2048f;

    /// <summary>Past this distance a particle is not drawn, in world units.</summary>
    /// <remarks>The system's own <c>+224</c>, which every impact effect sets to a hundred metres.</remarks>
    public const float FarCullUnits = 409600f;

    /// <summary>How many quads may be emitted in one frame.</summary>
    public const int MaximumQuads = 1024;

    /// <summary>The smallest a quad's projected half-extent may be, in pixels.</summary>
    /// <remarks>
    /// <c>0x4FD21C</c>: a projected half under half a pixel is raised to it, so an impact a long way
    /// off is still worth a pixel instead of falling through the rasteriser.
    /// </remarks>
    public const float MinimumHalfPixels = 0.5f;

    /// <summary>The flat tint every impact particle is drawn with.</summary>
    /// <remarks>
    /// The systems all declare <c>(127, 127, 127)</c> and none of them samples the world's light
    /// (<c>+220</c> is zero), so the colour is constant. <c>127</c> is full brightness in this space:
    /// the lighting path that would otherwise fill these three normalises its brightest channel to
    /// <c>127</c> by dividing through <c>1/127</c> at <c>0x4FD126</c>.
    /// </remarks>
    public const float NeutralTint = 1f;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;
    private const int MaximumVertices = MaximumQuads * 4;

    /// <summary>The four models the retail material table names for impact chips.</summary>
    private static readonly string[] FragmentModels = ["1009_01_1", "1011_01_1", "1012_01_1", "1013_01_1"];

    // Fully emissive, so the forward pass treats these quads as their own light rather than as
    // surfaces to be lit -- the same recipe the muzzle flame and the tracer use.
    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private readonly VirtualFileSystem? _fileSystem;
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private readonly Dictionary<string, FragmentModel> _fragmentModels = new(StringComparer.OrdinalIgnoreCase);
    private SpriteStrip _puff = SpriteStrip.Empty;
    private SpriteStrip _mist = SpriteStrip.Empty;
    private SpriteStrip _droplet = SpriteStrip.Empty;
    private MeshHandle _mesh = MeshHandle.None;
    private int _used;
    private int _lastUsed;
    private float _exposure = 1f;
    private float _metrePixels;
    private bool _spritesTried;
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
    }

    /// <summary>Gets how many frames <c>blood1.spr</c> turned out to have.</summary>
    /// <remarks>
    /// The mist's frame rate is <c>15 / frameCount</c> whole ticks, so the runtime needs this before it
    /// builds a body's systems. Retail ships seven frames, which gives two ticks each.
    /// </remarks>
    public int MistFrameCount => Math.Max(1, _mist.Frames.Length);

    /// <summary>Gets whether the three retail sprites all resolved.</summary>
    public bool SpritesAvailable => EnsureSprites();

    /// <summary>Gets how many dust quads the last draw emitted.</summary>
    public int LastPuffQuads { get; private set; }

    /// <summary>Gets how many blood-mist quads the last draw emitted.</summary>
    public int LastMistQuads { get; private set; }

    /// <summary>Gets how many blood-droplet quads the last draw emitted.</summary>
    public int LastDropletQuads { get; private set; }

    /// <summary>Binds the impact chip models out of a mission's packs.</summary>
    /// <param name="mission">The loaded mission whose common pack owns them.</param>
    /// <returns>How many of the four models resolved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mission"/> is <see langword="null"/>.</exception>
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

    /// <summary>Draws every live effect.</summary>
    /// <param name="effects">The runtime holding the particles and chips.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="blend">The frame's position between the last two ticks, 0–1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is <see langword="null"/>.</exception>
    public void Draw(
        ImpactEffectRuntime effects,
        in Camera3D camera,
        in Matrix4f projection,
        float blend)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ObjectDisposedException.ThrowIf(_disposed, this);

        DrawFragments(effects, camera, projection, blend);

        if (!EnsureSprites())
        {
            return;
        }

        Vector3d eye = camera.Position;
        _used = 0;

        // These quads are the original's own 2D sprites: flat, unlit, and authored in display terms.
        // They are drawn emissive so the deferred pass does not shade them, which puts them in the
        // scene-referred lit buffer -- so they take the same exposure division the first-person rig and
        // the selection glow take. Without it a dark scene's high adaptation clipped the blood's red
        // channel while it went on lifting green and blue, and the spray came out pink.
        _exposure = 1f / MathF.Max(_device.SceneExposure, 1e-3f);

        // Pixels per metre at one metre, for the half-pixel floor: the projection's vertical scale
        // times half the viewport's height, the same conversion the outline pass uses.
        _metrePixels = MathF.Abs(projection.M22) * _device.CurrentMode.Height * 0.5f;

        int puffFirst = _used / 4;
        AddSystem(effects.Puffs, _puff, eye, blend);
        int puffCount = (_used / 4) - puffFirst;

        int mistFirst = _used / 4;
        foreach (BloodSystems systems in effects.Blood.Values)
        {
            AddSystem(systems.Mist, _mist, eye, blend);
        }

        int mistCount = (_used / 4) - mistFirst;

        int dropletFirst = _used / 4;
        foreach (BloodSystems systems in effects.Blood.Values)
        {
            AddSystem(systems.Droplets, _droplet, eye, blend);
        }

        int dropletCount = (_used / 4) - dropletFirst;
        LastPuffQuads = puffCount;
        LastMistQuads = mistCount;
        LastDropletQuads = dropletCount;
        if (_used == 0)
        {
            _lastUsed = 0;
            return;
        }

        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, QuadIndices.Build(MaximumQuads));
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);
            _device.ClearPoseInterpolation();

            DrawBatch(puffFirst, puffCount, _puff.Texture, modelView);
            DrawBatch(mistFirst, mistCount, _mist.Texture, modelView);
            DrawBatch(dropletFirst, dropletCount, _droplet.Texture, modelView);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // Presentation only: a backend upload failure must not stop the game.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
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

        ReleaseSprite(ref _puff);
        ReleaseSprite(ref _mist);
        ReleaseSprite(ref _droplet);
        ReleaseFragmentModels();
    }

    private void DrawBatch(int first, int count, TextureHandle texture, in Matrix4f modelView)
    {
        if (count <= 0 || !texture.IsValid)
        {
            return;
        }

        _device.DrawMesh(
            _mesh,
            first * 6,
            count * 6,
            modelView,
            texture,
            TextureHandle.None,
            BlendMode.AlphaBlend);
    }

    private void AddSystem(RetailParticleSystem system, in SpriteStrip atlas, Vector3d eye, float blend)
    {
        if (system.Count == 0 || !atlas.Texture.IsValid)
        {
            return;
        }

        ReadOnlySpan<RetailParticle> particles = system.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            AddQuad(system, atlas, particles[i], eye, blend);
        }
    }

    private void AddQuad(
        RetailParticleSystem system,
        in SpriteStrip atlas,
        in RetailParticle particle,
        Vector3d eye,
        float blend)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        // The integrator is a plain Euler step, so the previous tick's position is exactly one
        // velocity behind -- no second copy is needed to interpolate the draw.
        Vector3d previous = new(
            particle.Position.X - particle.Velocity.X,
            particle.Position.Y - particle.Velocity.Y,
            particle.Position.Z - particle.Velocity.Z);
        Vector3d position = Vector3d.Lerp(previous, particle.Position, blend);

        Vector3d centre = (position / WorldScene.UnitsPerMetre) - eye;
        double distanceMetres = centre.Length;
        double distanceUnits = distanceMetres * WorldScene.UnitsPerMetre;
        if (distanceUnits <= NearCullUnits || distanceUnits >= FarCullUnits)
        {
            return;
        }

        // 0x4FD1CB: the authored size, scaled by the growth accumulator only where the system
        // declares a growth rate. The alpha never touches the size.
        float sizeUnits = system.SizeInUnits;
        if (system.GrowthPerTick != 0f)
        {
            sizeUnits *= particle.Growth;
        }

        // 0x498040 divides the AUTHORED size by the distance and uses the result as the quad's
        // half-extent -- 0x4FD350 writes centre plus and minus it -- so the number the effect declares
        // is the half, not the width. Halving it here drew every impact at half the size the original
        // does.
        float half = sizeUnits / UnitsPerMetre;
        if (!(half > 0f))
        {
            return;
        }

        // 0x4FD21C floors the projected half-extent at half a pixel, so a distant impact is always
        // worth one pixel rather than collapsing to nothing. Expressed back in world terms at this
        // distance, which is what this renderer builds its quad from.
        float pixelsPerMetre = _metrePixels;
        if (pixelsPerMetre > 0f)
        {
            float minimumHalf = MinimumHalfPixels * (float)distanceMetres / pixelsPerMetre;
            half = MathF.Max(half, minimumHalf);
        }

        Vector3d toEye = centre / -distanceMetres;
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = new(
            (reference.Y * toEye.Z) - (reference.Z * toEye.Y),
            (reference.Z * toEye.X) - (reference.X * toEye.Z),
            (reference.X * toEye.Y) - (reference.Y * toEye.X));
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return;
        }

        rightD /= rightLength;
        Vector3d upD = new(
            (toEye.Y * rightD.Z) - (toEye.Z * rightD.Y),
            (toEye.Z * rightD.X) - (toEye.X * rightD.Z),
            (toEye.X * rightD.Y) - (toEye.Y * rightD.X));

        // 0x4FD25A rotates the quad's corners about its own centre by the accumulated roll. Turning
        // the billboard's two axes is the same rotation without leaving world space.
        double sin = 0.0;
        double cos = 1.0;
        if (particle.Spin != 0f)
        {
            sin = Math.Sin(particle.Angle);
            cos = Math.Cos(particle.Angle);
        }

        Vector3d rolledRight = new(
            (rightD.X * cos) + (upD.X * sin),
            (rightD.Y * cos) + (upD.Y * sin),
            (rightD.Z * cos) + (upD.Z * sin));
        Vector3d rolledUp = new(
            (upD.X * cos) - (rightD.X * sin),
            (upD.Y * cos) - (rightD.Y * sin),
            (upD.Z * cos) - (rightD.Z * sin));

        Vector3f right = new(
            (float)(rolledRight.X * half), (float)(rolledRight.Y * half), (float)(rolledRight.Z * half));
        Vector3f up = new(
            (float)(rolledUp.X * half), (float)(rolledUp.Y * half), (float)(rolledUp.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        if (!IsFinite(centreF) || !IsFinite(right) || !IsFinite(up) || !IsFinite(normal))
        {
            return;
        }

        float opacity = Math.Clamp(particle.Alpha, 0f, 1f);
        float level = Math.Clamp(NeutralTint * _exposure, 0f, 1f);
        Bgra32 colour = Tint(level, level, level, opacity);

        SpriteStripFrame uv = atlas.Frames.Length == 0
            ? SpriteStripFrame.Full
            : atlas.Frames[Math.Clamp(particle.Frame, 0, atlas.Frames.Length - 1)];

        _vertices[_used] = new Vertex3D(centreF - right - up, normal, uv.U0, uv.V0, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, uv.U1, uv.V0, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, uv.U1, uv.V1, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, uv.U0, uv.V1, colour: colour);
        _used += 4;
    }

    private void DrawFragments(
        ImpactEffectRuntime effects,
        in Camera3D camera,
        in Matrix4f projection,
        float blend)
    {
        if (effects.Fragments.Count == 0 || _fragmentModels.Count == 0)
        {
            return;
        }

        try
        {
            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(DepthState.TestAndWrite);

            foreach (ImpactFragment fragment in effects.Fragments)
            {
                if (!_fragmentModels.TryGetValue(fragment.Model, out FragmentModel? model))
                {
                    continue;
                }

                if (!model.Mesh.IsValid)
                {
                    model.Mesh = _device.CreateMesh(model.Geometry.Vertices, model.Geometry.Indices);
                }

                Vector3d position = Vector3d.Lerp(
                    fragment.PreviousPosition, fragment.Position, blend);
                Matrix4f rigid = camera.CreateModelView(
                    position / WorldScene.UnitsPerMetre,
                    fragment.Orientation);
                Matrix4f modelView = ScaleModelView(rigid, GunShotImpactEffects.FragmentModelScale);

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
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }
    }

    private bool EnsureSprites()
    {
        if (_spritesTried)
        {
            return _puff.Texture.IsValid && _mist.Texture.IsValid && _droplet.Texture.IsValid;
        }

        _spritesTried = true;
        if (_fileSystem is null)
        {
            return false;
        }

        _puff = SpriteStrip.Load(_device, _fileSystem, SpriteArchive, GunShotImpactEffects.PuffSprite);
        _mist = SpriteStrip.Load(_device, _fileSystem, SpriteArchive, BloodEffects.MistSprite);
        _droplet = SpriteStrip.Load(_device, _fileSystem, SpriteArchive, BloodEffects.DropletSprite);
        return _puff.Texture.IsValid && _mist.Texture.IsValid && _droplet.Texture.IsValid;
    }

    private void ReleaseSprite(ref SpriteStrip atlas)
    {
        if (atlas.Texture.IsValid)
        {
            _device.DestroyTexture(atlas.Texture);
        }

        atlas = SpriteStrip.Empty;
    }

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

    private static Matrix4f ScaleModelView(in Matrix4f rigid, float scale) => new(
        rigid.M11 * scale, rigid.M12 * scale, rigid.M13 * scale, rigid.M14,
        rigid.M21 * scale, rigid.M22 * scale, rigid.M23 * scale, rigid.M24,
        rigid.M31 * scale, rigid.M32 * scale, rigid.M33 * scale, rigid.M34,
        rigid.M41, rigid.M42, rigid.M43, rigid.M44);

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

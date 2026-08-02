using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using InstanceView = OpenIGI.Game.World.SceneDistanceField.InstanceView;

namespace OpenIGI.Game.World;

/// <summary>
/// The level's distance field resampled onto grids that follow the camera, so a trace step is one fetch.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SceneDistanceField"/> gives the exact answer by consulting every object. A ray takes
/// hundreds of steps and a frame traces millions of rays, so the exact answer is unaffordable per step.
/// This resamples it onto a small volume around the camera: a step becomes one texture fetch, and how
/// much geometry the level holds stops mattering.
/// </para>
/// <para>
/// <b>Rebuilt rarely, and that is what makes it cheap.</b> I.G.I.'s levels are static — nothing in them
/// moves except doors and people, neither of which this describes — so a clipmap only becomes wrong
/// when the camera walks out of it. Each level is snapped to its own voxel grid and rebuilt when the
/// camera has moved a fraction of its extent, which for the outer levels is almost never.
/// </para>
/// <para>
/// <b>Snapping is not an optimisation.</b> A grid that follows the camera continuously resamples the
/// same geometry onto slightly different voxel centres every frame, and the result flickers — the
/// distance at a fixed world point changes because the grid moved under it. Snapping the origin to a
/// multiple of the voxel size means a point keeps the same voxel until the grid jumps a whole voxel,
/// and what was already computed stays valid.
/// </para>
/// <para>
/// All levels live in one volume texture stacked along z, so a trace binds one image and indexes it by
/// level rather than binding four.
/// </para>
/// <para>
/// <b>This subsumes the reference's separate mesh-SDF trace tier, deliberately.</b> The reference keeps
/// every mesh's own distance field and, for a ray, culls the meshes near it into a froxel grid
/// (<c>LumenMeshSDFCulling</c>) so the ray tests only those, finer than its coarse global field. This
/// resamples every mesh field into the clipmap at build, culling the instances per cell as it goes, so
/// the one global trace already carries the per-object detail the reference's mesh-SDF tier existed to
/// add — at the cost of the clipmap's own voxel resolution, which for indirect light is enough. The
/// froxel-culled mesh-SDF tier is therefore not ported: it would recover only sub-clipmap-voxel detail a
/// bounce does not need, at the price of a second acceleration structure and a per-ray cull.
/// </para>
/// </remarks>
public sealed class GlobalDistanceField : IDisposable
{
    private const string LogCategory = "DistanceField";

    /// <summary>How many floats one instance record occupies. Seven <c>vec4</c>s.</summary>
    /// <remarks>
    /// Everything is padded to <c>vec4</c> even where three components would do. A std430 block packs a
    /// <c>vec3</c> to a three-float stride on the CPU and a four-float alignment on the GPU, so a struct
    /// mixing the two reads correctly for its first element and slides for every one after it.
    /// </remarks>
    private const int FloatsPerInstance = 28;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly GlobalDistanceFieldSettings _settings;
    private readonly SceneDistanceField _scene;
    private readonly Vector3d _levelOrigin;

    private readonly TextureHandle _atlasTexture;
    private readonly TextureHandle _clipmaps;
    private readonly ComputeShaderHandle _build;
    private readonly StorageBufferHandle _instances;
    private readonly StorageBufferHandle _cellOffsets;
    private readonly StorageBufferHandle _cellIndices;

    private readonly Vector3d[] _origins;
    private readonly bool[] _built;
    private bool _disposed;

    private GlobalDistanceField(
        IGraphicsDevice device,
        IIgiLogger logger,
        GlobalDistanceFieldSettings settings,
        SceneDistanceField scene,
        Vector3d levelOrigin,
        TextureHandle atlasTexture,
        TextureHandle clipmaps,
        ComputeShaderHandle build,
        StorageBufferHandle instances,
        StorageBufferHandle cellOffsets,
        StorageBufferHandle cellIndices)
    {
        _device = device;
        _logger = logger;
        _settings = settings;
        _scene = scene;
        _levelOrigin = levelOrigin;
        _atlasTexture = atlasTexture;
        _clipmaps = clipmaps;
        _build = build;
        _instances = instances;
        _cellOffsets = cellOffsets;
        _cellIndices = cellIndices;
        _origins = new Vector3d[settings.Levels];
        _built = new bool[settings.Levels];
    }

    /// <summary>Gets the volume holding every level, stacked along z.</summary>
    public TextureHandle Clipmaps => _clipmaps;

    /// <summary>Gets how the clipmaps are laid out.</summary>
    public GlobalDistanceFieldSettings Settings => _settings;

    /// <summary>Gets the world point every float coordinate in this structure is relative to.</summary>
    /// <remarks>
    /// I.G.I.'s world coordinates carry a kilometres-large absolute offset, and a float at that
    /// magnitude resolves tens of centimetres — coarser than the voxels being placed. Everything the
    /// GPU sees is therefore an offset from this point, which is chosen once and never moves.
    /// </remarks>
    public Vector3d LevelOrigin => _levelOrigin;

    /// <summary>Gets where each level's minimum corner currently sits, in world space.</summary>
    public IReadOnlyList<Vector3d> Origins => _origins;

    /// <summary>Builds the clipmaps and everything the GPU needs to fill them.</summary>
    /// <param name="device">The device to allocate on. Must support compute.</param>
    /// <param name="scene">The level's exact field, which this resamples.</param>
    /// <param name="atlas">Every model's field packed into one volume.</param>
    /// <param name="settings">How the clipmaps are laid out.</param>
    /// <param name="logger">Where to report what was allocated.</param>
    /// <returns>The clipmaps, or <see langword="null"/> when the device cannot build them.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static GlobalDistanceField? Create(
        IGraphicsDevice device,
        SceneDistanceField scene,
        DistanceFieldAtlas atlas,
        GlobalDistanceFieldSettings settings,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(atlas);

        IIgiLogger log = logger ?? NullLogger.Instance;

        if (!device.Capabilities.SupportsCompute)
        {
            log.Information(LogCategory, "No compute on this context; the clipmaps will not be built.");
            return null;
        }

        if (!settings.Enabled || atlas.Slots.Length == 0)
        {
            log.Information(LogCategory, "Nothing to build: no fields, or the settings ask for none.");
            return null;
        }

        // Derived from the level rather than passed in, and specifically NOT the camera. Every float
        // the GPU sees is an offset from this point, so it has to be fixed for the life of the level:
        // a moving origin would silently re-base every instance in the buffer between one rebuild and
        // the next. Taking the centre of what was baked also keeps the offsets as small as they can be,
        // which is the whole reason the origin exists.
        Vector3d levelOrigin = CentreOf(scene);

        try
        {
            TextureHandle atlasTexture = device.CreateVolumeTexture(
                atlas.Width, atlas.Height, atlas.Depth, VolumeFormat.R8, "sdf.atlas");
            device.UpdateVolumeTexture<byte>(atlasTexture, atlas.Volume);


            TextureHandle clipmaps = device.CreateVolumeTexture(
                settings.Resolution,
                settings.Resolution,
                settings.Resolution * settings.Levels,
                VolumeFormat.R8,
                "sdf.clipmaps");

            ComputeShaderHandle build = device.CreateComputeShader(BuildSource, "sdf.clipmap.build");

            int instanceCount = Math.Max(scene.InstanceCount, 1);
            StorageBufferHandle instances = device.CreateStorageBuffer(
                instanceCount * FloatsPerInstance * sizeof(float), "sdf.instances");

            StorageBufferHandle cellOffsets = device.CreateStorageBuffer(
                (settings.CellsPerLevel + 1) * sizeof(int), "sdf.cell.offsets");

            // The true worst case: every object listed in every cell. Sized for it rather than
            // guessed at, because the buffer is allocated once and an estimate that is wrong is a hard
            // failure at load rather than a slow frame -- which is exactly how the first version of
            // this behaved on level 1.
            StorageBufferHandle cellIndices = device.CreateStorageBuffer(
                Math.Max(instanceCount * settings.CellsPerLevel, 1024) * sizeof(int), "sdf.cell.indices");

            GlobalDistanceField field = new(
                device, log, settings, scene, levelOrigin,
                atlasTexture, clipmaps, build, instances, cellOffsets, cellIndices);

            field.UploadInstances(atlas);

            log.Information(
                LogCategory,
                $"Clipmaps: {settings.Levels} level(s) of {settings.Resolution} cubed, "
                + $"{settings.NearExtent:0.#} m to {settings.ExtentOf(settings.Levels - 1):0.#} m, "
                + $"atlas {atlas.Width}x{atlas.Height}x{atlas.Depth}.");

            return field;
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The clipmaps could not be built: {error.Message}");
            return null;
        }
    }

    /// <summary>Compiles the build shader and throws it away, to prove it compiles at all.</summary>
    /// <param name="device">The device to compile on.</param>
    /// <param name="logger">Where to report the result.</param>
    /// <returns><see langword="true"/> when the shader compiled.</returns>
    /// <remarks>
    /// <para>
    /// The clipmaps are only built once a level has loaded and a field has been baked, which is a long
    /// way into a run. A shader that does not compile would surface there as a stage that silently did
    /// nothing, so this compiles it at start-up instead, where the failure is unambiguous and costs a
    /// millisecond.
    /// </para>
    /// <para>
    /// It proves compilation and nothing else — the arithmetic is checked against
    /// <see cref="SceneDistanceField.Sample"/>, which is the exact answer this approximates.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public static bool ValidateShader(IGraphicsDevice device, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        IIgiLogger log = logger ?? NullLogger.Instance;

        if (!device.Capabilities.SupportsCompute)
        {
            return false;
        }

        ComputeShaderHandle shader = default;

        try
        {
            shader = device.CreateComputeShader(BuildSource, "sdf.clipmap.build");
            log.Information(LogCategory, "The clipmap build shader compiles.");
            return true;
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The clipmap build shader does not compile: {error.Message}");
            return false;
        }
        finally
        {
            device.DestroyComputeShader(shader);
        }
    }

    /// <summary>Rebuilds whichever levels the camera has walked out of.</summary>
    /// <param name="camera">Where the camera is, in world space.</param>
    /// <param name="force">Rebuilds every level whether or not it has drifted.</param>
    /// <returns>How many levels were rebuilt.</returns>
    /// <remarks>
    /// An inner level is rebuilt often and an outer one almost never, which falls out of the threshold
    /// being a fraction of each level's own extent rather than a fixed distance.
    /// </remarks>
    public int Update(Vector3d camera, bool force = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int rebuilt = 0;

        for (int level = 0; level < _settings.Levels; level++)
        {
            float extent = _settings.ExtentOf(level);
            float voxel = _settings.VoxelSizeOf(level);

            // Snapped to this level's own voxel grid. Without it the grid slides continuously under the
            // world and the same point's distance changes every frame, which shows as a shimmer over
            // everything the field lights.
            Vector3d wanted = new(
                Math.Floor((camera.X - (extent * 0.5)) / voxel) * voxel,
                Math.Floor((camera.Y - (extent * 0.5)) / voxel) * voxel,
                Math.Floor((camera.Z - (extent * 0.5)) / voxel) * voxel);

            if (!force && _built[level] && Same(wanted, _origins[level]))
            {
                continue;
            }

            _origins[level] = wanted;
            _built[level] = true;
            BuildLevel(level, wanted);
            rebuilt++;
        }

        if (rebuilt > 0)
        {
            // One barrier for the whole update rather than one per level. The levels do not read each
            // other, so what has to be ordered is this against whatever traces next.
            _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
        }

        return rebuilt;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _device.DestroyStorageBuffer(_cellIndices);
        _device.DestroyStorageBuffer(_cellOffsets);
        _device.DestroyStorageBuffer(_instances);
        _device.DestroyComputeShader(_build);
        _device.DestroyTexture(_clipmaps);
        _device.DestroyTexture(_atlasTexture);
    }

    /// <summary>The centre of everything the scene baked, which the GPU's floats are measured from.</summary>
    /// <remarks>
    /// I.G.I.'s world coordinates carry a kilometres-large absolute offset, and a float at that
    /// magnitude resolves tens of centimetres — coarser than the voxels being placed. Measuring from
    /// the middle of the level keeps every offset small enough that a float describes it precisely.
    /// </remarks>
    private static Vector3d CentreOf(SceneDistanceField scene)
    {
        InstanceView[] views = scene.Describe();

        if (views.Length == 0)
        {
            return default;
        }

        Vector3d min = new(double.MaxValue);
        Vector3d max = new(double.MinValue);

        foreach (InstanceView view in views)
        {
            min = new Vector3d(
                Math.Min(min.X, view.BoundsMin.X),
                Math.Min(min.Y, view.BoundsMin.Y),
                Math.Min(min.Z, view.BoundsMin.Z));
            max = new Vector3d(
                Math.Max(max.X, view.BoundsMax.X),
                Math.Max(max.Y, view.BoundsMax.Y),
                Math.Max(max.Z, view.BoundsMax.Z));
        }

        return new Vector3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);
    }

    private static bool Same(Vector3d a, Vector3d b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Z - b.Z) < 1e-6;

    /// <summary>Packs every instance into the layout the shader reads.</summary>
    private void UploadInstances(DistanceFieldAtlas atlas)
    {
        InstanceView[] views = _scene.Describe();
        float[] packed = new float[Math.Max(views.Length, 1) * FloatsPerInstance];

        for (int i = 0; i < views.Length; i++)
        {
            InstanceView view = views[i];
            DistanceFieldAtlas.Slot slot = atlas.Slots[view.FieldIndex];
            int at = i * FloatsPerInstance;

            // The world-to-local rotation, a row per vec4, with the instance's position relative to the
            // level origin riding in the spare fourth component. Two things in one register because
            // both are needed together and neither fills one.
            Matrix3f rotation = view.Orientation;

            packed[at + 0] = rotation.M11;
            packed[at + 1] = rotation.M12;
            packed[at + 2] = rotation.M13;
            packed[at + 3] = (float)(view.Position.X - _levelOrigin.X);

            packed[at + 4] = rotation.M21;
            packed[at + 5] = rotation.M22;
            packed[at + 6] = rotation.M23;
            packed[at + 7] = (float)(view.Position.Y - _levelOrigin.Y);

            packed[at + 8] = rotation.M31;
            packed[at + 9] = rotation.M32;
            packed[at + 10] = rotation.M33;
            packed[at + 11] = (float)(view.Position.Z - _levelOrigin.Z);

            packed[at + 12] = slot.Corner.X;
            packed[at + 13] = slot.Corner.Y;
            packed[at + 14] = slot.Corner.Z;
            packed[at + 15] = slot.Range;

            packed[at + 16] = slot.Size.X;
            packed[at + 17] = slot.Size.Y;
            packed[at + 18] = slot.Size.Z;
            packed[at + 19] = 0f;

            packed[at + 20] = slot.Origin.X;
            packed[at + 21] = slot.Origin.Y;
            packed[at + 22] = slot.Origin.Z;
            packed[at + 23] = 0f;

            packed[at + 24] = slot.VoxelSize.X;
            packed[at + 25] = slot.VoxelSize.Y;
            packed[at + 26] = slot.VoxelSize.Z;
            packed[at + 27] = 0f;
        }

        _device.UpdateStorageBuffer<float>(_instances, packed);
    }

    /// <summary>Sorts the level's objects into cells and dispatches the build for one clipmap level.</summary>
    /// <remarks>
    /// The culling grid is what makes this affordable. Without it every voxel consults every object,
    /// which for an outer level over a few hundred objects is hundreds of millions of transforms per
    /// rebuild; with it a voxel consults the handful that reach into its own cell.
    /// </remarks>
    private void BuildLevel(int level, Vector3d origin)
    {
        float extent = _settings.ExtentOf(level);
        float voxel = _settings.VoxelSizeOf(level);
        int cells = _settings.CellsPerAxis;
        float cellSize = extent / cells;
        float reach = RangeOf(voxel);

        InstanceView[] views = _scene.Describe();

        List<int>[] buckets = new List<int>[_settings.CellsPerLevel];

        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i] = [];
        }

        for (int i = 0; i < views.Length; i++)
        {
            InstanceView view = views[i];

            // The cells this object's box reaches, grown by exactly the distance a voxel can care
            // about. A field that only listed objects it overlapped would report empty space right up
            // against every wall; one grown by a whole cell lists nearly everything in nearly every
            // cell, which is what overflowed the index buffer. The range is the honest bound: beyond
            // it, this level stores saturation and the trace moves to the next level out.
            int x0 = CellIndex((view.BoundsMin.X - reach) - origin.X, cellSize, cells, 0);
            int y0 = CellIndex((view.BoundsMin.Y - reach) - origin.Y, cellSize, cells, 0);
            int z0 = CellIndex((view.BoundsMin.Z - reach) - origin.Z, cellSize, cells, 0);
            int x1 = CellIndex((view.BoundsMax.X + reach) - origin.X, cellSize, cells, 0);
            int y1 = CellIndex((view.BoundsMax.Y + reach) - origin.Y, cellSize, cells, 0);
            int z1 = CellIndex((view.BoundsMax.Z + reach) - origin.Z, cellSize, cells, 0);

            // Wholly outside this level, so it contributes nothing anywhere in it.
            if (view.BoundsMin.X - reach > origin.X + extent || view.BoundsMax.X + reach < origin.X
                || view.BoundsMin.Y - reach > origin.Y + extent || view.BoundsMax.Y + reach < origin.Y
                || view.BoundsMin.Z - reach > origin.Z + extent || view.BoundsMax.Z + reach < origin.Z)
            {
                continue;
            }

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        buckets[(((z * cells) + y) * cells) + x].Add(i);
                    }
                }
            }
        }

        int[] offsets = new int[_settings.CellsPerLevel + 1];
        List<int> indices = [];

        for (int i = 0; i < buckets.Length; i++)
        {
            offsets[i] = indices.Count;
            indices.AddRange(buckets[i]);
        }

        offsets[^1] = indices.Count;

        _device.UpdateStorageBuffer<int>(_cellOffsets, offsets);
        _device.UpdateStorageBuffer<int>(_cellIndices, indices.Count > 0 ? [.. indices] : [0]);

        _device.BindStorageBuffer(0, _instances);
        _device.BindStorageBuffer(1, _cellOffsets);
        _device.BindStorageBuffer(2, _cellIndices);
        _device.BindImage(0, _clipmaps, ImageAccess.Write);
        _device.SetVolumeTexture(1, _atlasTexture);

        _device.SetComputeUniform(_build, "uAtlas", 1);
        _device.SetComputeUniform(_build, "uResolution", _settings.Resolution);
        _device.SetComputeUniform(_build, "uLevel", level);
        _device.SetComputeUniform(_build, "uCellsPerAxis", cells);
        _device.SetComputeUniform(_build, "uVoxelSize", voxel);
        _device.SetComputeUniform(_build, "uCellSize", cellSize);
        _device.SetComputeUniform(
            _build,
            "uOrigin",
            new Vector3f(
                (float)(origin.X - _levelOrigin.X),
                (float)(origin.Y - _levelOrigin.Y),
                (float)(origin.Z - _levelOrigin.Z)));

        float range = RangeOf(voxel);
        _device.SetComputeUniform(_build, "uRange", range);

        const int group = 4;
        int groups = (_settings.Resolution + group - 1) / group;

        _device.Dispatch(_build, groups, groups, groups);

    }

    /// <summary>The furthest distance one clipmap level's bytes encode.</summary>
    /// <remarks>
    /// <b>A few voxels, not half the grid.</b> The first version stored up to half the level's extent,
    /// which is wrong twice over. A grid cannot honour a distance far larger than its own resolution —
    /// it never recorded the detail that would justify one — and, more practically, a voxel that cares
    /// about geometry sixteen metres away needs every object within sixteen metres in its cull list,
    /// which on level 1 meant listing almost every object in almost every cell. That is what made the
    /// index buffer overflow.
    /// <para>
    /// Four voxels is what the nesting exists to permit: a ray that wants to step further than this
    /// steps in the next level out, whose voxels are twice as large and whose range is twice as far.
    /// </para>
    /// </remarks>
    private static float RangeOf(float voxelSize) => voxelSize * 4f;

    /// <summary>The furthest distance a level's stored bytes encode.</summary>
    /// <param name="level">The level, zero being the innermost.</param>
    /// <returns>The range in metres.</returns>
    /// <remarks>
    /// Public so a pass tracing these clipmaps decodes them exactly as the build encoded them. Two
    /// copies of this number drifting apart would put every traced surface at the wrong distance.
    /// </remarks>
    public float RangeOfLevel(int level) => RangeOf(_settings.VoxelSizeOf(level));

    private static int CellIndex(double offset, float cellSize, int cells, int bias)
    {
        int index = (int)Math.Floor(offset / cellSize) + bias;
        return Math.Clamp(index, 0, cells - 1);
    }

    /// <summary>The compute shader that fills one clipmap level.</summary>
    /// <remarks>
    /// One invocation per voxel. It finds its own cell, walks the objects that reach into it, transforms
    /// its world position into each object's own space, samples that object's slot of the atlas, and
    /// keeps the smallest — which is the union of distance fields, and the reason a union is free.
    /// </remarks>
    private const string BuildSource =
        """
        #version 430 core

        layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

        // Every level of the clipmap, stacked along z.
        layout(r8, binding = 0) uniform writeonly image3D uClipmaps;

        // Every model's field, packed side by side. Sampled rather than fetched, so the hardware's
        // trilinear filter does the interpolation between voxels for nothing.
        uniform sampler3D uAtlas;

        // Seven vec4s per instance. Padded to vec4 throughout: std430 gives a vec3 a four-float
        // alignment but a three-float stride on the CPU, so a struct mixing the two reads correctly for
        // its first element and slides for every one after it.
        layout(std430, binding = 0) readonly buffer Instances
        {
            vec4 uInstances[];
        };

        layout(std430, binding = 1) readonly buffer CellOffsets
        {
            int uCellOffsets[];
        };

        layout(std430, binding = 2) readonly buffer CellIndices
        {
            int uCellIndices[];
        };

        uniform int uResolution;
        uniform int uLevel;
        uniform int uCellsPerAxis;
        uniform float uVoxelSize;
        uniform float uCellSize;
        uniform float uRange;

        // This level's minimum corner, relative to the level origin every float here is measured from.
        uniform vec3 uOrigin;

        const int kFloatsPerInstance = 7;

        float SampleInstance(int instance, vec3 world)
        {
            int at = instance * kFloatsPerInstance;

            vec4 row0 = uInstances[at + 0];
            vec4 row1 = uInstances[at + 1];
            vec4 row2 = uInstances[at + 2];
            vec4 slotCorner = uInstances[at + 3];
            vec4 slotSize = uInstances[at + 4];
            vec4 fieldOrigin = uInstances[at + 5];
            vec4 voxelSize = uInstances[at + 6];

            // A field baked with a range of zero encodes nothing: every voxel decodes to zero, which
            // would read as a surface everywhere inside its slot and shadow whatever stands there. Two
            // such degenerate fields exist in the data. Treat one as absent -- it contributes no distance
            // to the union rather than a false one.
            if (slotCorner.w <= 0.0)
            {
                return 1e6;
            }

            // Into the model's own space. The rows carry the world-to-local rotation and the fourth
            // components carry where the object stands.
            vec3 offset = world - vec3(row0.w, row1.w, row2.w);
            vec3 local = vec3(dot(row0.xyz, offset), dot(row1.xyz, offset), dot(row2.xyz, offset));

            // Where that lands in the object's own field, in voxels.
            vec3 voxel = (local - fieldOrigin.xyz) / voxelSize.xyz;

            // Clamped half a texel INSIDE the slot, which is load-bearing. The slots are packed
            // adjacent, so a linear fetch at a slot's edge blends in whatever model was packed beside
            // it -- and the two have no geometric relationship whatever. Without this inset every
            // object in the level wears a thin halo of some unrelated object's surface.
            vec3 clamped = clamp(voxel, vec3(0.5), slotSize.xyz - vec3(0.5));

            vec3 uvw = (slotCorner.xyz + clamped) / vec3(textureSize(uAtlas, 0));
            float encoded = texture(uAtlas, uvw).r;
            float distance = ((encoded * 2.0) - 1.0) * slotCorner.w;

            // How far the sample had to be dragged back inside the slot, in local metres.
            float beyond = length((clamped - voxel) * voxelSize.xyz);

            // Outside its own slot an object's field holds no information, so the honest conservative
            // distance is the distance to the slot itself and nothing more. A slot is baked a margin
            // larger than the object's box, so its outer texels read near the field's full range -- and
            // the previous form added that range on top of the distance to the slot, handing back
            // clearance that is not there and letting a sphere trace step through the surface the slot
            // contains. The exact field this resamples composites the same way, taking the distance to an
            // object's bounds for any point outside them and only sampling the field within. Inside the
            // slot the outside distance is zero and the field value stands, sign and all.
            return beyond > 0.0 ? beyond : distance;
        }

        void main()
        {
            ivec3 at = ivec3(gl_GlobalInvocationID);

            if (any(greaterThanEqual(at, ivec3(uResolution))))
            {
                return;
            }

            // The voxel's centre, relative to the level origin.
            vec3 world = uOrigin + ((vec3(at) + 0.5) * uVoxelSize);

            ivec3 cell = clamp(
                ivec3(floor((world - uOrigin) / uCellSize)),
                ivec3(0),
                ivec3(uCellsPerAxis - 1));

            int cellIndex = ((cell.z * uCellsPerAxis) + cell.y) * uCellsPerAxis + cell.x;
            int first = uCellOffsets[cellIndex];
            int last = uCellOffsets[cellIndex + 1];

            float nearest = uRange;

            for (int i = first; i < last; i++)
            {
                nearest = min(nearest, SampleInstance(uCellIndices[i], world));
            }

            // Encoded exactly as the mesh fields are, centred so the sign survives. A trace decodes it
            // the same way, which is what lets one trace function read either.
            float encoded = (clamp(nearest / uRange, -1.0, 1.0) * 0.5) + 0.5;

            imageStore(uClipmaps, ivec3(at.xy, (uLevel * uResolution) + at.z), vec4(encoded, 0.0, 0.0, 0.0));
        }
        """;
}

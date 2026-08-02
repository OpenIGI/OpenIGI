using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

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
    /// <param name="levelOrigin">The world point float coordinates are measured from.</param>
    /// <param name="settings">How the clipmaps are laid out.</param>
    /// <param name="logger">Where to report what was allocated.</param>
    /// <returns>The clipmaps, or <see langword="null"/> when the device cannot build them.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static GlobalDistanceField? Create(
        IGraphicsDevice device,
        SceneDistanceField scene,
        DistanceFieldAtlas atlas,
        Vector3d levelOrigin,
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

            // Worst case every object reaches every cell. Bounded rather than grown, because the buffer
            // is allocated once and a level whose objects genuinely all overlap is a level that was
            // going to be slow regardless.
            StorageBufferHandle cellIndices = device.CreateStorageBuffer(
                Math.Max(instanceCount * settings.CellsPerLevel / 4, 1024) * sizeof(int), "sdf.cell.indices");

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

    private static bool Same(Vector3d a, Vector3d b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Z - b.Z) < 1e-6;

    /// <summary>Packs every instance into the layout the shader reads.</summary>
    private void UploadInstances(DistanceFieldAtlas atlas)
    {
        SceneDistanceField.InstanceView[] views = _scene.Describe();
        float[] packed = new float[Math.Max(views.Length, 1) * FloatsPerInstance];

        for (int i = 0; i < views.Length; i++)
        {
            SceneDistanceField.InstanceView view = views[i];
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

        SceneDistanceField.InstanceView[] views = _scene.Describe();

        List<int>[] buckets = new List<int>[_settings.CellsPerLevel];

        for (int i = 0; i < buckets.Length; i++)
        {
            buckets[i] = [];
        }

        for (int i = 0; i < views.Length; i++)
        {
            SceneDistanceField.InstanceView view = views[i];

            // The cell range this object's box reaches, in this level's space. Expanded by one cell so
            // an object just outside a cell still contributes the distance TO it -- a field that only
            // knew about objects it overlapped would report empty space right up against every wall.
            int x0 = CellIndex(view.BoundsMin.X - origin.X, cellSize, cells, -1);
            int y0 = CellIndex(view.BoundsMin.Y - origin.Y, cellSize, cells, -1);
            int z0 = CellIndex(view.BoundsMin.Z - origin.Z, cellSize, cells, -1);
            int x1 = CellIndex(view.BoundsMax.X - origin.X, cellSize, cells, 1);
            int y1 = CellIndex(view.BoundsMax.Y - origin.Y, cellSize, cells, 1);
            int z1 = CellIndex(view.BoundsMax.Z - origin.Z, cellSize, cells, 1);

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

        // The range the clipmap's own bytes encode. Two voxels either side is what a sphere trace can
        // actually use: a step longer than the grid's own resolution is a step past detail the grid
        // never recorded, so storing a larger range would be storing a promise it cannot keep.
        float range = voxel * _settings.Resolution * 0.5f;
        _device.SetComputeUniform(_build, "uRange", range);

        const int group = 4;
        int groups = (_settings.Resolution + group - 1) / group;
        _device.Dispatch(_build, groups, groups, groups);
    }

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

            // How far the sample had to be dragged back inside the slot. A point outside an object's
            // own volume is at least that far from anything the volume describes, so adding it keeps
            // the answer conservative -- which is the only safe direction for a sphere trace.
            vec3 outside = (clamped - voxel) * voxelSize.xyz;

            return distance + length(outside);
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

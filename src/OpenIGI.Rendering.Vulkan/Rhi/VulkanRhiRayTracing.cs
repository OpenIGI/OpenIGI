using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.RayTracing;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// The RHI's traced world on Vulkan: the legacy device's own, fed through the interface.
/// </summary>
/// <remarks>
/// <para>
/// The same world the legacy renderer traces its sun shadow against -- structures per mesh, placements
/// accumulated from the frame's draws, skinned meshes refitted, the terrain placed as drawn, cut-out masks
/// and the hit records that name them -- so the RHI's traced shadow is that shadow, not a second
/// implementation of it. What differs is only who asks: meshes arrive as RHI buffers and the
/// submissions' progress comes from the RHI device's own timeline.
/// </para>
/// <para>
/// A cut-out texture is decided by the legacy device's own test (<c>VulkanTexture.IsCutout</c>). Such a
/// surface with no mask to test against is kept out of the world rather than traced as a solid board.
/// </para>
/// </remarks>
internal sealed class VulkanRhiRayTracing : IRhiRayTracing, IDisposable
{
    private readonly VulkanRhiDevice _owner;
    private readonly VulkanWorldScene _scene;
    private readonly VulkanOpacityMasks _masks;
    private readonly VulkanPlaceholderScene _placeholder;
    private readonly VulkanTracedAlbedo _albedo;
    private readonly VulkanRhiTexture _albedoTexture;
    private readonly VulkanRhiAccelerationStructure _placeholderView;
    private readonly VulkanRhiTexture _maskTexture;
    private readonly HashSet<uint> _cutout = [];

    // One adapter per mesh, refreshed on every sighting: observed once per draw, a fresh one each time
    // would be thousands of allocations a frame.
    private readonly Dictionary<uint, TracedMesh> _meshes = [];

    // One view per structure the top level rotates through, so a bind group naming one is found again
    // the next time that structure comes round rather than built afresh every frame.
    private readonly Dictionary<VulkanAccelerationStructure, VulkanRhiAccelerationStructure> _wrapped = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiRayTracing"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device.</param>
    /// <param name="allocator">The allocator.</param>
    /// <param name="commands">The pool the mask uploads come from.</param>
    /// <param name="owner">The RHI device, whose submissions the world's lifetimes follow.</param>
    public VulkanRhiRayTracing(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        VulkanRhiDevice owner)
    {
        _owner = owner;
        _scene = new VulkanWorldScene(vk, api, device, allocator);

        // Each texture's colour, for a reflected hit; at the resolution the legacy device stores them at.
        _albedo = new VulkanTracedAlbedo(
            vk, device, allocator, commands, RenderCVars.ReflectionTexture.Value, ImageLayout.General);
        _albedoTexture = new VulkanRhiTexture(
            vk,
            device.Device,
            allocator,
            _albedo.Image,
            default,
            new RhiTextureDescriptor(
                _albedo.Resolution,
                _albedo.Resolution,
                RhiTextureFormat.Rgba8UnormSrgb,
                RhiTextureUsage.Sampled,
                DepthOrArrayLayers: _albedo.Capacity,
                Dimension: RhiTextureViewDimension.Texture2DArray,
                Label: "traced albedo"),
            owned: false)
        {
            Layout = ImageLayout.General,
        };

        // Built at once, as the Vulkan renderer's is: the tracing shaders bind a structure from their first
        // dispatch, before the world has seen a draw.
        _placeholder = new VulkanPlaceholderScene(vk, api, device, allocator, commands, owner);
        _placeholderView = new VulkanRhiAccelerationStructure(
            _placeholder.TopLevel ?? throw new InvalidOperationException("The placeholder scene built no structure."),
            "placeholder");

        // Resting in general, as every texture the RHI binds does.
        _masks = new VulkanOpacityMasks(vk, device, allocator, commands, ImageLayout.General);
        _maskTexture = new VulkanRhiTexture(
            vk,
            device.Device,
            allocator,
            _masks.Image,
            default,
            new RhiTextureDescriptor(
                VulkanOpacityMasks.Resolution,
                VulkanOpacityMasks.Resolution,
                RhiTextureFormat.R8Unorm,
                RhiTextureUsage.Sampled,
                DepthOrArrayLayers: VulkanOpacityMasks.MaxLayers,
                Dimension: RhiTextureViewDimension.Texture2DArray,
                Label: "cutout masks"),
            owned: false)
        {
            Layout = ImageLayout.General,
        };
    }

    /// <inheritdoc/>
    public IRhiAccelerationStructure? WorldScene
    {
        get
        {
            if (_scene.TopLevel is not { } top)
            {
                return null;
            }

            if (!_wrapped.TryGetValue(top, out VulkanRhiAccelerationStructure? wrapped))
            {
                wrapped = new VulkanRhiAccelerationStructure(top, "world");
                _wrapped[top] = wrapped;
            }

            return wrapped;
        }
    }

    /// <inheritdoc/>
    public bool IsWorldReady => _scene.IsReady;

    /// <inheritdoc/>
    public IRhiAccelerationStructure TraceableScene =>
        _scene.IsReady && WorldScene is { } world ? world : _placeholderView;

    /// <inheritdoc/>
    public int PlacementCount => _scene.PlacementCount;

    /// <inheritdoc/>
    public ulong HitRecordAddress => _scene.RecordAddress;

    /// <inheritdoc/>
    public IRhiTextureView CutoutMasks => _maskTexture.View;

    /// <inheritdoc/>
    public IRhiTextureView TracedAlbedo => _albedoTexture.View;

    /// <inheritdoc/>
    public void ObserveTexture(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        if (Textures.VulkanTexture.IsCutout(pixels, width, height))
        {
            _cutout.Add(textureId);
            _masks.Register(textureId, pixels, width, height);
        }

        // Every texture, not only the cut-outs: a reflected ray can strike anything. The array turns away
        // what it does not want, so the size question lives there.
        _albedo.Register(textureId, pixels, width, height);
    }

    /// <inheritdoc/>
    public void ForgetTexture(uint textureId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _albedo.Release(textureId);
    }

    /// <inheritdoc/>
    public void Observe(uint meshId, in RhiTracedMesh mesh, in Matrix4f worldTransform, in RhiTracedSurface surface, RhiRayMask mask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool cutout = _cutout.Contains(surface.TextureId);
        int maskLayer = -1;
        if (cutout && !_masks.TryGetLayer(surface.TextureId, out maskLayer))
        {
            return;
        }

        _scene.Observe(
            meshId,
            Adapt(meshId, mesh),
            worldTransform,
            new RayTracedSurface(
                surface.IndexOffset,
                surface.IndexCount,
                !cutout,
                maskLayer,

                // A miss is common and fine -- the hit then resolves as it would with no array -- so it
                // is not a reason to keep the run out, as a missing mask is.
                _albedo.TryGetLayer(surface.TextureId, out int albedoLayer) ? albedoLayer : -1,
                mesh.Animated),
            (byte)mask);
    }

    /// <inheritdoc/>
    public void ObserveCaster(uint meshId, in RhiTracedMesh mesh, in Matrix4f worldTransform, int indexCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scene.ObserveCaster(meshId, Adapt(meshId, mesh), worldTransform, indexCount);
    }

    /// <inheritdoc/>
    public void Forget(uint meshId)
    {
        _meshes.Remove(meshId);
        _scene.Forget(meshId);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _meshes.Clear();
        _scene.Reset();
    }

    /// <inheritdoc/>
    public void RecordSceneUpdate(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scene.Record(((VulkanRhiCommandEncoder)encoder).Command, _owner);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _maskTexture.Dispose();
        _masks.Dispose();
        _albedoTexture.Dispose();
        _albedo.Dispose();
        _placeholder.Dispose();
        _scene.Dispose();
    }

    private TracedMesh Adapt(uint meshId, in RhiTracedMesh mesh)
    {
        if (!_meshes.TryGetValue(meshId, out TracedMesh? adapter))
        {
            adapter = new TracedMesh();
            _meshes[meshId] = adapter;
        }

        // This sighting's, because a skinned mesh draws from a different buffer of its pose ring each
        // frame and the world re-points its structure at whichever one this is.
        adapter.Mesh = mesh;
        return adapter;
    }

    /// <summary>An RHI mesh as the traced world reads it, as of its latest sighting.</summary>
    private sealed class TracedMesh : IRayTracedMesh
    {
        public RhiTracedMesh Mesh { get; set; }

        public ulong VertexBufferAddress => ((VulkanRhiBuffer)Mesh.Vertices).Buffer.DeviceAddress;

        public ulong IndexBufferAddress => ((VulkanRhiBuffer)Mesh.Indices).Buffer.DeviceAddress;

        public int VertexCapacity => Mesh.VertexCapacity;

        public int IndexCapacity => Mesh.IndexCapacity;

        public int DrawableIndexCount => Mesh.DrawableIndexCount;

        public bool Animated => Mesh.Animated;

        public bool Storage => Mesh.Storage;

        public int ContentVersion => Mesh.ContentVersion;
    }
}

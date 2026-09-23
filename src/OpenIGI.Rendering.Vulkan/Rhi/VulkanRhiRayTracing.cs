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
    private readonly VulkanRhiTexture _maskTexture;
    private readonly HashSet<uint> _cutout = [];
    private VulkanRhiAccelerationStructure? _wrapped;
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

            if (_wrapped is null || !ReferenceEquals(_wrapped.Structure, top))
            {
                _wrapped = new VulkanRhiAccelerationStructure(top, "world");
            }

            return _wrapped;
        }
    }

    /// <inheritdoc/>
    public bool IsWorldReady => _scene.IsReady;

    /// <inheritdoc/>
    public int PlacementCount => _scene.PlacementCount;

    /// <inheritdoc/>
    public ulong HitRecordAddress => _scene.RecordAddress;

    /// <inheritdoc/>
    public IRhiTextureView CutoutMasks => _maskTexture.View;

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
            new TracedMesh(mesh),
            worldTransform,
            new RayTracedSurface(surface.IndexOffset, surface.IndexCount, !cutout, maskLayer, -1, mesh.Animated),
            (byte)mask);
    }

    /// <inheritdoc/>
    public void ObserveCaster(uint meshId, in RhiTracedMesh mesh, in Matrix4f worldTransform, int indexCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scene.ObserveCaster(meshId, new TracedMesh(mesh), worldTransform, indexCount);
    }

    /// <inheritdoc/>
    public void Forget(uint meshId) => _scene.Forget(meshId);

    /// <inheritdoc/>
    public void Reset() => _scene.Reset();

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
        _scene.Dispose();
    }

    /// <summary>An RHI mesh as the traced world reads it.</summary>
    private sealed class TracedMesh(RhiTracedMesh mesh) : IRayTracedMesh
    {
        public ulong VertexBufferAddress => ((VulkanRhiBuffer)mesh.Vertices).Buffer.DeviceAddress;

        public ulong IndexBufferAddress => ((VulkanRhiBuffer)mesh.Indices).Buffer.DeviceAddress;

        public int VertexCapacity => mesh.VertexCapacity;

        public int IndexCapacity => mesh.IndexCapacity;

        public int DrawableIndexCount => mesh.DrawableIndexCount;

        public bool Animated => mesh.Animated;

        public bool Storage => mesh.Storage;

        public int ContentVersion => mesh.ContentVersion;
    }
}

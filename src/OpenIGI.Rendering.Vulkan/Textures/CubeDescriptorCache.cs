using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>
/// Hands out the descriptor set that binds one cube map, one per cube, reused while the cube lives.
/// </summary>
/// <remarks>
/// <para>
/// Much smaller than <see cref="MeshTextureDescriptorCache"/> and deliberately so. That one is keyed by
/// a triple of textures and scales with the number of objects in a level; this is keyed by a single cube
/// and scales with the number of reflective surfaces, which across the whole of I.G.I. 2 is thirty-one
/// baked maps and per level a handful. One pool covers it, and the pools chain anyway because the cost
/// of that is ten lines and the cost of being wrong is an allocation failure in the middle of a frame.
/// </para>
/// <para>
/// A cube's set is written once, when it is first drawn with. Nothing invalidates it short of the cube
/// being destroyed, because unlike a diffuse-and-lightmap pairing there is no combination to go stale —
/// a cube is bound alone.
/// </para>
/// </remarks>
internal sealed unsafe class CubeDescriptorCache : IDisposable
{
    private const uint SetsPerPool = 64;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DescriptorSetLayout _layout;
    private readonly List<DescriptorPool> _pools = [];
    private readonly Dictionary<uint, DescriptorSet> _sets = [];
    private uint _remaining;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="CubeDescriptorCache"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pools live on.</param>
    /// <param name="layout">The one-sampler descriptor set layout the sets are allocated to.</param>
    public CubeDescriptorCache(Vk vk, Device device, DescriptorSetLayout layout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        _layout = layout;
    }

    /// <summary>Gets, writing if needed, the set that binds one cube.</summary>
    /// <param name="id">The cube's identifier, which keys the cache.</param>
    /// <param name="cube">The cube.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    public DescriptorSet Get(uint id, VulkanCubeTexture cube)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cube);

        if (_sets.TryGetValue(id, out DescriptorSet existing))
        {
            return existing;
        }

        DescriptorSet set = Allocate();
        // The image and its sampler bind apart, because the mesh shader declares them apart.
        DescriptorImageInfo image = new()
        {
            ImageView = cube.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        DescriptorImageInfo sampler = new()
        {
            Sampler = cube.Sampler,
        };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.SampledImage,
            PImageInfo = &image,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 1,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.Sampler,
            PImageInfo = &sampler,
        };

        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
        _sets[id] = set;
        return set;
    }

    /// <summary>Forgets the set that named one cube, which is being destroyed.</summary>
    /// <param name="id">The cube's identifier.</param>
    /// <remarks>
    /// The set itself is not given back — it belongs to a pool that is freed whole. Forgetting it is
    /// what stops a later cube that happened to take the same id from being drawn with the dead one's
    /// image view.
    /// </remarks>
    public void Forget(uint id) => _sets.Remove(id);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (DescriptorPool pool in _pools)
        {
            _vk.DestroyDescriptorPool(_device, pool, null);
        }

        _pools.Clear();
        _sets.Clear();
    }

    private DescriptorSet Allocate()
    {
        if (_pools.Count == 0 || _remaining == 0)
        {
            _pools.Add(CreatePool());
            _remaining = SetsPerPool;
        }

        DescriptorSetLayout layout = _layout;
        DescriptorPool current = _pools[^1];
        DescriptorSetAllocateInfo allocate = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = current,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };

        DescriptorSet set;
        VkCheck.Ok(_vk.AllocateDescriptorSets(_device, &allocate, &set), "vkAllocateDescriptorSets (cube)");
        _remaining--;
        return set;
    }

    private DescriptorPool CreatePool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.SampledImage,
            DescriptorCount = SetsPerPool,
        };
        sizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.Sampler,
            DescriptorCount = SetsPerPool,
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
            MaxSets = SetsPerPool,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool (cube)");
        return pool;
    }
}

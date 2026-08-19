using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>
/// Hands out the descriptor set that binds a draw's diffuse and lightmap textures, one per distinct
/// pair, reused while both textures live.
/// </summary>
/// <remarks>
/// The world shader samples two textures, so a draw binds a set that points at both. A model's records
/// pair the same textures over and over, so the set for a pair is written once and cached, keyed by the
/// two handle ids together. When either texture is destroyed, every set that named it is given back.
/// </remarks>
internal sealed unsafe class MeshTextureDescriptorCache : IDisposable
{
    private const uint MaxSets = 4096;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorPool _pool;
    private readonly Dictionary<(uint Diffuse, uint Lightmap), DescriptorSet> _sets = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MeshTextureDescriptorCache"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool lives on.</param>
    /// <param name="layout">The two-sampler descriptor set layout the sets are allocated to.</param>
    /// <exception cref="GraphicsDeviceException">The descriptor pool could not be created.</exception>
    public MeshTextureDescriptorCache(Vk vk, Device device, DescriptorSetLayout layout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        _layout = layout;

        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = MaxSets * 2,
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxSets,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool");
        _pool = pool;
    }

    /// <summary>Gets the descriptor set binding a diffuse and lightmap pair, writing it on first use.</summary>
    /// <param name="diffuseId">The diffuse texture's handle id.</param>
    /// <param name="diffuse">The diffuse texture.</param>
    /// <param name="lightmapId">The lightmap texture's handle id.</param>
    /// <param name="lightmap">The lightmap texture.</param>
    /// <returns>The descriptor set binding both textures.</returns>
    /// <exception cref="GraphicsDeviceException">A set could not be allocated.</exception>
    public DescriptorSet Get(uint diffuseId, VulkanTexture diffuse, uint lightmapId, VulkanTexture lightmap)
    {
        ArgumentNullException.ThrowIfNull(diffuse);
        ArgumentNullException.ThrowIfNull(lightmap);

        (uint, uint) key = (diffuseId, lightmapId);
        if (_sets.TryGetValue(key, out DescriptorSet existing))
        {
            return existing;
        }

        DescriptorSetLayout layout = _layout;
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };

        DescriptorSet set;
        VkCheck.Ok(_vk.AllocateDescriptorSets(_device, &allocInfo, &set), "vkAllocateDescriptorSets");

        DescriptorImageInfo* images = stackalloc DescriptorImageInfo[2];
        images[0] = new DescriptorImageInfo
        {
            Sampler = diffuse.Sampler,
            ImageView = diffuse.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        images[1] = new DescriptorImageInfo
        {
            Sampler = lightmap.Sampler,
            ImageView = lightmap.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &images[0],
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &images[1],
        };

        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
        _sets[key] = set;
        return set;
    }

    /// <summary>Frees every cached set that named a destroyed texture.</summary>
    /// <param name="textureId">The texture's handle id.</param>
    public void Forget(uint textureId)
    {
        List<(uint Diffuse, uint Lightmap)> stale = [];
        foreach ((uint Diffuse, uint Lightmap) key in _sets.Keys)
        {
            if (key.Diffuse == textureId || key.Lightmap == textureId)
            {
                stale.Add(key);
            }
        }

        foreach ((uint Diffuse, uint Lightmap) key in stale)
        {
            DescriptorSet set = _sets[key];
            _sets.Remove(key);
            _vk.FreeDescriptorSets(_device, _pool, 1, &set);
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
        _sets.Clear();
        _vk.DestroyDescriptorPool(_device, _pool, null);
    }
}

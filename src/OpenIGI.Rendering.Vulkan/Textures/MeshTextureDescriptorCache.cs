using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>
/// Hands out the descriptor set that binds a draw's diffuse and lightmap textures, one per distinct
/// pair, reused while both textures live.
/// </summary>
/// <remarks>
/// <para>
/// The world shader samples two textures, so a draw binds a set that points at both. A model's records
/// pair the same textures over and over, so the set for a pair is written once and cached, keyed by the
/// two handle ids together. When either texture is destroyed, every set that named it is given back.
/// </para>
/// <para>
/// <b>The pool grows.</b> How many sets a level needs is the number of distinct pairs it draws, and a
/// lightmap is per placement rather than per material -- so the count scales with the number of objects
/// in the level, not with the size of its texture set. One fixed pool is therefore a ceiling that a
/// large enough level walks into, and walking into it is fatal: the allocation fails with
/// <see cref="Result.ErrorOutOfPoolMemory"/> in the middle of drawing the world. Pools are chained
/// instead, a new one opened whenever the last is full, so the only real limit is memory.
/// </para>
/// </remarks>
internal sealed unsafe class MeshTextureDescriptorCache : IDisposable
{
    private const uint SetsPerPool = 4096;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DescriptorSetLayout _layout;
    private readonly bool _combined;
    private readonly List<DescriptorPool> _pools = [];
    private readonly Dictionary<(uint Diffuse, uint Lightmap, uint Detail), Entry> _sets = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MeshTextureDescriptorCache"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool lives on.</param>
    /// <param name="layout">The descriptor set layout the sets are allocated to.</param>
    /// <param name="combined">
    /// Whether to write one combined image sampler per texture rather than a sampled image and a
    /// sampler apart.
    /// </param>
    /// <exception cref="GraphicsDeviceException">The descriptor pool could not be created.</exception>
    /// <remarks>
    /// Both shapes exist while the migration to the shader library is under way. A module in that
    /// library must declare its texture and its sampler separately -- Slang's combined sampler takes
    /// two binding slots on the WGSL target and collides with the next declaration -- so the forward
    /// mesh pipeline binds the split shape, and the geometry, shadow, card and glass pipelines, still
    /// compiled from GLSL, bind the combined one. The flag and the combined branch go when they move.
    /// </remarks>
    public MeshTextureDescriptorCache(Vk vk, Device device, DescriptorSetLayout layout, bool combined)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        _layout = layout;
        _combined = combined;

        AddPool();
    }

    /// <summary>Gets the descriptor set binding a diffuse and lightmap pair, writing it on first use.</summary>
    /// <param name="diffuseId">The diffuse texture's handle id.</param>
    /// <param name="diffuse">The diffuse texture.</param>
    /// <param name="lightmapId">The lightmap texture's handle id.</param>
    /// <param name="lightmap">The lightmap texture.</param>
    /// <param name="detailId">The surface-detail texture's handle id.</param>
    /// <param name="detail">
    /// The surface-detail texture. A diffuse with no map of its own is given the neutral one, whose
    /// values are the identity of everything the detail block does, so no draw needs a flag saying which
    /// it has.
    /// </param>
    /// <returns>The descriptor set binding all three textures.</returns>
    /// <exception cref="GraphicsDeviceException">A set could not be allocated.</exception>
    public DescriptorSet Get(
        uint diffuseId,
        VulkanTexture diffuse,
        uint lightmapId,
        VulkanTexture lightmap,
        uint detailId,
        VulkanTexture detail)
    {
        ArgumentNullException.ThrowIfNull(diffuse);
        ArgumentNullException.ThrowIfNull(lightmap);
        ArgumentNullException.ThrowIfNull(detail);

        (uint, uint, uint) key = (diffuseId, lightmapId, detailId);
        if (_sets.TryGetValue(key, out Entry existing))
        {
            return existing.Set;
        }

        (DescriptorPool owner, DescriptorSet set) = Allocate();

        VulkanTexture[] textures = [diffuse, lightmap, detail];

        if (_combined)
        {
            DescriptorImageInfo* images = stackalloc DescriptorImageInfo[3];
            WriteDescriptorSet* combinedWrites = stackalloc WriteDescriptorSet[3];
            for (uint slot = 0; slot < 3; slot++)
            {
                images[slot] = new DescriptorImageInfo
                {
                    Sampler = textures[slot].Sampler,
                    ImageView = textures[slot].View,
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                };
                combinedWrites[slot] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = slot,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &images[slot],
                };
            }

            _vk.UpdateDescriptorSets(_device, 3, combinedWrites, 0, null);
        }
        else
        {
            // The image at the even slot and its sampler at the odd one after it.
            DescriptorImageInfo* images = stackalloc DescriptorImageInfo[6];
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[6];
            for (uint slot = 0; slot < 3; slot++)
            {
                uint image = slot * 2;
                uint sampler = image + 1;

                images[image] = new DescriptorImageInfo
                {
                    ImageView = textures[slot].View,
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                };
                images[sampler] = new DescriptorImageInfo
                {
                    Sampler = textures[slot].Sampler,
                };

                writes[image] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = image,
                    DescriptorType = DescriptorType.SampledImage,
                    DescriptorCount = 1,
                    PImageInfo = &images[image],
                };
                writes[sampler] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = sampler,
                    DescriptorType = DescriptorType.Sampler,
                    DescriptorCount = 1,
                    PImageInfo = &images[sampler],
                };
            }

            _vk.UpdateDescriptorSets(_device, 6, writes, 0, null);
        }
        _sets[key] = new Entry(owner, set);
        return set;
    }

    /// <summary>Frees every cached set that named a destroyed texture.</summary>
    /// <param name="textureId">The texture's handle id.</param>
    public void Forget(uint textureId)
    {
        List<(uint Diffuse, uint Lightmap, uint Detail)> stale = [];
        foreach ((uint Diffuse, uint Lightmap, uint Detail) key in _sets.Keys)
        {
            if (key.Diffuse == textureId || key.Lightmap == textureId || key.Detail == textureId)
            {
                stale.Add(key);
            }
        }

        foreach ((uint Diffuse, uint Lightmap, uint Detail) key in stale)
        {
            Entry entry = _sets[key];
            _sets.Remove(key);

            // Back to the pool it actually came from: freeing a set against a different pool is
            // undefined behaviour, and the whole point of chaining pools is that they differ.
            DescriptorSet set = entry.Set;
            _vk.FreeDescriptorSets(_device, entry.Pool, 1, &set);
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
        foreach (DescriptorPool pool in _pools)
        {
            _vk.DestroyDescriptorPool(_device, pool, null);
        }

        _pools.Clear();
    }

    // Allocate from the newest pool, opening another when it is full. A full pool answers
    // ErrorOutOfPoolMemory; one whose freed sets have left it too fragmented to satisfy the request
    // answers ErrorFragmentedPool. Both mean the same thing here -- this pool is done, start another --
    // and neither is a failure worth ending the frame over.
    private (DescriptorPool Pool, DescriptorSet Set) Allocate()
    {
        DescriptorSetLayout layout = _layout;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            DescriptorPool pool = _pools[^1];
            DescriptorSetAllocateInfo allocInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };

            DescriptorSet set;
            Result result = _vk.AllocateDescriptorSets(_device, &allocInfo, &set);
            if (result == Result.Success)
            {
                return (pool, set);
            }

            if (result is not (Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool))
            {
                VkCheck.Ok(result, "vkAllocateDescriptorSets");
            }

            AddPool();
        }

        throw new GraphicsDeviceException(
            "vkAllocateDescriptorSets failed: a freshly created descriptor pool could not satisfy one set.");
    }

    private void AddPool()
    {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = _combined ? DescriptorType.CombinedImageSampler : DescriptorType.SampledImage,
            DescriptorCount = SetsPerPool * 3,
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.Sampler,
            DescriptorCount = SetsPerPool * 3,
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = SetsPerPool,
            PoolSizeCount = _combined ? 1u : 2u,
            PPoolSizes = poolSizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool");
        _pools.Add(pool);
    }

    /// <summary>A cached set, with the pool it must be given back to.</summary>
    private readonly record struct Entry(DescriptorPool Pool, DescriptorSet Set);
}

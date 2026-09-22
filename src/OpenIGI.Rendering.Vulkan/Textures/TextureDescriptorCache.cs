using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>
/// Hands out the descriptor set that binds a texture to the sampler slot, one per texture, reused for
/// the texture's whole life.
/// </summary>
/// <remarks>
/// <para>
/// A draw does not bind a texture directly in Vulkan; it binds a descriptor set that points at one. The
/// contents of such a set do not change once written — a texture's view and sampler are fixed — so a set
/// is allocated and written the first time a texture is drawn and then kept, keyed by the texture's
/// handle. A draw run of the same texture costs one lookup rather than a descriptor update.
/// </para>
/// <para>
/// The pool is created able to free individual sets, so a destroyed texture gives its set back rather
/// than holding a slot until the pool is reset.
/// </para>
/// <para>
/// A set binds the image at zero and its sampler at one, separately, rather than one combined image
/// sampler. That is not a preference: the shader library's modules must declare a texture and a
/// sampler apart, because Slang's combined sampler expands into two binding slots on the WGSL target
/// and collides with whatever is declared next, so the SPIR-V those modules compile to expects the
/// pair. Every 2D draw in the frame reads sets from here, which is why the sprite, the
/// distance-field text and the loading backdrop had to move together.
/// </para>
/// </remarks>
internal sealed unsafe class TextureDescriptorCache : IDisposable
{
    private const uint MaxSets = 4096;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorPool _pool;
    private readonly Dictionary<uint, DescriptorSet> _sets = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TextureDescriptorCache"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool lives on.</param>
    /// <param name="layout">The descriptor set layout the sets are allocated to.</param>
    /// <exception cref="GraphicsDeviceException">The descriptor pool could not be created.</exception>
    public TextureDescriptorCache(Vk vk, Device device, DescriptorSetLayout layout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
        _device = device;
        _layout = layout;

        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.SampledImage,
            DescriptorCount = MaxSets,
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.Sampler,
            DescriptorCount = MaxSets,
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxSets,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool");
        _pool = pool;
    }

    /// <summary>Gets the descriptor set binding a texture, allocating and writing it on first use.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>The set binding <paramref name="texture"/>'s image at zero and its sampler at one.</returns>
    /// <exception cref="GraphicsDeviceException">A set could not be allocated.</exception>
    public DescriptorSet Get(uint textureId, VulkanTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_sets.TryGetValue(textureId, out DescriptorSet existing))
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

        DescriptorImageInfo imageInfo = new()
        {
            ImageView = texture.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        DescriptorImageInfo samplerInfo = new()
        {
            Sampler = texture.Sampler,
        };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            PImageInfo = &imageInfo,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 1,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = 1,
            PImageInfo = &samplerInfo,
        };

        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
        _sets[textureId] = set;
        return set;
    }

    /// <summary>Frees the descriptor set of a destroyed texture, if one was allocated.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    public void Forget(uint textureId)
    {
        if (_sets.Remove(textureId, out DescriptorSet set))
        {
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

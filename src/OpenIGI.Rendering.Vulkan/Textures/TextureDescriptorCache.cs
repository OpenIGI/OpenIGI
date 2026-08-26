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

        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = MaxSets,
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

    /// <summary>Gets the descriptor set binding a texture, allocating and writing it on first use.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>The descriptor set that binds <paramref name="texture"/> at binding zero.</returns>
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
            Sampler = texture.Sampler,
            ImageView = texture.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        WriteDescriptorSet write = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo,
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
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

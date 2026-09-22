using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>Builds a descriptor set layout out of what a compiled module says it binds.</summary>
/// <remarks>
/// <para>
/// A layout written by hand is a second copy of the shader's own binding table, and the two drift: a
/// binding added to the source, a number shifted by a split, a kind changed from a combined sampler to
/// a texture and a sampler. Vulkan reports none of it — a set laid out differently from the shader that
/// reads it draws a wrong picture in silence. The reflection the build commits beside each variant is
/// the one description of that table, so the layout is derived from it rather than kept in step with it.
/// </para>
/// <para>
/// Only the set a variant actually declares comes out, holes and all: the traced form of the shading
/// binds a visibility texture and the world where the software form binds neither, and a layout with
/// gaps where those numbers were is exactly right. What is NOT derived is stage visibility — Slang
/// reports module-scope parameters for every entry point whether or not it touches them, so the caller
/// states the stages, which it knows.
/// </para>
/// </remarks>
internal static unsafe class ReflectedSetLayout
{
    /// <summary>The descriptor type a binding of this kind is written as.</summary>
    /// <param name="kind">What the module declared.</param>
    /// <returns>The Vulkan descriptor type.</returns>
    /// <exception cref="NotSupportedException">The kind has no single-descriptor form.</exception>
    public static DescriptorType TypeOf(ShaderBindingKind kind) => kind switch
    {
        ShaderBindingKind.Texture => DescriptorType.SampledImage,
        ShaderBindingKind.Sampler => DescriptorType.Sampler,
        ShaderBindingKind.StorageTexture => DescriptorType.StorageImage,
        ShaderBindingKind.UniformBuffer => DescriptorType.UniformBuffer,
        ShaderBindingKind.StorageBuffer => DescriptorType.StorageBuffer,
        ShaderBindingKind.AccelerationStructure => DescriptorType.AccelerationStructureKhr,
        ShaderBindingKind.CombinedImageSampler => DescriptorType.CombinedImageSampler,
        ShaderBindingKind.InputAttachment => DescriptorType.InputAttachment,
        _ => throw new NotSupportedException(
            $"A {kind} binding has no descriptor a set layout can declare for it."),
    };

    /// <summary>How many descriptors of each type one set of this layout holds.</summary>
    /// <param name="reflection">The compiled variant whose bindings to count.</param>
    /// <param name="group">Which descriptor set to count.</param>
    /// <returns>A count per descriptor type, for sizing a pool.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reflection"/> is <see langword="null"/>.</exception>
    public static Dictionary<DescriptorType, uint> Counts(ShaderReflection reflection, int group = 0)
    {
        ArgumentNullException.ThrowIfNull(reflection);

        Dictionary<DescriptorType, uint> counts = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Group != group)
            {
                continue;
            }

            DescriptorType type = TypeOf(binding.Kind);
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        return counts;
    }

    /// <summary>Creates the layout for one descriptor set of a compiled variant.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the layout lives on.</param>
    /// <param name="reflection">The compiled variant whose bindings to declare.</param>
    /// <param name="stages">Which stages may read the set.</param>
    /// <param name="name">What to call the layout if creating it fails.</param>
    /// <param name="group">Which descriptor set to declare.</param>
    /// <returns>The created layout, which the caller owns.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reflection"/> is <see langword="null"/>.</exception>
    /// <exception cref="GraphicsDeviceException">The layout could not be created.</exception>
    public static DescriptorSetLayout Create(
        Vk vk,
        Device device,
        ShaderReflection reflection,
        ShaderStageFlags stages,
        string name,
        int group = 0)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(reflection);

        List<DescriptorSetLayoutBinding> declared = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Group != group)
            {
                continue;
            }

            declared.Add(new DescriptorSetLayoutBinding
            {
                Binding = (uint)binding.Binding,
                DescriptorType = TypeOf(binding.Kind),
                DescriptorCount = 1,
                StageFlags = stages,
            });
        }

        DescriptorSetLayout layout;
        DescriptorSetLayoutBinding[] bindings = [.. declared];
        fixed (DescriptorSetLayoutBinding* first = bindings)
        {
            DescriptorSetLayoutCreateInfo createInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = first,
            };

            VkCheck.Ok(
                vk.CreateDescriptorSetLayout(device, &createInfo, null, &layout),
                $"vkCreateDescriptorSetLayout ({name})");
        }

        return layout;
    }
}

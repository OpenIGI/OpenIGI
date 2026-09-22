using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// Turns what a module declares into the bind group layout entries that declare it to the device.
/// </summary>
/// <remarks>
/// <para>
/// A layout written out by hand is a second statement of what the shader already says, and a second
/// statement can disagree: a layout naming a binding the module does not have is refused, and one that
/// merely orders them differently binds the wrong texture to the wrong slot and shades something
/// plausible. So the layout is read rather than written, from the reflection the build commits beside
/// each compiled variant.
/// </para>
/// <para>
/// What a layout has to say is exactly what the reflection records: the kind, the view dimension,
/// whether a texture is an array of slices, what is read out of it, whether a sampler compares, and a
/// storage texture's format and access. The last four are things a Vulkan descriptor set does not distinguish at all, so they
/// are only ever wrong in the browser — which is why they are read from the shader rather than assumed.
/// </para>
/// <para>
/// The push block is left out. It is a binding on one target and not on the other, and which group it
/// lands in is the backend's business; the RHI takes push data as push data.
/// </para>
/// </remarks>
public static class ShaderBindingLayout
{
    /// <summary>The layout entry for one declared binding.</summary>
    /// <param name="binding">What the module declares.</param>
    /// <param name="stages">Which stages may read it.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The binding is of a kind the RHI has no slot for.</exception>
    public static RhiBindGroupLayoutEntry EntryFor(ShaderBinding binding, RhiShaderStages stages)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Kind switch
        {
            ShaderBindingKind.Texture => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.SampledTexture,
                stages,
                ViewDimension: Dimension(binding),
                SampleType: SampleType(binding)),

            // A comparison sampler is a different slot type, not a different sampler in the same slot:
            // a layout that calls one an ordinary sampler binds nothing.
            ShaderBindingKind.Sampler => new RhiBindGroupLayoutEntry(
                binding.Binding,
                binding.Comparison ? RhiBindingType.ComparisonSampler : RhiBindingType.Sampler,
                stages),

            // The access the module declared, not an assumption: a layout that says read-write about a
            // write-only binding binds nothing, and the failure names neither the module nor the texture.
            ShaderBindingKind.StorageTexture => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.StorageTexture,
                stages,
                Access(binding),
                StorageFormat(binding),
                Dimension(binding)),

            ShaderBindingKind.UniformBuffer => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.UniformBuffer,
                stages),

            ShaderBindingKind.StorageBuffer => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.StorageBuffer,
                stages),

            ShaderBindingKind.AccelerationStructure => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.AccelerationStructure,
                stages),

            _ => throw new NotSupportedException(
                $"A pass cannot bind '{binding.Name}', which is a {binding.Kind}."),
        };
    }

    /// <summary>The entries of one descriptor set of a compiled variant, in binding order.</summary>
    /// <param name="reflection">The compiled variant to read.</param>
    /// <param name="stages">Which stages may read the group.</param>
    /// <param name="group">Which group to take.</param>
    /// <returns>The entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reflection"/> is <see langword="null"/>.</exception>
    public static List<RhiBindGroupLayoutEntry> EntriesFor(
        ShaderReflection reflection,
        RhiShaderStages stages,
        int group = 0)
    {
        ArgumentNullException.ThrowIfNull(reflection);

        List<RhiBindGroupLayoutEntry> entries = [];

        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Group == group && !IsPushBlock(reflection, binding))
            {
                entries.Add(EntryFor(binding, stages));
            }
        }

        return entries;
    }

    /// <summary>Whether a binding is the module's push block rather than one of its resources.</summary>
    /// <param name="reflection">The compiled variant.</param>
    /// <param name="binding">The binding asked about.</param>
    /// <returns><see langword="true"/> when it is the push block.</returns>
    /// <remarks>
    /// By the slot the reflection states rather than by the block's name, because a module names its
    /// block whatever suits it and a name match would be a rule kept in two places.
    /// </remarks>
    public static bool IsPushBlock(ShaderReflection reflection, ShaderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(binding);

        return reflection.PushBlock is { } push
            && binding.Group == push.Group
            && binding.Binding == push.Binding;
    }

    /// <summary>What a shader reads out of a sampled texture, as the module declared it.</summary>
    private static RhiTextureSampleType SampleType(ShaderBinding binding) => binding.SampleType switch
    {
        ShaderSampleType.Depth => RhiTextureSampleType.Depth,
        ShaderSampleType.Uint => RhiTextureSampleType.Uint,
        ShaderSampleType.Sint => RhiTextureSampleType.Sint,
        _ => RhiTextureSampleType.Float,
    };

    /// <summary>How a storage binding may be reached, as the module declared it.</summary>
    private static RhiStorageAccess Access(ShaderBinding binding) => binding.StorageAccess switch
    {
        ShaderStorageAccess.ReadWrite => RhiStorageAccess.ReadWrite,
        _ => RhiStorageAccess.WriteOnly,
    };

    /// <summary>How a texture binding is addressed, from what the module declared it as.</summary>
    private static RhiTextureViewDimension Dimension(ShaderBinding binding) => (binding.Dimensions, binding.Arrayed) switch
    {
        (3, _) => RhiTextureViewDimension.Texture3D,
        (2, true) => RhiTextureViewDimension.Texture2DArray,
        _ => RhiTextureViewDimension.Texture2D,
    };

    /// <summary>
    /// The format a storage binding is declared with, as the module <em>authored</em> it.
    /// </summary>
    /// <remarks>
    /// The authored one rather than what a target emitted, because a backend that widens knows its own
    /// widening and reads it back from the same library. Stating the widened one here would make this
    /// the second place that rule lives.
    /// </remarks>
    private static RhiTextureFormat StorageFormat(ShaderBinding binding) => binding.StorageFormat switch
    {
        ShaderStorageFormat.R8Unorm => RhiTextureFormat.R8Unorm,
        ShaderStorageFormat.Rg8Unorm => RhiTextureFormat.Rg8Unorm,
        ShaderStorageFormat.Rg16Float => RhiTextureFormat.Rg16Float,
        ShaderStorageFormat.Rgba16Float => RhiTextureFormat.Rgba16Float,
        ShaderStorageFormat.R32Float => RhiTextureFormat.R32Float,
        ShaderStorageFormat.R32Uint => RhiTextureFormat.R32Uint,
        ShaderStorageFormat.Rgba32Float => RhiTextureFormat.Rgba32Float,
        _ => throw new NotSupportedException(
            $"The storage binding '{binding.Name}' declares the format {binding.StorageFormat}, which has "
            + "no counterpart here. A storage format is what a bind group layout is validated against, so "
            + "a guess would bind nothing."),
    };
}

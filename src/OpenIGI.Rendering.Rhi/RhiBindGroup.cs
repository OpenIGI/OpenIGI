namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// One slot of a bind group layout: what goes there, which stages see it, and how it is accessed.
/// </summary>
/// <param name="Binding">The binding number the shader declared.</param>
/// <param name="Type">What the slot holds.</param>
/// <param name="Visibility">Which stages may read it.</param>
/// <param name="StorageAccess">
/// How a <see cref="RhiBindingType.StorageTexture"/> is accessed. Ignored for every other type.
/// </param>
/// <param name="StorageFormat">
/// The format a <see cref="RhiBindingType.StorageTexture"/> is declared with, which must match the
/// shader's own declaration. Ignored for every other type.
/// </param>
/// <param name="ViewDimension">How a texture slot is addressed.</param>
/// <param name="HasDynamicOffset">
/// Whether a buffer slot's offset is supplied when the group is bound rather than when it is created.
/// One group can then serve every frame in flight, which is how the per-frame uniform ring works.
/// </param>
/// <param name="MultisampledTexture">Whether a sampled texture slot takes a multisampled texture.</param>
/// <param name="SampleType">
/// What a shader reads out of a <see cref="RhiBindingType.SampledTexture"/>. Stated because a backend
/// that validates the layout rejects a depth texture bound to a slot that says float, and the layout is
/// built before anything is bound to it — so it cannot be inferred from what arrives. Ignored for every
/// other type.
/// </param>
public readonly record struct RhiBindGroupLayoutEntry(
    int Binding,
    RhiBindingType Type,
    RhiShaderStages Visibility,
    RhiStorageAccess StorageAccess = RhiStorageAccess.WriteOnly,
    RhiTextureFormat StorageFormat = RhiTextureFormat.Undefined,
    RhiTextureViewDimension ViewDimension = RhiTextureViewDimension.Texture2D,
    bool HasDynamicOffset = false,
    bool MultisampledTexture = false,
    RhiTextureSampleType SampleType = RhiTextureSampleType.Float);

/// <summary>What a shader reads out of a sampled texture.</summary>
/// <remarks>
/// A bind group layout states it and is validated against it. Vulkan does not distinguish these at all
/// — every one of them is a sampled image in a descriptor set — so a wrong one is invisible there and
/// fatal in a browser.
/// </remarks>
public enum RhiTextureSampleType
{
    /// <summary>Floating point, the ordinary case.</summary>
    Float,

    /// <summary>A depth texture, which is what a comparison lookup reads.</summary>
    Depth,

    /// <summary>Unsigned integer.</summary>
    Uint,

    /// <summary>Signed integer.</summary>
    Sint,
}

/// <summary>The shape of a bind group.</summary>
/// <param name="Entries">Its slots, which need not be in binding order.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public sealed record RhiBindGroupLayoutDescriptor(
    IReadOnlyList<RhiBindGroupLayoutEntry> Entries,
    string Label = "");

/// <summary>What one slot of a bind group is filled with.</summary>
/// <remarks>
/// Exactly one of the three resources is set, matching the layout entry's type. The alternative — three
/// descriptor types and an overload each — costs more to read than a rule stated once and checked.
/// </remarks>
/// <param name="Binding">The binding number, which must name a slot of the layout.</param>
/// <param name="Buffer">The buffer, for a buffer slot.</param>
/// <param name="Offset">Where in the buffer the binding starts.</param>
/// <param name="Size">How much of the buffer the binding covers, or zero for the rest of it.</param>
/// <param name="TextureView">The view, for a texture slot.</param>
/// <param name="Sampler">The sampler, for a sampler slot.</param>
public readonly record struct RhiBindGroupEntry(
    int Binding,
    IRhiBuffer? Buffer = null,
    ulong Offset = 0,
    ulong Size = 0,
    IRhiTextureView? TextureView = null,
    IRhiSampler? Sampler = null)
{
    /// <summary>Binds a buffer, or a range of one.</summary>
    /// <param name="binding">The binding number.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the binding starts.</param>
    /// <param name="size">How much it covers, or zero for the rest.</param>
    /// <returns>The entry.</returns>
    public static RhiBindGroupEntry Of(int binding, IRhiBuffer buffer, ulong offset = 0, ulong size = 0) =>
        new(binding, Buffer: buffer, Offset: offset, Size: size);

    /// <summary>Binds a texture view.</summary>
    /// <param name="binding">The binding number.</param>
    /// <param name="view">The view.</param>
    /// <returns>The entry.</returns>
    public static RhiBindGroupEntry Of(int binding, IRhiTextureView view) =>
        new(binding, TextureView: view);

    /// <summary>Binds a sampler.</summary>
    /// <param name="binding">The binding number.</param>
    /// <param name="sampler">The sampler.</param>
    /// <returns>The entry.</returns>
    public static RhiBindGroupEntry Of(int binding, IRhiSampler sampler) =>
        new(binding, Sampler: sampler);
}

/// <summary>A set of resources bound together.</summary>
/// <param name="Layout">The shape they are bound against.</param>
/// <param name="Entries">What fills each slot.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public sealed record RhiBindGroupDescriptor(
    IRhiBindGroupLayout Layout,
    IReadOnlyList<RhiBindGroupEntry> Entries,
    string Label = "");

/// <summary>What a pipeline's bind groups and push data look like.</summary>
/// <param name="BindGroupLayouts">The groups, in the order a shader's sets are numbered.</param>
/// <param name="PushDataBytes">
/// How many bytes <c>SetPushData</c> may carry, or zero for a pipeline that takes none. Must not exceed
/// <see cref="RhiLimits.MaxPushDataBytes"/>.
/// </param>
/// <param name="PushDataVisibility">Which stages read the push data.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public sealed record RhiPipelineLayoutDescriptor(
    IReadOnlyList<IRhiBindGroupLayout> BindGroupLayouts,
    int PushDataBytes = 0,
    RhiShaderStages PushDataVisibility = RhiShaderStages.All,
    string Label = "");

using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Compute;

/// <summary>
/// A compute module as a pipeline ready to dispatch, with its bind group layouts derived from what the
/// module itself declares.
/// </summary>
/// <remarks>
/// <para>
/// Every compute pass in this renderer has the same shape: a layout matching the module's bindings, a
/// pipeline, a group filled with this frame's textures, and a dispatch sized from the target. Written
/// out by hand that is thirty lines a pass, most of it a second statement of what the shader already
/// says — and a second statement that can disagree, which is the interesting failure: a layout naming a
/// binding the module does not have is refused, and one that merely orders them differently binds the
/// wrong texture to the wrong slot and shades something plausible.
/// </para>
/// <para>
/// So the layout is read rather than written. The library records what each module binds, at which
/// group and index, of what kind and — for a storage texture — in what format, which is exactly the set
/// of distinctions a layout has to make.
/// </para>
/// <para>
/// The push block is left out of the derived layouts. It is a binding on one target and not on the
/// other, and which group it lands in is the backend's business: the RHI takes push data as push data
/// and each side spends what it must on it.
/// </para>
/// </remarks>
public sealed class ComputeProgram : IDisposable
{
    private readonly IRhiShaderModule _module;
    private readonly IRhiBindGroupLayout[] _groups;
    private readonly Dictionary<string, ShaderBinding> _byName = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ComputeProgram"/> class.</summary>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="module">Which module of the library.</param>
    /// <param name="variant">Which compiled form of it.</param>
    /// <param name="constants">Constants to fix at creation, or null for the module's own defaults.</param>
    /// <exception cref="InvalidOperationException">The module has no compute entry point.</exception>
    public ComputeProgram(
        IRhiDevice device,
        ShaderModuleId module,
        ShaderVariant variant = default,
        IReadOnlyDictionary<int, RhiConstant>? constants = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        Module = module;
        Reflection = ShaderCatalog.GetReflection(module, variant);

        ShaderEntryPoint entry = Reflection.EntryPointFor(ShaderStage.Compute);
        EntryPoint = entry.Name;

        IReadOnlyList<int>? size = entry.WorkgroupSize;
        WorkgroupSize = size is { Count: 3 } ? (size[0], size[1], size[2]) : (1, 1, 1);

        PushDataBytes = Reflection.PushBlock?.Size ?? 0;

        foreach (ShaderBinding binding in Reflection.Bindings)
        {
            if (!IsPushBlock(binding))
            {
                _byName[binding.Name] = binding;
            }
        }

        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(module, variant, module.ToString()));
        _groups = DeriveLayouts(device);

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            _groups,
            PushDataBytes,
            RhiShaderStages.Compute,
            module.ToString()));

        Pipeline = device.CreateComputePipeline(new RhiComputePipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, EntryPoint, constants),
            module.ToString()));
    }

    /// <summary>Gets which module this is.</summary>
    public ShaderModuleId Module { get; }

    /// <summary>Gets what the module declares.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets the compute entry point's name.</summary>
    public string EntryPoint { get; }

    /// <summary>Gets how many invocations one workgroup has, on each axis.</summary>
    public (int X, int Y, int Z) WorkgroupSize { get; }

    /// <summary>Gets how many bytes of push data the module reads.</summary>
    public int PushDataBytes { get; }

    /// <summary>Gets the bind group layouts, in group order.</summary>
    public IReadOnlyList<IRhiBindGroupLayout> GroupLayouts => _groups;

    /// <summary>Gets the pipeline layout.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the pipeline.</summary>
    public IRhiComputePipeline Pipeline { get; }

    /// <summary>Where the module binds one of its resources.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <returns>Its group and binding.</returns>
    /// <exception cref="ArgumentException">The module binds nothing of that name.</exception>
    /// <remarks>
    /// By name rather than by number so that a shader which gains a binding does not silently shift
    /// every one after it. The name is the thing a reader of both sides can match up.
    /// </remarks>
    public (int Group, int Binding) Slot(string name)
    {
        if (_byName.TryGetValue(name, out ShaderBinding? binding))
        {
            return (binding.Group, binding.Binding);
        }

        throw new ArgumentException(
            $"The module {Module} binds nothing called '{name}'. It binds: {string.Join(", ", _byName.Keys.Order())}.",
            nameof(name));
    }

    /// <summary>How many workgroups cover a region.</summary>
    /// <param name="width">How many invocations are wanted along x.</param>
    /// <param name="height">Along y.</param>
    /// <param name="depth">Along z.</param>
    /// <returns>The dispatch.</returns>
    /// <remarks>
    /// Rounded up, so the last workgroup covers a partial edge rather than leaving it unwritten — a
    /// shader is expected to discard the invocations past the end, which every one here does.
    /// </remarks>
    public (int X, int Y, int Z) GroupsFor(int width, int height = 1, int depth = 1) => (
        Math.Max(1, Divide(width, WorkgroupSize.X)),
        Math.Max(1, Divide(height, WorkgroupSize.Y)),
        Math.Max(1, Divide(depth, WorkgroupSize.Z)));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Pipeline.Dispose();
        Layout.Dispose();

        foreach (IRhiBindGroupLayout group in _groups)
        {
            group.Dispose();
        }

        _module.Dispose();
    }

    /// <summary>What a layout entry for one declared binding is.</summary>
    /// <param name="binding">What the module declares.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="NotSupportedException">The binding is of a kind no compute pass uses.</exception>
    public static RhiBindGroupLayoutEntry EntryFor(ShaderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Kind switch
        {
            ShaderBindingKind.Texture => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.SampledTexture,
                RhiShaderStages.Compute,
                ViewDimension: Dimension(binding)),

            ShaderBindingKind.Sampler => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.Sampler,
                RhiShaderStages.Compute),

            // Read-write, because that is what a storage texture in this library is: the modules that
            // only read one declare it as a sampled texture instead.
            ShaderBindingKind.StorageTexture => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.StorageTexture,
                RhiShaderStages.Compute,
                RhiStorageAccess.ReadWrite,
                StorageFormat(binding),
                Dimension(binding)),

            ShaderBindingKind.UniformBuffer => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.UniformBuffer,
                RhiShaderStages.Compute),

            ShaderBindingKind.StorageBuffer => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.StorageBuffer,
                RhiShaderStages.Compute),

            ShaderBindingKind.AccelerationStructure => new RhiBindGroupLayoutEntry(
                binding.Binding,
                RhiBindingType.AccelerationStructure,
                RhiShaderStages.Compute),

            _ => throw new NotSupportedException(
                $"A compute pass cannot bind '{binding.Name}', which is a {binding.Kind}."),
        };
    }

    /// <summary>How a texture binding is addressed, from how many dimensions it declared.</summary>
    private static RhiTextureViewDimension Dimension(ShaderBinding binding) => binding.Dimensions switch
    {
        3 => RhiTextureViewDimension.Texture3D,
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

    /// <summary>Whether a binding is the push block rather than a resource of the module's own.</summary>
    /// <remarks>
    /// Matched against where the reflection says the block is, not against its name: the block is a real
    /// binding on one target and push constants on the other, and a name that happened to contain the
    /// word would drop a texture from the layout — which binds nothing and reports nothing.
    /// </remarks>
    private bool IsPushBlock(ShaderBinding binding) =>
        Reflection.PushBlock is { } push
        && binding.Group == push.Group
        && binding.Binding == push.Binding;

    private static int Divide(int value, int by) => (value + by - 1) / by;

    private IRhiBindGroupLayout[] DeriveLayouts(IRhiDevice device)
    {
        Dictionary<int, List<RhiBindGroupLayoutEntry>> groups = [];

        foreach (ShaderBinding binding in Reflection.Bindings)
        {
            if (IsPushBlock(binding))
            {
                continue;
            }

            if (!groups.TryGetValue(binding.Group, out List<RhiBindGroupLayoutEntry>? entries))
            {
                entries = [];
                groups[binding.Group] = entries;
            }

            entries.Add(EntryFor(binding));
        }

        int count = groups.Count == 0 ? 0 : groups.Keys.Max() + 1;
        IRhiBindGroupLayout[] layouts = new IRhiBindGroupLayout[count];

        for (int group = 0; group < count; group++)
        {
            layouts[group] = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
                groups.TryGetValue(group, out List<RhiBindGroupLayoutEntry>? entries) ? entries : [],
                $"{Module}-{group}"));
        }

        return layouts;
    }
}

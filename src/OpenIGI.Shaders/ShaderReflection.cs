using System.Text.Json.Serialization;

namespace OpenIGI.Shaders;

/// <summary>What a resource a shader binds actually is.</summary>
/// <remarks>
/// The distinctions a bind-group or descriptor-set layout has to make, and no more. Slang reports a
/// richer type graph; the build reduces it to this because the backends only ever branch on these.
/// </remarks>
public enum ShaderBindingKind
{
    /// <summary>A sampled texture, of any dimension.</summary>
    Texture,

    /// <summary>A sampler, declared separately from the texture it is used with.</summary>
    Sampler,

    /// <summary>A read-write storage texture.</summary>
    StorageTexture,

    /// <summary>A uniform (constant) buffer.</summary>
    UniformBuffer,

    /// <summary>A storage (structured or byte-address) buffer.</summary>
    StorageBuffer,

    /// <summary>An acceleration structure, bound only on a backend that ray traces.</summary>
    AccelerationStructure,

    /// <summary>A texture and its sampler in one descriptor, which WGSL has no form for.</summary>
    /// <remarks>
    /// Only a module compiled for SPIR-V alone may declare one. Everything that has a WGSL form
    /// declares its textures and samplers apart, because that is the only shape both targets share.
    /// </remarks>
    CombinedImageSampler,

    /// <summary>A subpass input attachment, which WebGPU has no equivalent of at all.</summary>
    InputAttachment,

    /// <summary>An unbounded array of acceleration structures, reached by index from a bindless heap.</summary>
    AccelerationStructureArray,
}

/// <summary>What one texel of a storage texture holds, as a module declares it.</summary>
/// <remarks>
/// The formats this library's modules actually declare, and the ones a compiler widens them to. An
/// unknown format is a build failure rather than a value silently dropped: a storage binding's format
/// is what a WebGPU bind group layout is validated against, so a wrong one binds nothing.
/// </remarks>
public enum ShaderStorageFormat
{
    /// <summary>Not a storage texture.</summary>
    None,

    /// <summary>One unsigned byte, normalised.</summary>
    R8Unorm,

    /// <summary>Two unsigned bytes, normalised.</summary>
    Rg8Unorm,

    /// <summary>Four unsigned bytes, normalised.</summary>
    Rgba8Unorm,

    /// <summary>One 16-bit float.</summary>
    R16Float,

    /// <summary>Two 16-bit floats.</summary>
    Rg16Float,

    /// <summary>Four 16-bit floats.</summary>
    Rgba16Float,

    /// <summary>One 32-bit float.</summary>
    R32Float,

    /// <summary>One 32-bit unsigned integer.</summary>
    R32Uint,

    /// <summary>Two 32-bit floats.</summary>
    Rg32Float,

    /// <summary>Four 32-bit floats.</summary>
    Rgba32Float,

    /// <summary>Ten bits each of red, green and blue with two of alpha.</summary>
    Rgb10A2Unorm,

    /// <summary>Eleven bits each of red and green and ten of blue, unsigned float.</summary>
    Rg11B10Ufloat,
}

/// <summary>What a shader reads out of a sampled texture.</summary>
/// <remarks>
/// A WebGPU bind group layout states it and is validated against it: a depth texture bound to a slot
/// that says float binds nothing. A Vulkan descriptor set does not distinguish them, so this is another
/// difference only the browser can find. Read out of the emitted WGSL, which spells each one in the
/// type itself.
/// </remarks>
public enum ShaderSampleType
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

/// <summary>One resource a module binds, as both targets agree it is bound.</summary>
/// <param name="Name">The name the Slang source declares.</param>
/// <param name="Group">The descriptor set on Vulkan, the bind group on WebGPU.</param>
/// <param name="Binding">The binding index within that group.</param>
/// <param name="Kind">What the resource is.</param>
/// <remarks>
/// There is deliberately no "which stages read this". Slang reports module-scope parameters for
/// every entry point whether or not that entry point touches them -- compiling with <c>-entry</c>
/// alone does not narrow it -- so the information is not available here, and a field claiming it
/// would be wrong rather than merely absent. Stage visibility stays with the code that builds the
/// layout, which knows what it binds.
/// </remarks>
/// <param name="Dimensions">
/// How many dimensions a texture has, or nought for a resource that is not one. A backend that binds a
/// placeholder where a level provides nothing has to bind one of the right shape, and a two-dimensional
/// view written into a three-dimensional slot is a device loss rather than a wrong picture.
/// </param>
/// <param name="StorageFormat">
/// What one texel of a storage texture holds, as the source declared it and as the SPIR-V carries it.
/// <see cref="ShaderStorageFormat.None"/> for everything that is not a storage texture.
/// </param>
/// <param name="WgslStorageFormat">
/// What the same binding came out as for the WGSL target, which is not always what was declared: WGSL
/// allows a storage binding on a short list of formats and the compiler widens anything narrower. It is
/// recorded because a WebGPU bind group layout is validated against the format the module DECLARES, so
/// the host has to allocate that one -- and a host that reproduced the compiler's widening rule instead
/// of reading it would be a second place for that rule to live.
/// <see cref="ShaderStorageFormat.None"/> where the variant has no WGSL form.
/// </param>
/// <param name="StorageAccess">
/// How a storage binding may be reached, as the module's own type says: a bind group layout states it
/// and is validated against what was declared, so a write-only binding described as read-write binds
/// nothing. <see cref="ShaderStorageAccess.None"/> for everything that is not a storage binding.
/// </param>
/// <param name="WgslStorageAccess">
/// How the WGSL form reaches the same binding, where that differs from <paramref name="StorageAccess"/>:
/// an image a module reads back is read-write in the SPIR-V and written only in the WGSL, which reads
/// what it held from a copy instead. <see cref="ShaderStorageAccess.None"/> where the two agree, or the
/// variant has no WGSL form.
/// </param>
/// <param name="Arrayed">
/// Whether a texture is an array of slices rather than one image. A bind group layout states the view
/// dimension and is validated against it, so an array bound as a plain texture binds nothing — and the
/// two are the same in a descriptor set, so nothing on the Vulkan side would ever have caught it.
/// </param>
/// <param name="Comparison">
/// Whether a sampler compares rather than filters: the shadow sampler, which answers whether a depth
/// is nearer than the texel instead of returning it. Read out of the emitted WGSL, which is the only
/// place it is written down — Slang's reflection reports a comparison sampler and an ordinary one
/// identically, and SPIR-V does not distinguish them either. <see langword="false"/> for a variant with
/// no WGSL form, which is a variant no WebGPU layout can be built from anyway.
/// </param>
/// <param name="SampleType">
/// What a shader reads out of a sampled texture, which a bind group layout also states. Read from the
/// emitted WGSL for the same reason the comparison flag is, and <see cref="ShaderSampleType.Float"/>
/// for everything that is not a sampled texture.
/// </param>
public sealed record ShaderBinding(
    string Name,
    int Group,
    int Binding,
    ShaderBindingKind Kind,
    int Dimensions = 0,
    ShaderStorageFormat StorageFormat = ShaderStorageFormat.None,
    ShaderStorageFormat WgslStorageFormat = ShaderStorageFormat.None,
    ShaderStorageAccess StorageAccess = ShaderStorageAccess.None,
    // Written only where the WGSL disagrees, for the same reason as the flags below.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    ShaderStorageAccess WgslStorageAccess = ShaderStorageAccess.None,
    // Written only where they are true. The committed tables are meant to be read in review, and two
    // more false flags on each of a thousand ordinary bindings would bury what they are there to say.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Arrayed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Comparison = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    ShaderSampleType SampleType = ShaderSampleType.Float);

/// <summary>How a storage binding may be reached.</summary>
/// <remarks>
/// Recorded because a bind group layout states it and is validated against what the module declared: a
/// write-only binding described as read-write binds nothing, and the failure names neither. The modules
/// say which by the type they use — <c>WTexture2D</c> writes, <c>RWTexture2D</c> does both.
/// </remarks>
public enum ShaderStorageAccess
{
    /// <summary>Not a storage binding.</summary>
    None,

    /// <summary>Written and not read.</summary>
    WriteOnly,

    /// <summary>Read and written.</summary>
    ReadWrite,

    /// <summary>Read and not written: a storage buffer declared without RW.</summary>
    ReadOnly,
}

/// <summary>One field of a uniform block, for a host that sets its parameters by name.</summary>
/// <param name="Name">The name the Slang source declares.</param>
/// <param name="Offset">Where the field starts in the block, in bytes.</param>
/// <param name="Size">How many bytes the field occupies.</param>
public sealed record ShaderUniformField(string Name, int Offset, int Size);

/// <summary>A uniform block whose fields a host sets one at a time, by name.</summary>
/// <param name="Name">The block's name.</param>
/// <param name="Group">The descriptor set it is bound in.</param>
/// <param name="Binding">The binding index within that set.</param>
/// <param name="Size">The block's size in bytes.</param>
/// <param name="Fields">Its fields, in declaration order.</param>
/// <remarks>
/// Reported only for a block the module names <c>Globals</c>, which is the convention a module follows
/// when it wants to be driven the way the engine's own compute seam drives one: a call per parameter,
/// by name, rather than a struct the host fills. Every other constant buffer is an ordinary binding and
/// carries no field table, because nothing would read it and it would only be a second thing to keep in
/// step.
/// </remarks>
public sealed record ShaderUniformBlock(
    string Name,
    int Group,
    int Binding,
    int Size,
    IReadOnlyList<ShaderUniformField> Fields);

/// <summary>
/// The block of constants pushed per draw: a Vulkan push constant, and on WebGPU a uniform buffer
/// bound at <see cref="Group"/>.
/// </summary>
/// <param name="Name">The name the Slang source declares.</param>
/// <param name="Size">The block's size in bytes.</param>
/// <param name="Group">The bind group the WGSL target places it in.</param>
/// <param name="Binding">The binding index within that group on the WGSL target.</param>
/// <remarks>
/// The two targets do not lay the block out identically: WGSL receives it as a uniform buffer and so
/// uses std140, while the SPIR-V push block is std430. The two agree for a block of vectors and
/// matrices and disagree the moment one holds a bare three-component field or an array, so a block
/// that grows one must be checked rather than assumed.
/// </remarks>
public sealed record ShaderPushBlock(string Name, int Size, int Group, int Binding);

/// <summary>A constant fixed when a pipeline is created rather than when the module is compiled.</summary>
/// <param name="Name">The name the Slang source declares.</param>
/// <param name="Id">The identifier both targets use: a SPIR-V <c>SpecId</c>, a WGSL <c>@id</c>.</param>
public sealed record ShaderSpecializationConstant(string Name, int Id);

/// <summary>One entry point of a module.</summary>
/// <param name="Name">The function name, which is what a pipeline names to select it.</param>
/// <param name="Stage">The stage it is for.</param>
/// <param name="WorkgroupSize">The compute workgroup size, or <see langword="null"/> for other stages.</param>
public sealed record ShaderEntryPoint(string Name, ShaderStage Stage, IReadOnlyList<int>? WorkgroupSize);

/// <summary>Everything a backend needs to build layouts and pipelines for one compiled variant.</summary>
/// <param name="Module">The module this describes.</param>
/// <param name="Variant">The variant this describes.</param>
/// <param name="EntryPoints">The module's entry points.</param>
/// <param name="Bindings">The resources it binds, ordered by group then binding.</param>
/// <param name="PushBlock">The pushed constant block, or <see langword="null"/> if it has none.</param>
/// <param name="SpecializationConstants">The constants a pipeline may fix at creation.</param>
/// <param name="Globals">
/// The field table of a block the module named <c>Globals</c>, for a host that sets its parameters one
/// at a time by name, or <see langword="null"/> where the module declares no such block.
/// </param>
/// <remarks>
/// Written by <c>OpenIGI.Tools.ShaderBuild</c> from Slang's own reflection and committed beside the
/// compiled output, so nothing at runtime parses SPIR-V or WGSL to find out how to bind a shader.
/// </remarks>
public sealed record ShaderReflection(
    ShaderModuleId Module,
    string Variant,
    IReadOnlyList<ShaderEntryPoint> EntryPoints,
    IReadOnlyList<ShaderBinding> Bindings,
    ShaderPushBlock? PushBlock,
    IReadOnlyList<ShaderSpecializationConstant> SpecializationConstants,
    ShaderUniformBlock? Globals = null)
{
    /// <summary>Finds the entry point for a stage.</summary>
    /// <param name="stage">The stage wanted.</param>
    /// <returns>That stage's entry point.</returns>
    /// <exception cref="InvalidOperationException">The module has no entry point for the stage.</exception>
    public ShaderEntryPoint EntryPointFor(ShaderStage stage)
    {
        foreach (ShaderEntryPoint entryPoint in EntryPoints)
        {
            if (entryPoint.Stage == stage)
            {
                return entryPoint;
            }
        }

        throw new InvalidOperationException($"Shader module {Module} has no {stage} entry point.");
    }
}

/// <summary>Serialisation for the generated reflection files, source generated so trimming keeps it.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ShaderReflection))]
[JsonSerializable(typeof(ShaderManifest))]
public sealed partial class ShaderJsonContext : JsonSerializerContext;

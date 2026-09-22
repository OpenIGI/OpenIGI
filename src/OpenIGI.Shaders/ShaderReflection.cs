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
public sealed record ShaderBinding(
    string Name,
    int Group,
    int Binding,
    ShaderBindingKind Kind);

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
    IReadOnlyList<ShaderSpecializationConstant> SpecializationConstants)
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

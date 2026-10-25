using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OpenIGI.Shaders;

/// <summary>
/// The compiled shader library: SPIR-V for Vulkan, WGSL for WebGPU and the reflection that
/// describes both, all produced at build time and read from the assembly's resources.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here compiles anything. Every artefact was produced by
/// <c>dotnet run --project tools/OpenIGI.Tools.ShaderBuild</c> and committed, which is what lets the
/// browser target exist at all — there is no shader compiler to ship to it — and what took the
/// load-time GLSL compiles off the render thread on the desktop.
/// </para>
/// <para>
/// Lookups are cached because a pipeline rebuild asks for the same module repeatedly, and a
/// manifest read or a JSON parse per pipeline would put the cost back where it was taken from.
/// </para>
/// </remarks>
public static class ShaderCatalog
{
    private static readonly Assembly Assembly = typeof(ShaderCatalog).Assembly;

    private static readonly Lazy<ShaderManifest> ManifestSource = new(LoadManifest);

    private static readonly ConcurrentDictionary<string, byte[]> SpirvCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> WgslCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ShaderReflection> ReflectionCache = new(StringComparer.Ordinal);

    // The modules from outside the library, by the ids they were given: past the library's own, so the two never meet.
    private static readonly ConcurrentDictionary<ShaderModuleId, ExternalShaderModule> External = new();
    private static int _externalCount;

    /// <summary>The first id an external module is given; the library's own are all below it.</summary>
    public const int FirstExternalId = 1 << 20;

    /// <summary>Gets the manifest listing every module the library carries.</summary>
    public static ShaderManifest Manifest => ManifestSource.Value;

    /// <summary>Gets whether a variant of a module has a form for a target.</summary>
    /// <param name="id">The module.</param>
    /// <param name="variant">The variant.</param>
    /// <param name="target">The code a backend consumes.</param>
    /// <returns>
    /// <see langword="true"/> when the library carries it. A pass whose modules are not all carried for a
    /// backend asks this and stands down, rather than being refused halfway through building itself.
    /// </returns>
    public static bool Compiles(ShaderModuleId id, ShaderVariant variant, ShaderTarget target) =>
        External.TryGetValue(id, out ExternalShaderModule? external)
            ? IsDefault(variant) && external.Compiles(target)
            : Manifest.Module(id).Variant(variant)?.Compiles(target) ?? false;

    /// <summary>Adds a module from outside the library, which is then looked up by the id this returns like any of its own.</summary>
    /// <param name="module">The module.</param>
    /// <returns>Its id, at or past <see cref="FirstExternalId"/>.</returns>
    public static ShaderModuleId Register(ExternalShaderModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ShaderModuleId id = (ShaderModuleId)(FirstExternalId + Interlocked.Increment(ref _externalCount) - 1);
        External[id] = module;
        return id;
    }

    /// <summary>Gets a module from outside the library by the id it was given.</summary>
    /// <param name="id">The id.</param>
    /// <param name="module">Receives the module.</param>
    /// <returns>Whether the id is an external module's.</returns>
    public static bool TryGetExternal(ShaderModuleId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ExternalShaderModule? module) =>
        External.TryGetValue(id, out module);

    /// <summary>Gets one variant's SPIR-V, holding every entry point the module declares.</summary>
    /// <param name="id">The module wanted.</param>
    /// <param name="variant">The variant wanted.</param>
    /// <returns>The SPIR-V words, as bytes.</returns>
    /// <exception cref="InvalidOperationException">The variant was never compiled.</exception>
    public static ReadOnlyMemory<byte> GetSpirv(ShaderModuleId id, ShaderVariant variant) =>
        External.TryGetValue(id, out ExternalShaderModule? external)
            ? external.Spirv ?? throw new InvalidOperationException($"The shader module {external.Name} was not compiled for Vulkan.")
            : SpirvCache.GetOrAdd(ResourceName(id, variant, "spv"), static name => ReadBytes(name));

    /// <summary>Gets the default variant's SPIR-V.</summary>
    /// <param name="id">The module wanted.</param>
    /// <returns>The SPIR-V words, as bytes.</returns>
    public static ReadOnlyMemory<byte> GetSpirv(ShaderModuleId id) => GetSpirv(id, ShaderVariant.Default);

    /// <summary>Gets one variant's WGSL, holding every entry point the module declares.</summary>
    /// <param name="id">The module wanted.</param>
    /// <param name="variant">The variant wanted.</param>
    /// <returns>The WGSL source.</returns>
    /// <exception cref="InvalidOperationException">The variant was never compiled.</exception>
    public static string GetWgsl(ShaderModuleId id, ShaderVariant variant) =>
        External.TryGetValue(id, out ExternalShaderModule? external)
            ? external.Wgsl ?? throw new InvalidOperationException($"The shader module {external.Name} was not compiled for WebGPU.")
            : WgslCache.GetOrAdd(ResourceName(id, variant, "wgsl"), static name => Encoding.UTF8.GetString(ReadBytes(name)));

    /// <summary>Gets the default variant's WGSL.</summary>
    /// <param name="id">The module wanted.</param>
    /// <returns>The WGSL source.</returns>
    public static string GetWgsl(ShaderModuleId id) => GetWgsl(id, ShaderVariant.Default);

    /// <summary>Gets one variant's reflection: its entry points, bindings, push block and constants.</summary>
    /// <param name="id">The module wanted.</param>
    /// <param name="variant">The variant wanted.</param>
    /// <returns>The reflection.</returns>
    /// <exception cref="InvalidOperationException">The variant was never compiled, or its reflection is unreadable.</exception>
    public static ShaderReflection GetReflection(ShaderModuleId id, ShaderVariant variant) =>
        External.TryGetValue(id, out ExternalShaderModule? external)
            ? external.Reflection
            : ReflectionCache.GetOrAdd(ResourceName(id, variant, "reflection.json"), static name =>
            JsonSerializer.Deserialize(ReadBytes(name), ShaderJsonContext.Default.ShaderReflection)
                ?? throw new InvalidOperationException($"The shader reflection resource {name} is empty."));

    /// <summary>Gets the default variant's reflection.</summary>
    /// <param name="id">The module wanted.</param>
    /// <returns>The reflection.</returns>
    public static ShaderReflection GetReflection(ShaderModuleId id) => GetReflection(id, ShaderVariant.Default);

    /// <summary>The name a stage's compiled form is filed under, where a target compiles each stage apart.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns><c>vertex</c>, <c>fragment</c> or <c>compute</c>.</returns>
    /// <remarks>DXIL is compiled one entry point to a blob: <c>Generated/{Id}.{variant}.{stage}.dxil</c>.</remarks>
    public static string StageName(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vertex",
        ShaderStage.Fragment => "fragment",
        ShaderStage.Compute => "compute",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>Builds the resource name one generated artefact is embedded under.</summary>
    /// <param name="id">The module.</param>
    /// <param name="variant">The variant.</param>
    /// <param name="extension">The artefact's extension, without a leading dot.</param>
    /// <returns>The embedded resource name.</returns>
    public static string ResourceName(ShaderModuleId id, ShaderVariant variant, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        string name = string.IsNullOrEmpty(variant.Name) ? ShaderVariant.DefaultName : variant.Name;
        return $"OpenIGI.Shaders.Generated.{id}.{name}.{extension}";
    }

    // An external module carries its default variant alone.
    private static bool IsDefault(ShaderVariant variant) =>
        string.IsNullOrEmpty(variant.Name) || variant.Name == ShaderVariant.DefaultName;

    private static ShaderManifest LoadManifest() =>
        JsonSerializer.Deserialize(ReadBytes(ShaderManifest.ResourceName), ShaderJsonContext.Default.ShaderManifest)
            ?? throw new InvalidOperationException($"{ShaderManifest.FileName} is embedded but empty.");

    private static byte[] ReadBytes(string resourceName)
    {
        using Stream? stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The shader resource {resourceName} is not in the assembly. "
                + "Run `dotnet run --project tools/OpenIGI.Tools.ShaderBuild` to regenerate the shader library.");
        }

        using MemoryStream buffer = new((int)stream.Length);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

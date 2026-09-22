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

    /// <summary>Gets the manifest listing every module the library carries.</summary>
    public static ShaderManifest Manifest => ManifestSource.Value;

    /// <summary>Gets one variant's SPIR-V, holding every entry point the module declares.</summary>
    /// <param name="id">The module wanted.</param>
    /// <param name="variant">The variant wanted.</param>
    /// <returns>The SPIR-V words, as bytes.</returns>
    /// <exception cref="InvalidOperationException">The variant was never compiled.</exception>
    public static ReadOnlyMemory<byte> GetSpirv(ShaderModuleId id, ShaderVariant variant) =>
        SpirvCache.GetOrAdd(ResourceName(id, variant, "spv"), static name => ReadBytes(name));

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
        WgslCache.GetOrAdd(ResourceName(id, variant, "wgsl"), static name => Encoding.UTF8.GetString(ReadBytes(name)));

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
        ReflectionCache.GetOrAdd(ResourceName(id, variant, "reflection.json"), static name =>
            JsonSerializer.Deserialize(ReadBytes(name), ShaderJsonContext.Default.ShaderReflection)
                ?? throw new InvalidOperationException($"The shader reflection resource {name} is empty."));

    /// <summary>Gets the default variant's reflection.</summary>
    /// <param name="id">The module wanted.</param>
    /// <returns>The reflection.</returns>
    public static ShaderReflection GetReflection(ShaderModuleId id) => GetReflection(id, ShaderVariant.Default);

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

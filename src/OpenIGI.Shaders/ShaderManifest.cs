using System.Text.Json.Serialization;

namespace OpenIGI.Shaders;

/// <summary>One variant of a module, as the manifest declares it.</summary>
/// <param name="Name">The variant's name; part of its generated file names.</param>
/// <param name="Defines">Preprocessor defines handed to <c>slangc</c> for this variant.</param>
public sealed record ShaderVariantEntry(string Name, IReadOnlyDictionary<string, string> Defines)
{
    /// <summary>Gets the defines, treating an absent map as an empty one.</summary>
    public IReadOnlyDictionary<string, string> Defines { get; init; } =
        Defines ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>One module, as the manifest declares it.</summary>
/// <param name="Id">The module, which must have a matching <see cref="ShaderModuleId"/> value.</param>
/// <param name="Source">The Slang source's path relative to <c>Source/</c>.</param>
/// <param name="Variants">The variants to compile; empty means the default variant alone.</param>
public sealed record ShaderModuleEntry(
    ShaderModuleId Id,
    string Source,
    IReadOnlyList<ShaderVariantEntry> Variants)
{
    /// <summary>Gets the variants to compile, substituting the default when none are declared.</summary>
    public IReadOnlyList<ShaderVariantEntry> Variants { get; init; } =
        Variants is { Count: > 0 }
            ? Variants
            : [new ShaderVariantEntry(ShaderVariant.DefaultName, new Dictionary<string, string>(StringComparer.Ordinal))];
}

/// <summary>
/// The list of shader modules and the Slang release that compiled them: the single source of truth
/// the build tool reads and the catalogue resolves names through.
/// </summary>
/// <param name="SlangVersion">The <c>slangc -v</c> string the committed output was produced with.</param>
/// <param name="Modules">The modules the library carries.</param>
/// <remarks>
/// One list, read by both sides, so a module cannot be compiled but unreachable or named but never
/// compiled. It is embedded in the assembly rather than read from disk because the catalogue has to
/// work from a published single file and, later, from a browser.
/// </remarks>
public sealed record ShaderManifest(string SlangVersion, IReadOnlyList<ShaderModuleEntry> Modules)
{
    /// <summary>The manifest's file name, at the project root and embedded into the assembly.</summary>
    public const string FileName = "ShaderModules.json";

    /// <summary>The resource name the manifest is embedded under.</summary>
    [JsonIgnore]
    public static string ResourceName => $"OpenIGI.Shaders.{FileName}";

    /// <summary>Finds a module's entry.</summary>
    /// <param name="id">The module wanted.</param>
    /// <returns>That module's entry.</returns>
    /// <exception cref="InvalidOperationException">The manifest does not list the module.</exception>
    public ShaderModuleEntry Module(ShaderModuleId id)
    {
        foreach (ShaderModuleEntry module in Modules)
        {
            if (module.Id == id)
            {
                return module;
            }
        }

        throw new InvalidOperationException($"{FileName} does not list the shader module {id}.");
    }
}

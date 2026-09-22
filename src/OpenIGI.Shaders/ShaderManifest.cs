using System.Text.Json.Serialization;

namespace OpenIGI.Shaders;

/// <summary>Which compiled form a variant is produced in.</summary>
public enum ShaderTarget
{
    /// <summary>SPIR-V, for Vulkan.</summary>
    Spirv,

    /// <summary>WGSL, for WebGPU.</summary>
    Wgsl,
}

/// <summary>One variant of a module, as the manifest declares it.</summary>
/// <param name="Name">The variant's name; part of its generated file names.</param>
/// <param name="Defines">Preprocessor defines handed to <c>slangc</c> for this variant.</param>
/// <param name="Targets">
/// The forms to compile, or empty for both. A variant that traces rays lists SPIR-V alone: WGSL has
/// no ray tracing, so there is nothing for that target to be compiled to, and the backend that reads
/// it stands the feature down the way MoltenVK does.
/// </param>
public sealed record ShaderVariantEntry(
    string Name,
    IReadOnlyDictionary<string, string> Defines,
    IReadOnlyList<ShaderTarget> Targets)
{
    /// <summary>Gets the defines, treating an absent map as an empty one.</summary>
    public IReadOnlyDictionary<string, string> Defines { get; init; } =
        Defines ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the targets to compile, substituting both when none are declared.</summary>
    public IReadOnlyList<ShaderTarget> Targets { get; init; } =
        Targets is { Count: > 0 } ? Targets : [ShaderTarget.Spirv, ShaderTarget.Wgsl];

    /// <summary>Whether this variant is compiled for a target.</summary>
    /// <param name="target">The target asked about.</param>
    /// <returns><see langword="true"/> when the variant is compiled for it.</returns>
    public bool Compiles(ShaderTarget target) => Targets.Contains(target);
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
            : [new ShaderVariantEntry(ShaderVariant.DefaultName, new Dictionary<string, string>(StringComparer.Ordinal), [])];
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

    /// <summary>The release a <c>slangc -v</c> string names, without the build that produced it.</summary>
    /// <param name="version">A version string as <c>slangc -v</c> reports it.</param>
    /// <returns>The release, which is everything before the first hyphen.</returns>
    /// <remarks>
    /// A release is distributed more than once: the Vulkan SDK ships its own build of it, which reports
    /// the tag plus the commits past it (<c>2026.13.1-1-g84792eb15</c>), while the tagged download
    /// reports the tag alone. Measured against the whole library, the two produce byte-identical output,
    /// so the build is information and the release is the thing that has to match.
    /// </remarks>
    public static string ReleaseOf(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        int hyphen = version.IndexOf('-', StringComparison.Ordinal);
        return hyphen < 0 ? version.Trim() : version[..hyphen].Trim();
    }

    /// <summary>Whether a compiler produces the output this library committed.</summary>
    /// <param name="version">The compiler's version, as <c>slangc -v</c> reports it.</param>
    /// <returns><see langword="true"/> when it is a build of the pinned release.</returns>
    public bool Accepts(string version) =>
        string.Equals(ReleaseOf(version), ReleaseOf(SlangVersion), StringComparison.Ordinal);

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

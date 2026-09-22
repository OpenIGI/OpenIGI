using System.Text.RegularExpressions;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>
/// The storage formats a compiled WGSL module actually declares.
/// </summary>
/// <remarks>
/// <para>
/// Read out of the emitted text rather than predicted. WGSL allows a storage binding on a short list of
/// formats and the compiler widens anything narrower — <c>r8</c>, <c>rg8</c> and <c>rg16f</c> all come
/// out as <c>rgba32float</c> today — and a WebGPU bind group layout is validated against the format the
/// module declares, so the host has to allocate exactly that.
/// </para>
/// <para>
/// Recording it here rather than reproducing the rule in a backend is the whole point: the rule belongs
/// to whichever compiler produced the output, it is not written down anywhere the host can read, and it
/// is free to change between releases. What is committed beside the module is what the module says.
/// </para>
/// </remarks>
public static partial class WgslStorageFormats
{
    /// <summary>Reads every storage binding a WGSL module declares.</summary>
    /// <param name="wgsl">The emitted WGSL.</param>
    /// <param name="module">The module, for the diagnostic.</param>
    /// <returns>The format of each, by group and binding.</returns>
    /// <exception cref="ShaderBuildException">A declared format is one the library has no name for.</exception>
    public static Dictionary<(int Group, int Binding), ShaderStorageFormat> Read(string wgsl, ShaderModuleId module)
    {
        ArgumentNullException.ThrowIfNull(wgsl);

        Dictionary<(int, int), ShaderStorageFormat> formats = [];

        foreach (Match match in StorageTexturePattern().Matches(wgsl))
        {
            int binding = int.Parse(match.Groups["binding"].ValueSpan, provider: null);
            int group = int.Parse(match.Groups["group"].ValueSpan, provider: null);
            string name = match.Groups["format"].Value;

            formats[(group, binding)] = ReflectionReader.ParseStorageFormat(name)
                ?? throw new ShaderBuildException(
                    $"{module}: its WGSL declares the storage format '{name}' at group {group} binding "
                    + $"{binding}, which the library has no name for. Add it to ShaderStorageFormat.");
        }

        return formats;
    }

    /// <summary>Puts what the WGSL declared onto the reflection the SPIR-V produced.</summary>
    /// <param name="reflection">The reflection so far.</param>
    /// <param name="formats">What the WGSL declared, by group and binding.</param>
    /// <param name="module">The module, for the diagnostic.</param>
    /// <returns>The reflection, with every storage binding carrying both formats.</returns>
    /// <exception cref="ShaderBuildException">The two targets disagree about which bindings are storage textures.</exception>
    public static ShaderReflection Merge(
        ShaderReflection reflection,
        Dictionary<(int Group, int Binding), ShaderStorageFormat> formats,
        ShaderModuleId module)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(formats);

        List<ShaderBinding> merged = [];

        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Kind != ShaderBindingKind.StorageTexture)
            {
                merged.Add(binding);
                continue;
            }

            if (!formats.TryGetValue((binding.Group, binding.Binding), out ShaderStorageFormat format))
            {
                throw new ShaderBuildException(
                    $"{module}: '{binding.Name}' is a storage texture at group {binding.Group} binding "
                    + $"{binding.Binding} in the SPIR-V and is not one in the WGSL. The two targets have to "
                    + "agree about what is bound where.");
            }

            merged.Add(binding with { WgslStorageFormat = format });
        }

        // A storage texture in the WGSL that the SPIR-V does not have is the same disagreement seen from
        // the other side, and just as much a reason to stop.
        foreach (((int group, int binding), ShaderStorageFormat format) in formats)
        {
            if (!merged.Exists(b => b.Group == group && b.Binding == binding))
            {
                throw new ShaderBuildException(
                    $"{module}: its WGSL declares a {format} storage texture at group {group} binding "
                    + $"{binding} that the SPIR-V does not bind.");
            }
        }

        return reflection with { Bindings = merged };
    }

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*texture_storage_\dd<(?<format>[a-z0-9_]+)")]
    private static partial Regex StorageTexturePattern();
}

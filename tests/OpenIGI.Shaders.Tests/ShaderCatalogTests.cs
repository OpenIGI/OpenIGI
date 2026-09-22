using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The invariants the shader library rests on: that the manifest, the enumeration and the directory
/// describe the same set of modules, and that the two compiled targets agree about how a module is
/// bound.
/// </summary>
/// <remarks>
/// The cross-target agreement is the one worth stating out loud. It holds only because every module
/// declares its textures and samplers separately: Slang's combined <c>Sampler2D</c> expands into two
/// binding slots on the WGSL target and one on SPIR-V, so a module written that way binds
/// differently on each backend while compiling cleanly on both. The tests below would catch that.
/// </remarks>
public sealed partial class ShaderCatalogTests
{
    private static string SourceDirectory => Path.Combine(ShaderRepository.ProjectDirectory, "Source");

    [GeneratedRegex(@"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var(?:<[^>]*>)?\s*(?<name>\w+)")]
    private static partial Regex WgslBindingRegex { get; }

    /// <summary>Every module in the enumeration is listed in the manifest, and the reverse.</summary>
    [Fact]
    public void Manifest_and_enumeration_describe_the_same_modules()
    {
        IEnumerable<ShaderModuleId> declared = Enum.GetValues<ShaderModuleId>();
        IEnumerable<ShaderModuleId> listed = ShaderCatalog.Manifest.Modules.Select(module => module.Id);

        Assert.Equal(declared.Order().ToArray(), listed.Order().ToArray());
    }

    /// <summary>Every module the manifest lists has the Slang source it names.</summary>
    [Fact]
    public void Every_manifest_module_has_its_source()
    {
        List<string> missing = [];

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            string path = Path.Combine(SourceDirectory, module.Source.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing.Add($"{module.Id} -> {module.Source}");
            }
        }

        Assert.True(missing.Count == 0, "Manifest entries with no source:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Every Slang source outside <c>Common/</c> is a module the manifest lists, so a shader cannot
    /// be added to the directory and then silently never compiled.
    /// </summary>
    [Fact]
    public void Every_source_is_listed_in_the_manifest()
    {
        HashSet<string> listed = ShaderCatalog.Manifest.Modules
            .Select(module => module.Source.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> unlisted = [];
        string common = Path.Combine(SourceDirectory, "Common");

        foreach (string path in Directory.EnumerateFiles(SourceDirectory, "*.slang", SearchOption.AllDirectories))
        {
            if (path.StartsWith(common, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(SourceDirectory, path);
            if (!listed.Contains(relative))
            {
                unlisted.Add(relative);
            }
        }

        Assert.True(
            unlisted.Count == 0,
            $"Slang sources not listed in {ShaderManifest.FileName}:\n  " + string.Join("\n  ", unlisted));
    }

    /// <summary>Every declared variant has all three of its compiled artefacts embedded.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_variant_has_its_compiled_artifacts(ShaderModuleId module, string variant)
    {
        ShaderVariant selected = new(variant);

        Assert.False(ShaderCatalog.GetSpirv(module, selected).IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(ShaderCatalog.GetWgsl(module, selected)));
        Assert.NotNull(ShaderCatalog.GetReflection(module, selected));
    }

    /// <summary>The SPIR-V is SPIR-V: the magic number and a whole number of words.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Spirv_is_well_formed(ShaderModuleId module, string variant)
    {
        ReadOnlySpan<byte> spirv = ShaderCatalog.GetSpirv(module, new ShaderVariant(variant)).Span;

        Assert.Equal(0, spirv.Length % 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv[..4]));
    }

    /// <summary>The reflection names entry points the WGSL actually declares.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Entry_points_exist_in_both_targets(ShaderModuleId module, string variant)
    {
        ShaderVariant selected = new(variant);
        ShaderReflection reflection = ShaderCatalog.GetReflection(module, selected);
        string wgsl = ShaderCatalog.GetWgsl(module, selected);

        Assert.NotEmpty(reflection.EntryPoints);

        foreach (ShaderEntryPoint entryPoint in reflection.EntryPoints)
        {
            Assert.Contains($"fn {entryPoint.Name}(", wgsl, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The binding numbers the reflection records -- taken from the SPIR-V compile -- are the numbers
    /// the WGSL uses. This is the invariant the whole one-source-two-targets design rests on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Bindings_agree_across_both_targets(ShaderModuleId module, string variant)
    {
        ShaderVariant selected = new(variant);
        ShaderReflection reflection = ShaderCatalog.GetReflection(module, selected);
        string wgsl = ShaderCatalog.GetWgsl(module, selected);

        // Slang suffixes WGSL identifiers, and expands a texture-plus-sampler pair into names derived
        // from each declaration, so a binding is matched by the declared name being a prefix.
        Dictionary<(int Group, int Binding), string> wgslSlots = [];
        foreach (Match match in WgslBindingRegex.Matches(wgsl))
        {
            int group = int.Parse(match.Groups["group"].ValueSpan, CultureInfo.InvariantCulture);
            int binding = int.Parse(match.Groups["binding"].ValueSpan, CultureInfo.InvariantCulture);
            wgslSlots[(group, binding)] = match.Groups["name"].Value;
        }

        Dictionary<(int Group, int Binding), string> reflected = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            reflected[(binding.Group, binding.Binding)] = binding.Name;
        }

        if (reflection.PushBlock is { } pushBlock)
        {
            reflected[(pushBlock.Group, pushBlock.Binding)] = pushBlock.Name;
        }

        List<string> wrong = [];

        // The WGSL side is the one iterated, because it may legitimately hold FEWER bindings: Slang
        // drops a binding the shader never reads on that target and keeps it on SPIR-V, where the
        // reflection lists every module-scope parameter whether or not an entry point touches it.
        // Glazing relies on that -- both its modules declare the whole shared mesh set so every
        // pipeline binding it agrees about the shape, and each samples only part of it. What must
        // never happen is the two targets DISAGREEING about a slot they both have.
        foreach (((int Group, int Binding) slot, string wgslName) in wgslSlots)
        {
            if (!reflected.TryGetValue(slot, out string? name))
            {
                wrong.Add($"the WGSL binds '{wgslName}' at group {slot.Group} binding {slot.Binding}, which the reflection does not list");
                continue;
            }

            if (!wgslName.StartsWith(name, StringComparison.Ordinal))
            {
                wrong.Add($"group {slot.Group} binding {slot.Binding} is '{name}' in SPIR-V but '{wgslName}' in WGSL");
            }
        }

        // The push block is not optional: every module here has one and WGSL must bind it, because a
        // push constant becomes an ordinary uniform buffer on that target.
        if (reflection.PushBlock is { } push && !wgslSlots.ContainsKey((push.Group, push.Binding)))
        {
            wrong.Add($"the push block '{push.Name}' is not bound at group {push.Group} binding {push.Binding} in the WGSL");
        }

        Assert.True(wrong.Count == 0, $"{module}/{variant} binds differently on the two targets:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>A compute entry point reports its workgroup size; a raster one does not.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void Workgroup_sizes_are_recorded_for_compute_alone(ShaderModuleId module, string variant)
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(module, new ShaderVariant(variant));

        foreach (ShaderEntryPoint entryPoint in reflection.EntryPoints)
        {
            if (entryPoint.Stage == ShaderStage.Compute)
            {
                Assert.NotNull(entryPoint.WorkgroupSize);
                Assert.Equal(3, entryPoint.WorkgroupSize!.Count);
            }
            else
            {
                Assert.Null(entryPoint.WorkgroupSize);
            }
        }
    }

    /// <summary>Every module and variant the manifest declares.</summary>
    /// <returns>The module and variant name of each compiled artefact set.</returns>
    public static TheoryData<ShaderModuleId, string> Variants()
    {
        TheoryData<ShaderModuleId, string> data = [];

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                data.Add(module.Id, variant.Name);
            }
        }

        return data;
    }
}

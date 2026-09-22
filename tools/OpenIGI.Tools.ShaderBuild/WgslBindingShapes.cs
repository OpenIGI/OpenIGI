using System.Text.RegularExpressions;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>
/// What a compiled WGSL module says its textures and samplers are, beyond what Slang's reflection says.
/// </summary>
/// <remarks>
/// <para>
/// A WebGPU bind group layout states, for every texture, how many dimensions it has, whether it is an
/// array of slices and what a shader reads out of it; and for every sampler, whether it filters or
/// compares. It is validated against all of it, and a layout that gets one wrong binds nothing. A
/// Vulkan descriptor set distinguishes none of it — an arrayed depth texture and a flat colour one are
/// both a sampled image, and a comparison sampler and an ordinary one are both a sampler — so these are
/// exactly the distinctions the Vulkan backend could never have caught.
/// </para>
/// <para>
/// Slang's reflection reports whether a texture is arrayed and says nothing whatever about the other
/// two: <c>SamplerComparisonState</c> and <c>SamplerState</c> come out identically, and so does the
/// shadow map that becomes a depth texture. The emitted WGSL says both, because WGSL has a type for
/// each, so they are read back off the text the way the storage formats are — the compiler's own answer
/// rather than a rule reproduced here.
/// </para>
/// </remarks>
public static partial class WgslBindingShapes
{
    /// <summary>What one binding the WGSL declares turned out to be.</summary>
    /// <param name="Comparison">Whether a sampler compares rather than filters.</param>
    /// <param name="Arrayed">Whether a texture is an array of slices.</param>
    /// <param name="SampleType">What a shader reads out of the texture.</param>
    public readonly record struct Shape(bool Comparison, bool Arrayed, ShaderSampleType SampleType);

    /// <summary>Reads every texture and sampler a WGSL module declares.</summary>
    /// <param name="wgsl">The emitted WGSL.</param>
    /// <returns>What each is, by group and binding.</returns>
    public static Dictionary<(int Group, int Binding), Shape> Read(string wgsl)
    {
        ArgumentNullException.ThrowIfNull(wgsl);

        Dictionary<(int, int), Shape> shapes = [];

        foreach (Match match in SamplerPattern().Matches(wgsl))
        {
            shapes[Slot(match)] = new Shape(
                match.Groups["comparison"].Success,
                Arrayed: false,
                ShaderSampleType.Float);
        }

        foreach (Match match in TexturePattern().Matches(wgsl))
        {
            shapes[Slot(match)] = new Shape(
                Comparison: false,
                match.Groups["array"].Success,
                SampleTypeOf(match));
        }

        return shapes;
    }

    /// <summary>Puts what the WGSL declared onto the reflection the SPIR-V produced.</summary>
    /// <param name="reflection">The reflection so far.</param>
    /// <param name="shapes">What the WGSL declared, by group and binding.</param>
    /// <param name="module">The module, for the diagnostic.</param>
    /// <returns>The reflection, with every sampler and texture carrying what the WGSL says it is.</returns>
    /// <exception cref="ShaderBuildException">The two targets disagree about what a texture is.</exception>
    public static ShaderReflection Merge(
        ShaderReflection reflection,
        Dictionary<(int Group, int Binding), Shape> shapes,
        ShaderModuleId module)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(shapes);

        List<ShaderBinding> merged = [];

        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Kind is not (ShaderBindingKind.Texture or ShaderBindingKind.Sampler)
                || !shapes.TryGetValue((binding.Group, binding.Binding), out Shape shape))
            {
                merged.Add(binding);
                continue;
            }

            // Whether a texture is arrayed comes from Slang and is available on every target, so the
            // WGSL is a check on it rather than the source. The two disagreeing means one of them is
            // being read wrong, and a layout built from either would be a guess.
            if (binding.Kind is ShaderBindingKind.Texture && shape.Arrayed != binding.Arrayed)
            {
                throw new ShaderBuildException(
                    $"{module}: '{binding.Name}' is {(binding.Arrayed ? string.Empty : "not ")}an arrayed "
                    + $"texture in the reflection and {(shape.Arrayed ? string.Empty : "not ")}one in the "
                    + "WGSL. The two targets have to agree about what is bound where.");
            }

            merged.Add(binding with { Comparison = shape.Comparison, SampleType = shape.SampleType });
        }

        return reflection with { Bindings = merged };
    }

    /// <summary>What the WGSL says a texture holds, out of the type it spelt.</summary>
    /// <param name="match">The texture declaration.</param>
    /// <returns>The sample type.</returns>
    /// <remarks>
    /// A depth texture is a type of its own in WGSL and carries no element type at all; everything else
    /// states one. Unrecognised falls to float, which is what every texture in this library that is not
    /// a depth one or a counter holds.
    /// </remarks>
    private static ShaderSampleType SampleTypeOf(Match match)
    {
        if (match.Groups["depth"].Success)
        {
            return ShaderSampleType.Depth;
        }

        return match.Groups["element"].Value switch
        {
            "u32" => ShaderSampleType.Uint,
            "i32" => ShaderSampleType.Sint,
            _ => ShaderSampleType.Float,
        };
    }

    private static (int Group, int Binding) Slot(Match match) => (
        int.Parse(match.Groups["group"].ValueSpan, provider: null),
        int.Parse(match.Groups["binding"].ValueSpan, provider: null));

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*sampler(?<comparison>_comparison)?\s*;")]
    private static partial Regex SamplerPattern();

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*"
        + @"texture_(?<depth>depth_)?\dd(?<array>_array)?\s*(?:<\s*(?<element>\w+)|;)")]
    private static partial Regex TexturePattern();
}

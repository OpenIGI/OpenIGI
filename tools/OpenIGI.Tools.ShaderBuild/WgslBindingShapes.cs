using System.Text.RegularExpressions;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>
/// What a compiled WGSL module says its textures and samplers are, beyond what Slang's reflection says.
/// </summary>
/// <remarks>
/// <para>
/// A WebGPU bind group layout states, for every texture, how many dimensions it has and whether it is
/// an array of slices; and for every sampler, whether it filters or compares. It is validated against
/// all of it, and a layout that gets one wrong binds nothing. A Vulkan descriptor set distinguishes
/// none of it — an arrayed texture and a flat one are both a sampled image, and a comparison sampler
/// and an ordinary one are both a sampler — so these are exactly the distinctions the Vulkan backend
/// could never have caught.
/// </para>
/// <para>
/// Slang's reflection reports whether a texture is arrayed and says nothing whatever about a comparison
/// sampler: <c>SamplerComparisonState</c> and <c>SamplerState</c> come out identically. The emitted
/// WGSL does say, because WGSL has two types for it, so it is read back off the text the way the
/// storage formats are — the compiler's own answer rather than a rule reproduced here.
/// </para>
/// </remarks>
public static partial class WgslBindingShapes
{
    /// <summary>What one binding the WGSL declares turned out to be.</summary>
    /// <param name="Comparison">Whether a sampler compares rather than filters.</param>
    /// <param name="Arrayed">Whether a texture is an array of slices.</param>
    public readonly record struct Shape(bool Comparison, bool Arrayed);

    /// <summary>Reads every texture and sampler a WGSL module declares.</summary>
    /// <param name="wgsl">The emitted WGSL.</param>
    /// <returns>What each is, by group and binding.</returns>
    public static Dictionary<(int Group, int Binding), Shape> Read(string wgsl)
    {
        ArgumentNullException.ThrowIfNull(wgsl);

        Dictionary<(int, int), Shape> shapes = [];

        foreach (Match match in SamplerPattern().Matches(wgsl))
        {
            shapes[Slot(match)] = new Shape(match.Groups["comparison"].Success, Arrayed: false);
        }

        foreach (Match match in TexturePattern().Matches(wgsl))
        {
            shapes[Slot(match)] = new Shape(Comparison: false, match.Groups["array"].Success);
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

            merged.Add(binding with { Comparison = shape.Comparison });
        }

        return reflection with { Bindings = merged };
    }

    private static (int Group, int Binding) Slot(Match match) => (
        int.Parse(match.Groups["group"].ValueSpan, provider: null),
        int.Parse(match.Groups["binding"].ValueSpan, provider: null));

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*sampler(?<comparison>_comparison)?\s*;")]
    private static partial Regex SamplerPattern();

    [GeneratedRegex(
        @"@binding\((?<binding>\d+)\)\s*@group\((?<group>\d+)\)\s*var\s+\w+\s*:\s*texture_(?:depth_)?\dd(?<array>_array)?\s*[<;]")]
    private static partial Regex TexturePattern();
}

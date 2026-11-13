using System.Text.RegularExpressions;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Moves a stage's constant arrays into uniforms whose initial values are the constants.</summary>
/// <remarks>
/// <para>
/// SPIRV-Cross writes a constant array -- a sample pattern, a ring of neighbour offsets -- as a global <c>const</c>.
/// NVIDIA's OpenGL compiler keeps such an array in each invocation's local memory: every invocation writes the
/// whole array out as it starts and reads it back by index. That memory shares the cache texture reads go through,
/// so the passes that index one inside a loop of texture reads -- the lamp shadow march, the lighting, the
/// reflections -- ran at half the speed of the same SPIR-V under Vulkan, whose compiler keeps it in constant memory.
/// </para>
/// <para>
/// A uniform with an initialiser is set as the program links and lives in the program's constant buffer, which an
/// index reads directly, so nothing has to be done at run time. Each is renamed for its stage, since a vertex and a
/// fragment stage linked together share one namespace of uniforms and SPIRV-Cross numbers each stage apart.
/// </para>
/// </remarks>
internal static partial class GlslConstantArrays
{
    /// <summary>Rewrites a stage's global constant arrays as initialised uniforms.</summary>
    /// <param name="source">The stage's GLSL.</param>
    /// <param name="stage">The stage, whose name each array takes on.</param>
    /// <returns>The rewritten GLSL; the same text where it declares none.</returns>
    public static string Hoist(string source, ShaderStage stage)
    {
        ArgumentNullException.ThrowIfNull(source);

        string suffix = stage switch
        {
            ShaderStage.Vertex => "_vert",
            ShaderStage.Fragment => "_frag",
            _ => "_comp",
        };

        List<string> names = [];
        string hoisted = Declaration().Replace(source, match =>
        {
            names.Add(match.Groups["name"].Value);
            return $"uniform {match.Groups["type"].Value} {match.Groups["name"].Value}{suffix}[{match.Groups["length"].Value}] = {match.Groups["value"].Value};";
        });

        foreach (string name in names)
        {
            hoisted = Regex.Replace(
                hoisted,
                $@"\b{Regex.Escape(name)}\b(?!{Regex.Escape(suffix)})",
                name + suffix,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));
        }

        return hoisted;
    }

    // A global constant array as SPIRV-Cross writes one: on one line, at the start of it, of a plain type.
    [GeneratedRegex(
        @"^const (?<type>(?:float|int|uint|bool|[biu]?vec[234]|mat[234](?:x[234])?)) (?<name>\w+)\[(?<length>\d+)\] = (?<value>\k<type>\[\]\(.*\));$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex Declaration();
}

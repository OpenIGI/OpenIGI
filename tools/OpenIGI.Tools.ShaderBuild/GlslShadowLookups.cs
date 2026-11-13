using System.Text;
using System.Text.RegularExpressions;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Turns a comparison against a layered shadow map's top level back into a plain lookup at that level.</summary>
/// <remarks>
/// <para>
/// A cascade or lamp map is compared against at its one level (<c>SampleCmpLevelZero</c>). Core GLSL 4.30 has no
/// lookup at a given level for an array or cube of shadow maps, so SPIRV-Cross writes the next thing that means the
/// same: a lookup with both gradients nought. The answer is right, but a lookup by gradient runs at a fraction of the
/// rate of one by level on NVIDIA's hardware, and the lighting and the lamp shadow march make a dozen of them a pixel:
/// both ran at half the speed of the same SPIR-V under Vulkan.
/// </para>
/// <para>
/// <c>GL_EXT_texture_shadow_lod</c> adds the lookup by level, and every desktop driver of the last several years has
/// it. Each such lookup is written as a macro the stage defines either way, so a driver without the extension still
/// compiles the gradient form.
/// </para>
/// </remarks>
internal static partial class GlslShadowLookups
{
    private const string Macro = "spvShadowLod0";

    private const string Preamble =
        "#extension GL_EXT_texture_shadow_lod : enable\n" +
        "#ifdef GL_EXT_texture_shadow_lod\n" +
        "#define " + Macro + "(s, c) textureLod(s, c, 0.0)\n" +
        "#else\n" +
        "#define " + Macro + "(s, c) textureGrad(s, c, vec2(0.0), vec2(0.0))\n" +
        "#endif\n";

    private const string Lookup = "textureGrad(";

    /// <summary>Rewrites a stage's zero-gradient lookups of layered shadow maps as lookups at the top level.</summary>
    /// <param name="source">The stage's GLSL.</param>
    /// <returns>The rewritten GLSL; the same text where it makes none.</returns>
    public static string AtTopLevel(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        HashSet<string> shadowArrays = [.. LayeredShadowSampler().Matches(source).Select(match => match.Groups["name"].Value)];
        if (shadowArrays.Count == 0)
        {
            return source;
        }

        StringBuilder rewritten = new(source.Length);
        int copied = 0;
        bool changed = false;

        for (int at = source.IndexOf(Lookup, StringComparison.Ordinal); at >= 0; at = source.IndexOf(Lookup, at + 1, StringComparison.Ordinal))
        {
            int open = at + Lookup.Length - 1;
            int close = ClosingParenthesis(source, open);
            if (close < 0)
            {
                continue;
            }

            List<string> arguments = Arguments(source, open + 1, close);
            if (arguments.Count != 4
                || !shadowArrays.Contains(arguments[0])
                || arguments[2] != "vec2(0.0)"
                || arguments[3] != "vec2(0.0)")
            {
                continue;
            }

            rewritten.Append(source, copied, at - copied)
                .Append(Macro).Append('(').Append(arguments[0]).Append(", ").Append(arguments[1]).Append(')');
            copied = close + 1;
            changed = true;
        }

        if (!changed)
        {
            return source;
        }

        rewritten.Append(source, copied, source.Length - copied);
        string text = rewritten.ToString();

        // After the version line, which must come first, and before any code, which the extension must precede.
        int afterVersion = text.IndexOf('\n', StringComparison.Ordinal) + 1;
        return text.Insert(afterVersion, Preamble);
    }

    private static int ClosingParenthesis(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // The arguments between two parentheses, split at the commas that are not inside a nested call.
    private static List<string> Arguments(string text, int start, int end)
    {
        List<string> arguments = [];
        int depth = 0;
        int from = start;
        for (int i = start; i < end; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(text[from..i].Trim());
                    from = i + 1;
                    break;
            }
        }

        arguments.Add(text[from..end].Trim());
        return arguments;
    }

    [GeneratedRegex(
        @"uniform (?:sampler2DArrayShadow|samplerCubeShadow|samplerCubeArrayShadow) (?<name>\w+);",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex LayeredShadowSampler();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>
/// Turns a compute shader written in the GL dialect into one Vulkan's SPIR-V compiler accepts, and reads
/// out the descriptor layout the rewritten source implies.
/// </summary>
/// <remarks>
/// <para>
/// The engine's compute shaders are authored for GL 4.3: their scalar parameters are loose
/// <c>uniform</c> declarations in the default block, which Vulkan GLSL forbids — every uniform there must
/// sit in a block. This gathers those loose uniforms into a single anonymous <c>std140</c> uniform block,
/// which keeps their names unqualified in the body (so nothing else in the source changes) while giving
/// them the block Vulkan requires. Their byte offsets follow the <c>std140</c> rule the compiler will
/// itself apply, so a value written at the offset computed here lands where the shader reads it.
/// </para>
/// <para>
/// The storage buffers, images and samplers keep the explicit <c>binding</c> numbers they were authored
/// with; those are read back here so the pipeline's descriptor-set layout can be built without a separate
/// hand-kept table. GL's three binding namespaces collapse into one in Vulkan, so a collision between,
/// say, a storage buffer and an image at the same number is a real fault and is reported rather than
/// silently mis-bound.
/// </para>
/// </remarks>
internal static partial class ComputeShaderAnalysis
{
    /// <summary>One resource a compute shader binds, and the descriptor it needs.</summary>
    /// <param name="Binding">The set-0 binding number reassigned for Vulkan, unique across every kind.</param>
    /// <param name="Slot">The number the API binds it by — the original GL binding, per-kind.</param>
    /// <param name="Kind">Which kind of descriptor it is.</param>
    /// <param name="Name">The resource's name; for a default-block sampler, the name its unit is set by.</param>
    /// <param name="IsVolume">Whether a sampled image is a 3D volume rather than a 2D texture.</param>
    /// <param name="ByUnit">
    /// Whether the resource resolves by the texture unit assigned through <c>SetComputeUniform</c> — a
    /// GL default-block sampler — rather than by its fixed <see cref="Slot"/>.
    /// </param>
    internal readonly record struct Resource(
        uint Binding, uint Slot, ComputeResourceKind Kind, string Name, bool IsVolume = false, bool ByUnit = false);

    /// <summary>One scalar or vector parameter, and where it sits in the globals block.</summary>
    /// <param name="Name">The uniform's name, as the body references it.</param>
    /// <param name="Offset">Its byte offset within the block, by the std140 rule.</param>
    /// <param name="Size">How many bytes the value occupies.</param>
    /// <param name="Type">
    /// The type it was declared with. Carried rather than inferred from <paramref name="Size"/>, because
    /// a size does not identify a type — a float and an int are both four bytes, and re-declaring one as
    /// the other silently reinterprets every value written to it.
    /// </param>
    /// <param name="ArrayLength">
    /// How many elements, for an array; zero for a plain uniform, and negative for the per-element entry
    /// that exists only so <c>name[i]</c> can be written to, which must not be declared a second time.
    /// </param>
    internal readonly record struct Uniform(string Name, int Offset, int Size, string Type = "", int ArrayLength = 0);

    /// <summary>The rewritten source and everything read out of it.</summary>
    /// <param name="Source">GLSL a Vulkan target accepts.</param>
    /// <param name="Resources">The storage buffers, images and samplers, by binding.</param>
    /// <param name="Uniforms">The gathered scalar parameters, by name.</param>
    /// <param name="UniformBlockBinding">The binding the globals block was placed at, or -1 if empty.</param>
    /// <param name="UniformBlockSize">The globals block's size in bytes, rounded up to 16.</param>
    internal sealed record Analysis(
        string Source,
        IReadOnlyList<Resource> Resources,
        IReadOnlyDictionary<string, Uniform> Uniforms,
        int UniformBlockBinding,
        int UniformBlockSize);

    /// <summary>Analyses and rewrites one compute source.</summary>
    /// <param name="source">The GL-dialect GLSL.</param>
    /// <param name="name">The shader's name, for error messages.</param>
    /// <returns>The rewritten source and its layout.</returns>
    /// <exception cref="GraphicsDeviceException">Two resources share a binding, or a uniform is unsupported.</exception>
    public static Analysis Analyse(string source, string name)
    {
        // GL keeps storage buffers, images and samplers in three separate binding spaces, so the same
        // number can mean an SSBO here and an image there; Vulkan has one space per set. Each resource is
        // given a fresh, unique Vulkan binding in declaration order, and the number the source wrote is
        // rewritten to it. The original per-kind number is kept as the resource's Slot, because that is
        // what the API still binds by — BindStorageBuffer, BindImage and SetTexture all speak GL's numbers.
        MatchCollection matches = ResourceRegex().Matches(source);
        List<Resource> resources = [];
        List<(int Index, int Length, string Text)> rewrites = [];
        HashSet<(ComputeResourceKind, uint)> perKind = [];
        uint nextBinding = 0;

        foreach (Match match in matches)
        {
            uint slot = ParseBinding(match.Groups["layout"].Value, match.Groups["res"].Value, name);
            ComputeResourceKind kind = match.Groups["ssbo"].Success
                ? ComputeResourceKind.StorageBuffer
                : match.Groups["image"].Success
                    ? ComputeResourceKind.StorageImage
                    : match.Groups["scene"].Success
                        ? ComputeResourceKind.Scene
                        : ComputeResourceKind.SampledImage;

            // Two resources of the same kind at one number is a genuine fault, not a namespace overlap.
            if (!perKind.Add((kind, slot)))
            {
                throw new GraphicsDeviceException(
                    $"Compute shader '{name}' binds two {kind} resources at slot {slot}.");
            }

            uint binding = nextBinding++;
            bool isVolume = match.Groups["image"].Value.Contains("3D", StringComparison.Ordinal)
                || match.Groups["sampler"].Value.Contains("3D", StringComparison.Ordinal)
                || match.Groups["sampler"].Value.Contains("Cube", StringComparison.Ordinal);
            resources.Add(new Resource(binding, slot, kind, match.Groups["res"].Value, isVolume));

            // Only the binding number inside this resource's own layout is rewritten; its other qualifiers
            // — std430, the image format — are left exactly as written.
            string rewritten = BindingRegex().Replace(match.Value, $"binding = {binding}", 1);
            rewrites.Add((match.Index, match.Length, rewritten));
        }

        // GL default-block samplers — `uniform sampler2D uName;` with no layout — are bound by a texture
        // unit the engine assigns through SetComputeUniform, not by a declared binding. Vulkan needs every
        // sampler in the set layout, so each is given a fresh binding, its declaration rewritten to carry
        // it, and it is resolved at dispatch by the unit its name was set to.
        foreach (Match match in DefaultSamplerRegex().Matches(source))
        {
            string type = match.Groups["type"].Value;
            string field = match.Groups["name"].Value;
            uint binding = nextBinding++;
            bool isVolume = type.Contains("3D", StringComparison.Ordinal) || type.Contains("Cube", StringComparison.Ordinal);
            resources.Add(new Resource(binding, 0, ComputeResourceKind.SampledImage, field, isVolume, ByUnit: true));

            string replacement = $"{match.Groups["indent"].Value}layout(set = 0, binding = {binding}) uniform {type} {field};";
            rewrites.Add((match.Index, match.Length, replacement));
        }

        string rebased = ApplyRewrites(source, rewrites);
        (string body, Dictionary<string, Uniform> uniforms, int blockSize) = GatherUniforms(rebased, name);

        int blockBinding = -1;
        if (uniforms.Count > 0)
        {
            // The gathered globals are one more descriptor in the same set, past every resource's binding.
            blockBinding = (int)nextBinding;
            body = InsertUniformBlock(body, blockBinding, uniforms);
        }

        if (resources.Any(r => r.Kind == ComputeResourceKind.Scene))
        {
            body = EnableRayQuery(body);
        }

        return new Analysis(body, resources, uniforms, blockBinding, blockSize);
    }

    /// <summary>Raises a shader to the language ray query is defined against, and asks for it.</summary>
    /// <param name="source">The rewritten source.</param>
    /// <returns>The source with a version and an extension a ray query compiles under.</returns>
    /// <remarks>
    /// These shaders are authored for GL 4.3, and ray query does not exist there. A 4.50 shader is not told
    /// the version is too low either — it is told <c>rayQueryEXT</c> is an undeclared identifier, which
    /// reads like the stage does not support it and sends you looking in the wrong place entirely. So the
    /// version is raised where a trace is present and left alone everywhere else.
    /// </remarks>
    private static string EnableRayQuery(string source)
    {
        Match version = VersionRegex().Match(source);
        const string raised = "#version 460\n#extension GL_EXT_ray_query : require\n";

        return version.Success
            ? source[..version.Index] + raised + source[(version.Index + version.Length)..]
            : raised + source;
    }

    private static string ApplyRewrites(string source, List<(int Index, int Length, string Text)> rewrites)
    {
        StringBuilder builder = new(source);

        // In reverse, so an earlier edit never shifts a later one's offset.
        foreach ((int index, int length, string text) in rewrites.OrderByDescending(r => r.Index))
        {
            builder.Remove(index, length);
            builder.Insert(index, text);
        }

        return builder.ToString();
    }

    private static uint ParseBinding(string layout, string resourceName, string shaderName)
    {
        Match binding = BindingRegex().Match(layout);
        if (!binding.Success)
        {
            throw new GraphicsDeviceException(
                $"Compute shader '{shaderName}' declares '{resourceName}' without an explicit binding; "
                + "the Vulkan backend needs every compute resource numbered.");
        }

        return uint.Parse(binding.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static (string Body, Dictionary<string, Uniform> Uniforms, int BlockSize) GatherUniforms(
        string source, string name)
    {
        Dictionary<string, Uniform> uniforms = new(StringComparer.Ordinal);
        int cursor = 0;

        string body = UniformRegex().Replace(source, match =>
        {
            string type = match.Groups["type"].Value;
            string field = match.Groups["name"].Value;
            string length = match.Groups["length"].Value;
            (int align, int size, int stored) = Std140(type, field, name);

            if (length.Length > 0)
            {
                // std140 pads every element of an array out to sixteen bytes, whatever the element is.
                int count = int.Parse(length, CultureInfo.InvariantCulture);
                int stride = Align(size, 16);
                cursor = Align(cursor, 16);

                uniforms[field] = new Uniform(field, cursor, stride * count, type, count);
                for (int i = 0; i < count; i++)
                {
                    // The caller names an element the way the shader body does. These are write targets
                    // only; the array is declared once, above, so they are marked not to be declared.
                    string element = string.Create(CultureInfo.InvariantCulture, $"{field}[{i}]");
                    uniforms[element] = new Uniform(element, cursor + (i * stride), stored, type, -1);
                }

                cursor += stride * count;
                return string.Empty;
            }

            cursor = Align(cursor, align);
            uniforms[field] = new Uniform(field, cursor, stored, type);
            cursor += size;

            // Removed from where it stood; re-declared together inside the injected block. A blank line
            // keeps the reported line numbers close to the original for any later compile error.
            return string.Empty;
        });

        int blockSize = Align(cursor, 16);
        return (body, uniforms, blockSize);
    }

    private static string InsertUniformBlock(string source, int binding, Dictionary<string, Uniform> uniforms)
    {
        // Re-declared in offset order, so the block the compiler lays out matches the offsets computed
        // here. Anonymous, so every member stays referenced by its bare name exactly as the default-block
        // uniform was — the body needs no rewriting.
        StringBuilder block = new();
        block.Append("layout(std140, set = 0, binding = ").Append(binding).Append(") uniform _IgiGlobals {\n");
        foreach (Uniform uniform in uniforms.Values.Where(u => u.ArrayLength >= 0).OrderBy(u => u.Offset))
        {
            block.Append("    ").Append(TypeOf(uniform)).Append(' ').Append(uniform.Name);
            if (uniform.ArrayLength > 0)
            {
                block.Append('[').Append(uniform.ArrayLength.ToString(CultureInfo.InvariantCulture)).Append(']');
            }

            block.Append(";\n");
        }

        block.Append("};\n");

        // After the #version line (and any extension directives that must follow it). Placing it before
        // #version is a compile error; after the first newline past the directives is always valid.
        int insertAt = DirectivesEnd(source);
        return source[..insertAt] + block + source[insertAt..];
    }

    /// <summary>The type a gathered uniform is re-emitted with.</summary>
    private static string TypeOf(Uniform uniform) => uniform.Type.Length > 0 ? uniform.Type : "vec4";

    private static int DirectivesEnd(string source)
    {
        int position = 0;
        int lastDirectiveEnd = 0;
        while (position < source.Length)
        {
            int lineEnd = source.IndexOf('\n', position);
            if (lineEnd < 0)
            {
                lineEnd = source.Length - 1;
            }

            string line = source[position..(lineEnd + 1)].TrimStart();
            if (line.StartsWith("#version", StringComparison.Ordinal)
                || line.StartsWith("#extension", StringComparison.Ordinal))
            {
                lastDirectiveEnd = lineEnd + 1;
            }

            position = lineEnd + 1;
        }

        return lastDirectiveEnd;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    /// <summary>The std140 alignment, footprint and stored byte count for one uniform type.</summary>
    private static (int Align, int Size, int Stored) Std140(string type, string field, string shaderName) => type switch
    {
        "float" or "int" or "uint" or "bool" => (4, 4, 4),
        "vec2" or "ivec2" or "uvec2" => (8, 8, 8),
        "vec3" or "ivec3" or "uvec3" => (16, 12, 12),
        "vec4" or "ivec4" or "uvec4" => (16, 16, 16),
        "mat3" => (16, 48, 48),
        "mat4" => (16, 64, 64),
        _ => throw new GraphicsDeviceException(
            $"Compute shader '{shaderName}' has a uniform '{field}' of unsupported type '{type}'."),
    };

    [GeneratedRegex(
        @"layout\s*\(\s*(?<layout>[^)]*)\)\s*(?:readonly|writeonly|restrict|coherent|volatile|\s)*"
        + @"(?:(?<ssbo>buffer)|uniform\s+(?:readonly|writeonly|restrict|coherent|volatile|\s)*"
        + @"(?:(?<image>[iu]?image[23]D(?:Array)?)|(?<sampler>[iu]?sampler[23]D(?:Array|Shadow)?)"
        + @"|(?<scene>accelerationStructureEXT)))"
        + @"\s+(?<res>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled)]
    private static partial Regex ResourceRegex();

    [GeneratedRegex(@"^[ 	]*#version[^
]*
?
", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"binding\s*=\s*(\d+)", RegexOptions.Compiled)]
    private static partial Regex BindingRegex();

    [GeneratedRegex(
        @"^(?<indent>[ \t]*)uniform[ \t]+(?<type>[iu]?sampler(?:2D|3D|Cube)(?:Array|Shadow)?)"
        + @"[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;[ \t]*(?://[^\r\n]*)?$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex DefaultSamplerRegex();

    [GeneratedRegex(
        @"^[ \t]*uniform[ \t]+(?<type>float|int|uint|bool|vec2|vec3|vec4|ivec2|ivec3|ivec4|uvec2|uvec3|uvec4|mat3|mat4)"
        + @"[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:[ \t]*\[[ \t]*(?<length>\d+)[ \t]*\])?[ \t]*;[ \t]*(?://[^\r\n]*)?\r?$",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex UniformRegex();
}

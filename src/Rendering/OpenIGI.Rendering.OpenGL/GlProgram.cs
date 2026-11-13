using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.Shaders.Glsl;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>A linked program: the library's GLSL for each stage, its specialisation constants defined.</summary>
/// <remarks>
/// <para>
/// GLSL 4.30 has no specialisation constants. The GLSL forms declare each one as the macro
/// <c>SPIRV_CROSS_CONSTANT_ID_n</c> with its default, so a constant a stage is given is a <c>#define</c> placed
/// after the version line, spelt as the type its default is spelt in. Each stage is compiled with its own, which is
/// what a pipeline giving its vertex and fragment stages different constants means.
/// </para>
/// <para>
/// A draw's base vertex and first instance are read through <c>GL_ARB_shader_draw_parameters</c> where the driver
/// has it, and through two uniforms where it does not; their locations are found here, and a draw sets them.
/// </para>
/// </remarks>
internal static partial class GlProgram
{
    /// <summary>The linked program and the uniforms a draw keeps up to date.</summary>
    /// <param name="Name">The program object.</param>
    /// <param name="BaseVertex">The fallback base vertex uniform, or -1 where the driver supplies it.</param>
    /// <param name="BaseInstance">The fallback first instance uniform, or -1 where the driver supplies it.</param>
    public readonly record struct Linked(uint Name, int BaseVertex, int BaseInstance);

    /// <summary>Compiles and links a program, on the owning thread.</summary>
    /// <param name="gl">The entry points.</param>
    /// <param name="module">The module the stages come from.</param>
    /// <param name="stages">Each stage and the constants it is given.</param>
    /// <param name="label">A name, for errors and captures.</param>
    /// <returns>The program.</returns>
    /// <exception cref="InvalidOperationException">A stage did not compile, or the program did not link.</exception>
    public static Linked Build(
        GL gl,
        GlShaderModule module,
        IReadOnlyList<(ShaderStage Stage, IReadOnlyDictionary<int, RhiConstant>? Constants)> stages,
        string label)
    {
        uint program = gl.CreateProgram();
        List<uint> shaders = [];

        try
        {
            foreach ((ShaderStage stage, IReadOnlyDictionary<int, RhiConstant>? constants) in stages)
            {
                string source = Specialise(GlslCatalog.Get(module.Module, module.Variant, stage), constants);
                uint shader = gl.CreateShader(stage switch
                {
                    ShaderStage.Vertex => GLEnum.VertexShader,
                    ShaderStage.Fragment => GLEnum.FragmentShader,
                    _ => GLEnum.ComputeShader,
                });

                gl.ShaderSource(shader, source);
                gl.CompileShader(shader);
                gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
                if (compiled == 0)
                {
                    string log = gl.GetShaderInfoLog(shader);
                    gl.DeleteShader(shader);
                    throw new InvalidOperationException(
                        $"The {stage} stage of {module.Module} ({module.Variant.Name}) for '{label}' did not compile: {log.Trim()}");
                }

                gl.AttachShader(program, shader);
                shaders.Add(shader);
            }

            gl.LinkProgram(program);
            gl.GetProgram(program, GLEnum.LinkStatus, out int linked);
            if (linked == 0)
            {
                throw new InvalidOperationException(
                    $"{module.Module} ({module.Variant.Name}) for '{label}' did not link: {gl.GetProgramInfoLog(program).Trim()}");
            }
        }
        catch
        {
            gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            foreach (uint shader in shaders)
            {
                gl.DetachShader(program, shader);
                gl.DeleteShader(shader);
            }
        }

        return new Linked(
            program,
            gl.GetUniformLocation(program, "SPIRV_Cross_BaseVertex"),
            gl.GetUniformLocation(program, "SPIRV_Cross_BaseInstance"));
    }

    /// <summary>Defines the constants a stage is given, each spelt as the type of the default it replaces.</summary>
    /// <param name="source">The stage's GLSL.</param>
    /// <param name="constants">The constants, by specialisation id; null or empty for none.</param>
    /// <returns>The GLSL to compile.</returns>
    internal static string Specialise(string source, IReadOnlyDictionary<int, RhiConstant>? constants)
    {
        if (constants is null || constants.Count == 0)
        {
            return source;
        }

        StringBuilder defines = new();
        foreach ((int id, RhiConstant value) in constants.OrderBy(c => c.Key))
        {
            Match declared = DefaultOf(id).Match(source);
            if (!declared.Success)
            {
                // A constant the stage does not declare -- the other stage's, or one its variant compiled away.
                continue;
            }

            defines.Append("#define SPIRV_CROSS_CONSTANT_ID_")
                .Append(id.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(Spell(value, declared.Groups["value"].Value))
                .Append('\n');
        }

        if (defines.Length == 0)
        {
            return source;
        }

        int afterVersion = source.IndexOf('\n', StringComparison.Ordinal) + 1;
        return string.Concat(source.AsSpan(0, afterVersion), defines.ToString(), source.AsSpan(afterVersion));
    }

    // The value, in the form of the default it replaces: true/false, 1u, 1.0, or 1.
    private static string Spell(RhiConstant value, string spelledDefault)
    {
        string trimmed = spelledDefault.Trim();

        if (trimmed is "true" or "false")
        {
            return value.Number != 0 ? "true" : "false";
        }

        if (trimmed.EndsWith('u'))
        {
            uint bits = value.Kind == RhiConstantKind.Float ? (uint)value.Number : value.Bits;
            return bits.ToString(CultureInfo.InvariantCulture) + "u";
        }

        if (trimmed.Contains('.', StringComparison.Ordinal) || trimmed.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            float number = value.Kind == RhiConstantKind.Float ? BitConverter.UInt32BitsToSingle(value.Bits) : (float)value.Number;
            string text = number.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) ? text : text + ".0";
        }

        int whole = value.Kind == RhiConstantKind.Float ? (int)value.Number : unchecked((int)value.Bits);
        return whole.ToString(CultureInfo.InvariantCulture);
    }

    private static Regex DefaultOf(int id) => new(
        $@"#define SPIRV_CROSS_CONSTANT_ID_{id.ToString(CultureInfo.InvariantCulture)} (?<value>[^\n]+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
}

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace OpenIGI.Shaders.Glsl;

/// <summary>The shader library's GLSL forms: one GLSL 4.30 text per stage of every variant compiled for OpenGL.</summary>
/// <remarks>
/// The GLSL counterpart of <c>DxilCatalog</c>, kept in its own assembly so the browser build never carries it.
/// Which variants have a GLSL form, and the unit each resource binds at, is still <see cref="ShaderCatalog"/>'s
/// to say: <see cref="ShaderBinding.GlslUnit"/>, <see cref="ShaderPushBlock.GlslUnit"/> and
/// <see cref="ShaderReflection.GlslSamplers"/>.
/// </remarks>
public static class GlslCatalog
{
    private static readonly Assembly Assembly = typeof(GlslCatalog).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Gets one stage of one variant's GLSL.</summary>
    /// <param name="id">The module.</param>
    /// <param name="variant">The variant.</param>
    /// <param name="stage">The stage, which names the entry point the text was translated from.</param>
    /// <returns>The GLSL text, whose entry point is <c>main</c>.</returns>
    /// <exception cref="InvalidOperationException">The variant has no GLSL form for that stage.</exception>
    public static string Get(ShaderModuleId id, ShaderVariant variant, ShaderStage stage) =>
        ShaderCatalog.TryGetExternal(id, out ExternalShaderModule? external)
            ? external.Glsl is not null && external.Glsl.TryGetValue(stage, out string? text)
                ? text
                : throw new InvalidOperationException($"The shader module {external.Name} has no {stage} GLSL.")
            : Cache.GetOrAdd(
            ShaderCatalog.ResourceName(id, variant, $"{ShaderCatalog.StageName(stage)}.glsl"),
            static name =>
            {
                using Stream stream = Assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException(
                        $"The GLSL resource {name} is not embedded; run tools/OpenIGI.Tools.ShaderBuild.");
                using StreamReader reader = new(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            });
}

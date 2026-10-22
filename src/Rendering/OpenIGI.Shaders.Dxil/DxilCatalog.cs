using System.Collections.Concurrent;
using System.Reflection;

namespace OpenIGI.Shaders.Dxil;

/// <summary>The shader library's DXIL forms: one signed blob per stage of every variant compiled for Direct3D 12.</summary>
/// <remarks>
/// The DXIL counterpart of <see cref="ShaderCatalog.GetSpirv(ShaderModuleId, ShaderVariant)"/>, kept in its
/// own assembly so the browser build never carries it. Which variants have a DXIL form, and how each binds,
/// is still <see cref="ShaderCatalog"/>'s to say.
/// </remarks>
public static class DxilCatalog
{
    private static readonly Assembly Assembly = typeof(DxilCatalog).Assembly;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    /// <summary>Gets one stage of one variant's DXIL.</summary>
    /// <param name="id">The module.</param>
    /// <param name="variant">The variant.</param>
    /// <param name="stage">The stage, which names the entry point the blob was compiled from.</param>
    /// <returns>The signed DXIL container.</returns>
    /// <exception cref="InvalidOperationException">The variant has no DXIL form for that stage.</exception>
    public static ReadOnlyMemory<byte> Get(ShaderModuleId id, ShaderVariant variant, ShaderStage stage) =>
        ShaderCatalog.TryGetExternal(id, out ExternalShaderModule? external)
            ? external.Dxil.TryGetValue(stage, out byte[]? blob)
                ? blob
                : throw new InvalidOperationException($"The shader module {external.Name} has no {stage} DXIL.")
            : Cache.GetOrAdd(
            ShaderCatalog.ResourceName(id, variant, $"{ShaderCatalog.StageName(stage)}.dxil"),
            static name =>
            {
                using Stream stream = Assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException(
                        $"The DXIL resource {name} is not embedded; run tools/OpenIGI.Tools.ShaderBuild.");
                byte[] bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                return bytes;
            });
}

using System.Text;
using System.Text.Json;

namespace OpenIGI.Shaders;

/// <summary>A shader module from outside the library: a mod's, compiled by the same tool into the same forms.</summary>
/// <remarks>
/// Registered with <see cref="ShaderCatalog.Register"/>, it is looked up by the id that gives it like any of the library's, so a
/// pass and every backend build it without knowing where it came from. Only its default variant is read.
/// </remarks>
/// <param name="Name">The module's name, as the mod's own manifest lists it.</param>
/// <param name="Reflection">What it binds and declares.</param>
/// <param name="Spirv">Its SPIR-V, or <see langword="null"/> where it was not compiled for Vulkan.</param>
/// <param name="Wgsl">Its WGSL, or <see langword="null"/> where it was not compiled for WebGPU.</param>
/// <param name="Dxil">Its DXIL, one blob a stage; empty where it was not compiled for Direct3D 12.</param>
public sealed record ExternalShaderModule(
    string Name,
    ShaderReflection Reflection,
    byte[]? Spirv,
    string? Wgsl,
    IReadOnlyDictionary<ShaderStage, byte[]> Dxil)
{
    /// <summary>Reads a module the shader tool compiled into a folder: <c>{name}.default.*</c>, as the library files its own.</summary>
    /// <param name="directory">The folder, a mod's compiled shaders.</param>
    /// <param name="name">The module's name.</param>
    /// <returns>The module.</returns>
    /// <exception cref="InvalidOperationException">Its reflection is not there or cannot be read.</exception>
    public static ExternalShaderModule Read(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        string stem = Path.Combine(directory, $"{name}.{ShaderVariant.DefaultName}");
        string reflectionPath = stem + ".reflection.json";
        if (!File.Exists(reflectionPath))
        {
            throw new InvalidOperationException($"The shader module {name} has no reflection at {reflectionPath}.");
        }

        ShaderReflection reflection;
        try
        {
            reflection = JsonSerializer.Deserialize(File.ReadAllBytes(reflectionPath), ShaderJsonContext.Default.ShaderReflection)
                ?? throw new InvalidOperationException($"The reflection of the shader module {name} is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"The reflection of the shader module {name} cannot be read: {error.Message}", error);
        }

        Dictionary<ShaderStage, byte[]> dxil = [];
        foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
        {
            string path = $"{stem}.{ShaderCatalog.StageName(stage)}.dxil";
            if (File.Exists(path))
            {
                dxil[stage] = File.ReadAllBytes(path);
            }
        }

        return new ExternalShaderModule(
            name,
            reflection,
            File.Exists(stem + ".spv") ? File.ReadAllBytes(stem + ".spv") : null,
            File.Exists(stem + ".wgsl") ? Encoding.UTF8.GetString(File.ReadAllBytes(stem + ".wgsl")) : null,
            dxil);
    }

    /// <summary>Gets whether the module has a form for a target.</summary>
    /// <param name="target">The code a backend consumes.</param>
    /// <returns>Whether it does.</returns>
    public bool Compiles(ShaderTarget target) => target switch
    {
        ShaderTarget.Spirv => Spirv is not null,
        ShaderTarget.Wgsl => Wgsl is not null,
        ShaderTarget.Dxil => Dxil.Count > 0,
        _ => false,
    };
}

using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>Where a compiled variant binds each of the things it reads, looked up by name.</summary>
/// <remarks>
/// <para>
/// A descriptor write names a number, and a number written out beside the shader that declares it is a
/// copy that drifts: split one combined sampler into a texture and a sampler and every binding after it
/// moves, with nothing to say so. Writing to the number the shader itself reports cannot drift, and
/// costs a dictionary lookup on a path that runs once a frame.
/// </para>
/// <para>
/// It also answers whether a variant declares a thing at all, which is what the conditional bindings
/// need: the traced form of the shading declares a visibility texture and the world it traces against,
/// the software form declares neither, and a write to a binding the layout does not have is a
/// validation error rather than a harmless no-op.
/// </para>
/// </remarks>
internal sealed class ShaderBindingTable
{
    private readonly Dictionary<string, uint> _bindings;

    /// <summary>Initialises a table from one descriptor set of a compiled variant.</summary>
    /// <param name="reflection">The compiled variant to read.</param>
    /// <param name="group">Which descriptor set to take.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reflection"/> is <see langword="null"/>.</exception>
    public ShaderBindingTable(ShaderReflection reflection, int group = 0)
    {
        ArgumentNullException.ThrowIfNull(reflection);

        _bindings = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (binding.Group == group)
            {
                _bindings[binding.Name] = (uint)binding.Binding;
            }
        }

        Module = reflection.Module.ToString();
        Variant = reflection.Variant;
    }

    /// <summary>Gets the module this describes, for a failure that has to name the shader.</summary>
    public string Module { get; }

    /// <summary>Gets the variant this describes.</summary>
    public string Variant { get; }

    /// <summary>Gets where a named resource is bound.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <returns>Its binding index.</returns>
    /// <exception cref="InvalidOperationException">The variant declares no such resource.</exception>
    public uint this[string name] => _bindings.TryGetValue(name, out uint binding)
        ? binding
        : throw new InvalidOperationException(
            $"Shader {Module}.{Variant} declares nothing called '{name}' to bind.");

    /// <summary>Whether the variant declares a named resource at all.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <returns><see langword="true"/> if it is declared and so must be written.</returns>
    public bool Declares(string name) => _bindings.ContainsKey(name);
}

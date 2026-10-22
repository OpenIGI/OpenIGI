using OpenIGI.Shaders;

namespace OpenIGI.Rendering;

/// <summary>One pass a mod runs over the lit scene before the tone map, by the shader module it registered.</summary>
/// <param name="Name">The pass's name, which its values are set by.</param>
/// <param name="Module">The module, from <see cref="ShaderCatalog.Register"/>.</param>
public readonly record struct PostPassModule(string Name, ShaderModuleId Module);

/// <summary>The passes mods run over the lit scene before the tone map, and the values their settings give them.</summary>
/// <remarks>
/// Part of <see cref="IGraphicsDevice"/>. Every pass is one compute dispatch written against the contract in
/// <c>Shaders/Source/Mod/PostPass.slang</c>: the scene's colour and depth in, the colour out, sixteen values. They run in the
/// order given, after the shutter and before the bloom and the tone map, so the game's exposure and grade see what they
/// leave. A device that runs no such passes ignores both members.
/// </remarks>
public interface IPostPassDevice
{
    /// <summary>Sets the passes, in the order they run; none to run none.</summary>
    /// <param name="passes">The passes.</param>
    /// <remarks>A pass whose module does not keep the contract is left out, and the device says why.</remarks>
    void SetPostPasses(IReadOnlyList<PostPassModule> passes)
    {
    }

    /// <summary>Sets the values a pass reads, up to sixteen; the rest read as nought.</summary>
    /// <param name="name">The pass's name.</param>
    /// <param name="values">The values, in the order its mod names them.</param>
    void SetPostPassValues(string name, ReadOnlySpan<float> values)
    {
    }
}

using OpenIGI.Core.Graphics;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering;

/// <summary>One pass a mod runs over the picture, by the shader module it registered.</summary>
/// <param name="Name">The pass's name, which its values are set by.</param>
/// <param name="Module">The module, from <see cref="ShaderCatalog.Register"/>.</param>
/// <param name="Stage">Where it runs: over the lit scene, or over the finished picture.</param>
public readonly record struct PostPassModule(string Name, ShaderModuleId Module, PostPassStage Stage = PostPassStage.Scene);

/// <summary>The passes mods run over the picture, and the values their settings give them.</summary>
/// <remarks>
/// <para>
/// Part of <see cref="IGraphicsDevice"/>. Every pass is one compute dispatch written against the contract in
/// <c>Shaders/Source/Mod/PostPass.slang</c>: the picture and the scene's depth in, the picture out, sixteen values. The passes
/// of each stage run in the order given.
/// </para>
/// <para>
/// A <see cref="PostPassStage.Scene"/> pass runs after the shutter and before the bloom and the tone map, so the game's exposure
/// and grade see what it leaves. A <see cref="PostPassStage.Display"/> pass runs after the tone map and the grade, over the
/// picture as it is shown and before the interface is drawn over it. A device that runs no such passes ignores both members.
/// </para>
/// </remarks>
public interface IPostPassDevice
{
    /// <summary>Sets the passes, in the order they run; none to run none.</summary>
    /// <param name="passes">The passes, of both stages.</param>
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

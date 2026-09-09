using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;

namespace OpenIGI.Game.Igi2;

/// <summary>I.G.I. 2: Covert Strike, as far as the engine can currently run it.</summary>
/// <remarks>
/// <para>
/// The layout and the format profile are real and measured against a retail installation. The module
/// list is <b>empty</b>, and that is not a placeholder: with no modules, every task type a level
/// declares becomes a <c>DeclaredTask</c> placeholder, and all 24 shipped levels build their trees
/// correctly with no gameplay behaviour attached. Adding I.G.I. 2's task classes is what the gameplay
/// milestones do, one module at a time.
/// </para>
/// <para>
/// TODO(phase-1): moves to <c>OpenIGI.Game.Igi2</c>.
/// </para>
/// </remarks>
public sealed class Igi2Runtime : IGameTitleRuntime
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2Runtime Instance { get; } = new();

    /// <inheritdoc/>
    public GameTitle Title => GameTitle.Igi2;

    /// <inheritdoc/>
    public ITitleDataLayout Layout => Igi2DataLayout.Instance;

    /// <inheritdoc/>
    public AssetFormatProfile Formats => AssetFormatProfile.Igi2;

    /// <inheritdoc/>
    /// <remarks>
    /// Empty until I.G.I. 2 has task classes. A level still loads: every declared type becomes a
    /// placeholder, which is exactly what the structural load tests assert.
    /// </remarks>
    public IReadOnlyList<IContentModule> Modules { get; } = [];
}

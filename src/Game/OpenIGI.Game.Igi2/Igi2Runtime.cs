using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;

namespace OpenIGI.Game.Igi2;

/// <summary>I.G.I. 2: Covert Strike, as far as the engine can currently run it.</summary>
/// <remarks>
/// The layout and the format profile are measured against a retail installation. The module list covers what the gameplay milestones have reached and no more; every type it does not
/// cover still loads, as a <c>DeclaredTask</c> placeholder carrying its declared parameters, so all 24
/// shipped levels build correct trees at every stage and gain behaviour a module at a time.
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
    public IReadOnlyList<IContentModule> Modules => Igi2ContentModules.All;
}

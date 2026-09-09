using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;

namespace OpenIGI.Game.Igi1;

/// <summary>Project I.G.I., as the engine runs it.</summary>
public sealed class Igi1Runtime : IGameTitleRuntime
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi1Runtime Instance { get; } = new();

    /// <inheritdoc/>
    public GameTitle Title => GameTitle.Igi1;

    /// <inheritdoc/>
    public ITitleDataLayout Layout => Igi1DataLayout.Instance;

    /// <inheritdoc/>
    public AssetFormatProfile Formats => AssetFormatProfile.Igi1;

    /// <inheritdoc/>
    public IReadOnlyList<IContentModule> Modules => Igi1ContentModules.All;
}

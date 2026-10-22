using OpenIGI.Core.Content;
using OpenIGI.Core.Titles;
using OpenIGI.Formats;

namespace OpenIGI.Engine.Titles;

/// <summary>Everything the engine needs in order to run one game title.</summary>
/// <remarks>
/// <para>
/// Resolved exactly once, in the app composition root, from the profile the player chose. Nothing below
/// the composition root ever asks <em>which</em> title is running: it asks the title. That is the
/// difference between this and a <c>switch</c> on an enum — there is one place where the question is
/// asked, and it is the same place that already chooses the graphics backend.
/// </para>
/// <para>
/// It is also what lets the host stop naming a title project. A host that reaches for
/// <c>Igi1ContentModules</c> by name is a host only I.G.I. 1 can use, however well factored everything
/// underneath it is.
/// </para>
/// </remarks>
public interface IGameTitleRuntime
{
    /// <summary>Gets the title this runtime implements.</summary>
    GameTitle Title { get; }

    /// <summary>Gets where the title keeps its data.</summary>
    ITitleDataLayout Layout { get; }

    /// <summary>Gets which encodings of each asset format this title's loaders accept.</summary>
    AssetFormatProfile Formats { get; }

    /// <summary>Gets the content modules, in registration order.</summary>
    /// <remarks>
    /// Order is behaviour: a module whose natives another module's tables call has to declare first, and
    /// the original's own start-up order is the specification for it.
    /// </remarks>
    IReadOnlyList<IContentModule> Modules { get; }

    /// <summary>Gets the folders of files that stand in for the title's own while it runs: its mods' data, highest first.</summary>
    /// <remarks>None for a title as it ships; the host stacks them over the player's packs and the game folder's overrides.</remarks>
    IReadOnlyList<TitleContentFolder> ContentFolders => [];

    /// <summary>Gets the campaigns beside the title's own: its mods', in their order.</summary>
    /// <remarks>
    /// Their missions are the title's like any other, found by its mission scan; they are kept out of its own progression,
    /// and the menu lists each campaign apart.
    /// </remarks>
    IReadOnlyList<TitleCampaign> Campaigns => [];

    /// <summary>Gets the mods the title is played with that change what happens in play, which its saves record.</summary>
    IReadOnlyList<ModReference> Mods => [];
}

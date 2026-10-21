using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;

namespace OpenIGI.Host.Titles;

/// <summary>Everything the host needs in order to run one game, resolved together.</summary>
/// <param name="Runtime">The title's layout, format profile and content modules.</param>
/// <param name="Presentation">The renderers it draws its world with.</param>
/// <param name="WorldBuilder">How a loaded level becomes placements.</param>
/// <param name="Missions">Builds the title's own mission table; a factory, because the scan logs as it goes.</param>
/// <param name="DataRoot">Where this title is installed, or empty when it is not.</param>
/// <param name="Gameplay">The title's own player, or <see langword="null"/> for a title that walks the host's rig.</param>
/// <remarks>
/// <para>
/// Four lookups that used to be four constructor parameters, resolved one title at a time. Bundling
/// them is not tidiness: the host can now be handed a <em>different</em> title's four while it is
/// running, and a switch that took three of the four would be a game drawing one title's world through
/// another's renderers.
/// </para>
/// <para>
/// They stay four separate things rather than one interface because the reference graph forbids joining
/// them — <c>Engine</c> cannot name a <c>WorldScene</c> and so <see cref="IGameTitleRuntime"/> cannot
/// carry the walk, and <c>Presentation</c> references <c>Engine</c> and so cannot be named by it.
/// </para>
/// </remarks>
public sealed record TitleBinding(
    IGameTitleRuntime Runtime,
    ITitlePresentationProvider Presentation,
    ITitleWorldBuilder WorldBuilder,
    Func<IOpenIgiLogger, IMissionCatalogue<MissionDescriptor>> Missions,
    string DataRoot,
    ITitleGameplay? Gameplay = null)
{
    /// <summary>Gets whether this title is installed where the catalogue looked.</summary>
    public bool IsInstalled => new GameProfile(Runtime.Title, DataRoot).IsInstalled;
}

/// <summary>The games a build can run, and where each of them is.</summary>
/// <remarks>
/// <para>
/// Implemented by the composition root, which is the only place allowed to name more than one title.
/// The host takes this rather than one title's pieces so that switching games at run time is the same
/// operation as starting in one — there is no second path that could get a different answer.
/// </para>
/// <para>
/// It is deliberately not a registry the host can add to. A title is a compile-time fact about which
/// content assemblies were linked, and discovering them by reflection would cost the AOT targets their
/// static call graph for a list that is three lines long.
/// </para>
/// </remarks>
public interface ITitleCatalog
{
    /// <summary>Gets every title this build can run, in the order a selector should list them.</summary>
    IReadOnlyList<GameTitle> Titles { get; }

    /// <summary>Resolves everything needed to run one title.</summary>
    /// <param name="title">The title to run.</param>
    /// <param name="binding">Receives the resolved services.</param>
    /// <returns><see langword="false"/> when this build has no implementation for that title.</returns>
    bool TryResolve(GameTitle title, [NotNullWhen(true)] out TitleBinding? binding);

    /// <summary>Makes the mission machinery the host runs every title's levels with.</summary>
    /// <param name="services">What the mission asks of the host.</param>
    /// <returns>The mission, made once for the host's lifetime.</returns>
    /// <remarks>
    /// Project I.G.I.'s, today, for every title: the sequel brings its own player and simulation through
    /// <see cref="TitleBinding.Gameplay"/> and runs beside it.
    /// </remarks>
    IHostedMission CreateHostedMission(IMissionHostServices services);
}

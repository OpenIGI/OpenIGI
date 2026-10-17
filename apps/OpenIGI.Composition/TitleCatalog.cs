using OpenIGI.Core.Configuration;
using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation.Igi2;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Titles;
using OpenIGI.Presentation;

namespace OpenIGI.Composition;

/// <summary>The titles a build can run, shared by every host that runs them.</summary>
/// <remarks>
/// <para>
/// <b>The only place that names more than one game.</b> Every layering rule about titles exists to keep
/// that true: a host, a renderer or a shared registry that reaches for one by name is a component only
/// that game can use, however well factored everything underneath it is.
/// </para>
/// <para>
/// Written by hand rather than discovered: a container that found these by reflection would cost the AOT
/// targets their static call graph. A mod edits <see cref="Titles"/>.
/// </para>
/// </remarks>
public static class TitleCatalog
{
    /// <summary>Gets the titles this build plays, each with everything it brings to the host.</summary>
    /// <remarks>
    /// The one list naming both games. A mod adds a title here, or replaces one's package -- its renderers, its scene walk,
    /// its player -- keeping its place.
    /// </remarks>
    public static TitleRegistry Titles { get; } = new TitleRegistry()
        .Register(new TitlePackage(
            Igi1Runtime.Instance,
            Igi1PresentationProvider.Instance,
            Igi1WorldBuilder.Instance,
            static log => new MissionRegistry(log)))
        .Register(new TitlePackage(
            Igi2Runtime.Instance,
            Igi2PresentationProvider.Instance,
            Igi2WorldBuilder.Instance,
            static log => new Igi2MissionCatalogue(log),
            Igi2Gameplay.Instance));

    /// <summary>Gets the catalogue the host resolves titles through.</summary>
    /// <remarks>
    /// Every registered package, bound to where the desktop finds it installed. Resolving is free, so it can happen on the
    /// frame a player picks a game.
    /// </remarks>
    public static ITitleCatalog Catalog => field ??= new HostCatalog(Titles.Packages, DiscoverDataRoot);

    /// <summary>Gets a catalogue of one title, installed at a root the host already knows.</summary>
    /// <param name="runtime">The title.</param>
    /// <param name="dataRoot">Where its data is.</param>
    /// <returns>The catalogue.</returns>
    /// <remarks>
    /// For a host with nothing to discover: a browser's installation is the folder the person picked,
    /// put where the page decided, and there is no second game beside it to switch to.
    /// </remarks>
    public static ITitleCatalog Rooted(IGameTitleRuntime runtime, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        return new HostCatalog([Titles.Get(runtime.Title)], _ => dataRoot);
    }

    /// <summary>Gets every title this build knows how to run.</summary>
    public static IReadOnlyList<IGameTitleRuntime> All => [.. Titles.Packages.Select(static p => p.Runtime)];

    /// <summary>Gets the title used when nothing selects one.</summary>
    /// <remarks>Project I.G.I., the first title the port played.</remarks>
    public static IGameTitleRuntime Default => Titles.Get(GameTitle.Igi1).Runtime;

    /// <summary>Names the environment variable that says where one title is installed.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The variable's name.</returns>
    /// <remarks>
    /// Per title, because a machine may well have both games installed and one variable cannot say where
    /// two of them are. The names are the ones the test suite already uses, so a shell that can run the
    /// retail tests can run the game with no further setting up.
    /// </remarks>
    public static string GamePathVariableFor(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.GamePathVariable;
    }

    /// <summary>Finds the runtime a title id names.</summary>
    /// <param name="id">The id, as <c>-title</c> or the environment gives it; null selects nothing.</param>
    /// <param name="runtime">Receives the runtime when the id names one.</param>
    /// <returns><see langword="false"/> when the id is not a title this build runs.</returns>
    /// <remarks>
    /// Case-insensitive, because a command line is typed by a person. An unknown id is reported rather
    /// than silently falling back: a run that was asked for one game and quietly gave another is worse
    /// than one that stops.
    /// </remarks>
    public static bool TryResolve(string? id, [NotNullWhen(true)] out IGameTitleRuntime? runtime)
    {
        runtime = Titles.TryGet(id, out TitlePackage? package) ? package.Runtime : null;
        return runtime is not null;
    }

    /// <summary>Gets the ids <see cref="TryResolve"/> accepts, for an error message.</summary>
    public static string KnownTitleIds => string.Join(", ", Titles.Packages.Select(static p => p.Title.Id));

    /// <summary>Finds a title that is actually installed, when nothing selected one.</summary>
    /// <param name="candidates">Directories to look in, in order of preference.</param>
    /// <param name="dataRoot">Receives where the title was found.</param>
    /// <returns>The runtime, or null when no title this build runs is installed anywhere it looked.</returns>
    /// <remarks>
    /// <para>
    /// Each title is tried against its own environment variable first and the shared candidates after,
    /// in the order <see cref="Titles"/> lists them -- so a machine with both installed runs the first, and a
    /// machine with only the sequel still starts rather than reporting the first game missing.
    /// </para>
    /// <para>
    /// A probe rather than a default, because the failure it replaces is a confusing one: pointing the
    /// port at an I.G.I. 2 installation and being told there is no <c>config.qvm</c> in it names a file
    /// that game does not have.
    /// </para>
    /// </remarks>
    public static IGameTitleRuntime? Probe(IEnumerable<string?> candidates, out string? dataRoot)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        string?[] shared = [.. candidates];
        foreach (TitlePackage package in Titles.Packages)
        {
            string?[] withOwn =
                [Environment.GetEnvironmentVariable(package.Title.GamePathVariable), .. shared];

            if (GameProfileResolver.DiscoverDataRoot(withOwn, package.Title) is { } found)
            {
                dataRoot = found;
                return package.Runtime;
            }
        }

        dataRoot = null;
        return null;
    }

    /// <summary>Finds the runtime for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its runtime.</returns>
    /// <exception cref="ArgumentException">This build has no runtime for that title.</exception>
    public static IGameTitleRuntime For(GameTitle title) => Titles.Get(title).Runtime;

    /// <summary>Finds the mission catalogue for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <param name="logger">Where scan diagnostics go.</param>
    /// <returns>Its catalogue.</returns>
    /// <exception cref="ArgumentException">This build has no such title.</exception>
    public static IMissionCatalogue<MissionDescriptor> MissionCatalogueFor(GameTitle title, IOpenIgiLogger? logger = null) =>
        Titles.Get(title).Missions(logger);

    /// <summary>Finds the scene walk for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its world builder.</returns>
    /// <exception cref="ArgumentException">This build has no such title.</exception>
    public static ITitleWorldBuilder WorldBuilderFor(GameTitle title) => Titles.Get(title).WorldBuilder;

    /// <summary>Finds the renderers for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its presentation provider.</returns>
    /// <exception cref="ArgumentException">This build has no such title.</exception>
    public static ITitlePresentationProvider PresentationFor(GameTitle title) => Titles.Get(title).Presentation;

    /// <summary>Finds a title's own player, for a title that brings one.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its gameplay, or <see langword="null"/> for a title that walks the host's own rig.</returns>
    /// <exception cref="ArgumentException">This build has no such title.</exception>
    public static ITitleGameplay? GameplayFor(GameTitle title) => Titles.Get(title).Gameplay;

    /// <summary>The catalogue as the host sees it: packages bound to where their titles are installed.</summary>
    private sealed class HostCatalog(
        IReadOnlyList<TitlePackage> packages,
        Func<GameTitle, string> dataRoot) : ITitleCatalog
    {
        public IReadOnlyList<GameTitle> Titles => [.. packages.Select(static p => p.Title)];

        public bool TryResolve(GameTitle title, [NotNullWhen(true)] out TitleBinding? binding)
        {
            ArgumentNullException.ThrowIfNull(title);
            TitlePackage? package = packages.FirstOrDefault(p => p.Title == title);
            binding = package?.Bind(dataRoot(title));
            return binding is not null;
        }
    }

    /// <summary>Where a title is installed, as a desktop finds it.</summary>
    /// <remarks>
    /// The title's own variable, then a probe of the places a build is normally dropped into. -game is
    /// the active title's alone and the composition root applies it, since it cannot mean two
    /// installations at once.
    /// </remarks>
    private static string DiscoverDataRoot(GameTitle title) =>
        Environment.GetEnvironmentVariable(title.GamePathVariable)
            ?? GameProfileResolver.DiscoverDataRoot(SearchPaths, title)
            ?? string.Empty;

    /// <summary>Where a title is looked for when its own variable says nothing.</summary>
    /// <remarks>
    /// The executable's folder, the app's own, and the working directory — so dropping the port into a
    /// game's directory is the whole of the installation procedure. Asked for by process path rather
    /// than <c>AppContext.BaseDirectory</c>, because a single-file build unpacks itself into a temporary
    /// folder and would otherwise search that.
    /// </remarks>
    private static string[] SearchPaths { get; } =
        [.. new[] { SettingsPaths.ExecutableDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

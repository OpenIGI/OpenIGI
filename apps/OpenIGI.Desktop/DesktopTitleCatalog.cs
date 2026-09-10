using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Host.Configuration;
using OpenIGI.Presentation;

namespace OpenIGI.Desktop;

/// <summary>The titles this build can run.</summary>
/// <remarks>
/// <para>
/// <b>The only place that names more than one game.</b> Every layering rule about titles exists to keep
/// that true: a host, a renderer or a shared registry that reaches for one by name is a component only
/// that game can use, however well factored everything underneath it is.
/// </para>
/// <para>
/// Written by hand rather than discovered. A container that found these by reflection would buy nothing
/// — there are two, and the list is three lines — and would cost the AOT targets their static call
/// graph.
/// </para>
/// </remarks>
internal static class DesktopTitleCatalog
{
    /// <summary>Gets every title this build knows how to run.</summary>
    public static IReadOnlyList<IGameTitleRuntime> All { get; } =
    [
        Igi1Runtime.Instance,
        Igi2Runtime.Instance,
    ];

    /// <summary>Gets the title used when nothing selects one.</summary>
    /// <remarks>
    /// Project I.G.I., because it is the title this port actually plays. I.G.I. 2 is selectable and its
    /// levels load, but it has no gameplay yet, so defaulting to it would be a worse first run.
    /// </remarks>
    public static IGameTitleRuntime Default => Igi1Runtime.Instance;

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
        return title == GameTitle.Igi2 ? "IGI2_GAME_PATH" : HostOptions.GamePathVariable;
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
        runtime = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (IGameTitleRuntime candidate in All)
        {
            if (string.Equals(candidate.Title.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                runtime = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the ids <see cref="TryResolve"/> accepts, for an error message.</summary>
    public static string KnownTitleIds => string.Join(", ", All.Select(static r => r.Title.Id));

    /// <summary>Finds a title that is actually installed, when nothing selected one.</summary>
    /// <param name="candidates">Directories to look in, in order of preference.</param>
    /// <param name="dataRoot">Receives where the title was found.</param>
    /// <returns>The runtime, or null when no title this build runs is installed anywhere it looked.</returns>
    /// <remarks>
    /// <para>
    /// Each title is tried against its own environment variable first and the shared candidates after,
    /// in the order <see cref="All"/> lists them — so a machine with both installed runs the one this
    /// port actually plays, and a machine with only the sequel still starts rather than reporting the
    /// first game missing.
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
        foreach (IGameTitleRuntime runtime in All)
        {
            string?[] withOwn =
                [Environment.GetEnvironmentVariable(GamePathVariableFor(runtime.Title)), .. shared];

            if (GameProfileResolver.DiscoverDataRoot(withOwn, runtime.Title) is { } found)
            {
                dataRoot = found;
                return runtime;
            }
        }

        dataRoot = null;
        return null;
    }

    /// <summary>Finds the runtime for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its runtime.</returns>
    /// <exception cref="ArgumentException">This build has no runtime for that title.</exception>
    public static IGameTitleRuntime For(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);

        foreach (IGameTitleRuntime runtime in All)
        {
            if (runtime.Title == title)
            {
                return runtime;
            }
        }

        throw new ArgumentException($"This build has no runtime for '{title.Id}'.", nameof(title));
    }

    /// <summary>Finds the mission catalogue for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <param name="logger">Where scan diagnostics go.</param>
    /// <returns>Its catalogue.</returns>
    /// <exception cref="ArgumentException">This build has no catalogue for that title.</exception>
    /// <remarks>
    /// A fourth lookup, and the reason is the same each time: the reference graph forbids joining them.
    /// What differs between the two is the reader, not the walk — <c>DefineMission</c> takes eleven
    /// arguments in one game and fourteen in the other, and the fourth argument of the first is a next
    /// mission id the second does not have.
    /// </remarks>
    public static IMissionCatalogue<MissionDescriptor> MissionCatalogueFor(GameTitle title, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (title == GameTitle.Igi1)
        {
            return new MissionRegistry(logger);
        }

        if (title == GameTitle.Igi2)
        {
            return new Igi2MissionCatalogue(logger);
        }

        throw new ArgumentException($"This build has no mission catalogue for '{title.Id}'.", nameof(title));
    }

    /// <summary>Finds the scene walk for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its world builder.</returns>
    /// <exception cref="ArgumentException">This build has no scene walk for that title.</exception>
    /// <remarks>
    /// A third lookup, for the same reason there is a second: <c>OpenIGI.Game</c> references
    /// <c>OpenIGI.Engine</c>, so <c>Engine</c> cannot name a <c>WorldScene</c> and
    /// <see cref="IGameTitleRuntime"/> cannot carry this. All three are resolved here, from the same
    /// title, in the one place allowed to name either game.
    /// </remarks>
    public static ITitleWorldBuilder WorldBuilderFor(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (title == GameTitle.Igi1)
        {
            return Igi1WorldBuilder.Instance;
        }

        if (title == GameTitle.Igi2)
        {
            return Igi2WorldBuilder.Instance;
        }

        throw new ArgumentException($"This build has no scene walk for {title}.", nameof(title));
    }

    /// <summary>Finds the renderers for a title.</summary>
    /// <param name="title">The title to run.</param>
    /// <returns>Its presentation provider.</returns>
    /// <exception cref="ArgumentException">This build has no renderers for that title.</exception>
    /// <remarks>
    /// A second lookup rather than a member of <see cref="IGameTitleRuntime"/>, because the reference
    /// graph forbids joining them: <c>OpenIGI.Presentation</c> references <c>OpenIGI.Engine</c>, so
    /// <c>Engine</c> cannot name a presenter. What matters is that both are resolved here, from the same
    /// title, in the one place allowed to name either game.
    /// </remarks>
    public static ITitlePresentationProvider PresentationFor(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (title == GameTitle.Igi1)
        {
            return Igi1PresentationProvider.Instance;
        }

        if (title == GameTitle.Igi2)
        {
            return Igi2PresentationProvider.Instance;
        }

        throw new ArgumentException($"This build has no renderers for '{title.Id}'.", nameof(title));
    }
}

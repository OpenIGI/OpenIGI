using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
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

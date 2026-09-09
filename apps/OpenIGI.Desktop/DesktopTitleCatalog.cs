using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Content;

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
}

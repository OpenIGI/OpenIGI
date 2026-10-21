using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;

namespace OpenIGI.Host.Titles;

/// <summary>Everything one game brings to the host: its runtime, its renderers, its scene walk, its missions and its player.</summary>
/// <param name="Runtime">The title's runtime: its identity, its content modules and its asset rules.</param>
/// <param name="Presentation">The title's renderers.</param>
/// <param name="WorldBuilder">The title's scene walk.</param>
/// <param name="Missions">Makes the title's mission catalogue.</param>
/// <param name="Gameplay">The title's own player, when it brings one; the host's I.G.I. rig otherwise.</param>
public sealed record TitlePackage(
    IGameTitleRuntime Runtime,
    ITitlePresentationProvider Presentation,
    ITitleWorldBuilder WorldBuilder,
    Func<IOpenIgiLogger?, IMissionCatalogue<MissionDescriptor>> Missions,
    ITitleGameplay? Gameplay = null)
{
    /// <summary>Gets the title.</summary>
    public GameTitle Title => Runtime.Title;

    /// <summary>Binds the package to an installation.</summary>
    /// <param name="dataRoot">The installation's data root.</param>
    /// <returns>The binding.</returns>
    public TitleBinding Bind(string dataRoot) =>
        new(Runtime, Presentation, WorldBuilder, log => Missions(log), dataRoot, Gameplay);
}

/// <summary>The titles this build can play, in the order they are offered; a mod adds a title or replaces one here.</summary>
/// <remarks>Registration is explicit, with no reflection, so the list is the same under NativeAOT.</remarks>
public sealed class TitleRegistry
{
    private readonly List<TitlePackage> _packages = [];

    /// <summary>Gets the packages, in the order they were registered.</summary>
    public IReadOnlyList<TitlePackage> Packages => _packages;

    /// <summary>Adds a title.</summary>
    /// <param name="package">The title's package.</param>
    /// <returns>This registry.</returns>
    /// <exception cref="ArgumentException">A title of the same id is registered already.</exception>
    public TitleRegistry Register(TitlePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (IndexOf(package.Title.Id) >= 0)
        {
            throw new ArgumentException($"A title '{package.Title.Id}' is registered already.", nameof(package));
        }

        _packages.Add(package);
        return this;
    }

    /// <summary>Replaces a registered title's package, keeping its place.</summary>
    /// <param name="package">The new package, whose title's id names the one it replaces.</param>
    /// <returns>This registry.</returns>
    /// <exception cref="KeyNotFoundException">No title of that id is registered.</exception>
    public TitleRegistry Replace(TitlePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        int index = IndexOf(package.Title.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"No title '{package.Title.Id}' is registered.");
        }

        _packages[index] = package;
        return this;
    }

    /// <summary>Finds a title's package by its id, ignoring case and surrounding space.</summary>
    /// <param name="id">The title's id.</param>
    /// <param name="package">The package, when found.</param>
    /// <returns>Whether it was found.</returns>
    public bool TryGet(string? id, [NotNullWhen(true)] out TitlePackage? package)
    {
        int index = string.IsNullOrWhiteSpace(id) ? -1 : IndexOf(id.Trim());
        package = index < 0 ? null : _packages[index];
        return package is not null;
    }

    /// <summary>Finds a title's package.</summary>
    /// <param name="title">The title.</param>
    /// <param name="package">The package, when found.</param>
    /// <returns>Whether it was found.</returns>
    public bool TryGet(GameTitle title, [NotNullWhen(true)] out TitlePackage? package)
    {
        ArgumentNullException.ThrowIfNull(title);
        package = _packages.Find(p => p.Title == title);
        return package is not null;
    }

    /// <summary>Gets a title's package.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The package.</returns>
    /// <exception cref="ArgumentException">This build has no such title.</exception>
    public TitlePackage Get(GameTitle title) =>
        TryGet(title, out TitlePackage? package)
            ? package
            : throw new ArgumentException($"This build has no title '{title.Id}'.", nameof(title));

    private int IndexOf(string id) =>
        _packages.FindIndex(p => string.Equals(p.Title.Id, id, StringComparison.OrdinalIgnoreCase));
}

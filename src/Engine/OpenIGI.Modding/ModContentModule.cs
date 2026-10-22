using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Events;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Modding;

/// <summary>A mod's own content module: what it registers, and what it does once a session has loaded its tables.</summary>
/// <param name="mod">The mod.</param>
/// <param name="title">The game.</param>
internal sealed class ModContentModule(InstalledMod mod, GameTitle title) : IContentModule
{
    /// <summary>The category the mods' diagnostics are logged under.</summary>
    public const string LogCategory = "Mods";

    /// <summary>Gets what the mod registers, in the order it said it.</summary>
    public List<Action<ContentRegistrationContext>> Registrations { get; } = [];

    /// <summary>Gets what the mod does once a session has loaded its tables, in the order it said it.</summary>
    public List<Action<IModContext>> Loads { get; } = [];

    /// <inheritdoc/>
    public string Name => $"Mod.{mod.Manifest.Id}";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        foreach (Action<ContentRegistrationContext> registration in Registrations)
        {
            registration(context);
        }
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
        if (Loads.Count == 0)
        {
            return;
        }

        Context loaded = new(mod, title, context);
        foreach (Action<IModContext> load in Loads)
        {
            load(loaded);
        }
    }

    // The session as the mod reads it.
    private sealed class Context(InstalledMod mod, GameTitle title, ContentLoadContext load) : IModContext
    {
        public ModManifest Manifest => mod.Manifest;

        public GameTitle Title => title;

        public string Directory => mod.Directory;

        public IServiceCatalog Services => load.Services;

        public GameEvents Events => load.Services.Require<GameEvents>();

        public VirtualFileSystem FileSystem => load.FileSystem;

        public ResourceRegistry Resources => load.Resources;

        public IOpenIgiLogger Logger => load.Logger;

        public bool TryGetDataRoot(GameTitle other, [NotNullWhen(true)] out string? root)
        {
            ArgumentNullException.ThrowIfNull(other);
            root = null;
            return load.Services.TryGet(out ITitleDataLocator? locator) && locator.TryGetDataRoot(other, out root);
        }
    }
}

using OpenIGI.Core.Content;
using OpenIGI.Engine.Titles;
using OpenIGI.Modding.Loader;

namespace OpenIGI.Modding.Testing;

/// <summary>A mod configured as the game configures it, for a test to look at what it adds and changes.</summary>
/// <remarks>
/// <para>
/// A harness holds one mod: its code as the test made it (<see cref="Of"/>), or its package as the launcher installs it, read
/// through the game's loader (<see cref="Installed"/>, <see cref="Load"/>). A test project built with the mod SDK
/// (<c>&lt;ModTests&gt;true&lt;/ModTests&gt;</c>) has each mod it references installed beside it, under <c>mods/</c>.
/// </para>
/// <para>
/// <see cref="Extend"/> is what the game does with the mod as a title starts; <see cref="Start"/> goes on to register the mod's
/// modules and load their tables, as a session does, and gives back what they published.
/// </para>
/// </remarks>
public sealed class ModHarness
{
    /// <summary>The folder beside a mod's tests its referenced mods are installed in.</summary>
    public const string ModsDirectoryName = "mods";

    private ModHarness(InstalledMod mod) => Mod = mod;

    /// <summary>Gets the mod.</summary>
    public InstalledMod Mod { get; }

    /// <summary>Gets the mod as the only one on.</summary>
    public ModSet Set => new([Mod]);

    /// <summary>Holds a mod's code as the test made it.</summary>
    /// <param name="mod">The mod.</param>
    /// <param name="id">Its id.</param>
    /// <param name="titles">The titles it is for, by id (<c>igi1</c>, <c>igi2</c>); none for both.</param>
    /// <param name="directory">Its folder, which its data and configuration are read from; none for a mod without one.</param>
    /// <returns>The harness.</returns>
    public static ModHarness Of(IOpenIgiMod mod, string id = "test", IReadOnlyList<string>? titles = null, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrEmpty(id);
        return new ModHarness(new InstalledMod(new ModManifest(id, id, "1.0", ModApi.Version, titles ?? []), directory ?? string.Empty, mod));
    }

    /// <summary>Loads a mod the test project references, from where the SDK installs it beside the tests.</summary>
    /// <param name="id">The mod's id.</param>
    /// <returns>The harness.</returns>
    public static ModHarness Installed(string id) => Load(Path.Combine(AppContext.BaseDirectory, ModsDirectoryName), id);

    /// <summary>Loads a mod through the game's loader, as the game loads its installed mods.</summary>
    /// <param name="modsDirectory">The folder the mod is installed in, one folder of its own among others.</param>
    /// <param name="id">The mod's id.</param>
    /// <returns>The harness.</returns>
    /// <exception cref="InvalidOperationException">The loader did not load the mod.</exception>
    public static ModHarness Load(string modsDirectory, string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(modsDirectory);
        ArgumentException.ThrowIfNullOrEmpty(id);
        InstalledMod mod = ModLoader.Load(modsDirectory).Mods.FirstOrDefault(m => string.Equals(m.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No mod {id} loads from {modsDirectory}.");
        return new ModHarness(mod);
    }

    /// <summary>The title as the game runs it with the mod on.</summary>
    /// <param name="runtime">The title.</param>
    /// <returns>The title with the mod's modules after its own.</returns>
    public IGameTitleRuntime Extend(IGameTitleRuntime runtime) => Set.Extend(runtime);

    /// <summary>Registers the mod's modules and loads their tables, as a session of the title does.</summary>
    /// <param name="runtime">The title.</param>
    /// <returns>What the mod's modules registered and published.</returns>
    public ModSession Start(IGameTitleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        IGameTitleRuntime modded = Extend(runtime);
        return ModSession.Start(modded.Modules.Skip(runtime.Modules.Count));
    }
}

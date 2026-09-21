using OpenIGI.Engine.Vfs;

namespace OpenIGI.Engine.Titles;

/// <summary>Where a game title keeps its data.</summary>
/// <remarks>
/// <para>
/// Only the paths the <em>engine</em> resolves are here. A path read by exactly one of a title's own
/// registries — <c>LOCAL:weapons</c>, <c>LOCAL:material/material.qvm</c> — is not configuration: it is
/// part of that registry, and the registry moves with its title. Putting them here would turn one
/// interface into a bag of forty properties that each had a single caller.
/// </para>
/// <para>
/// I.G.I. 2 keeps the identical alias vocabulary — <c>LOCAL:</c>, <c>MISSION:</c>, <c>COMPUTER:</c> and
/// <c>LANGUAGE:</c> appear verbatim in its binary — so what differs between the titles is the
/// <em>targets</em>, never the mechanism. That is why <see cref="VirtualFileSystem"/> itself needs no
/// change at all to run a second game.
/// </para>
/// </remarks>
public interface ITitleDataLayout
{
    /// <summary>Gets the alias bindings the virtual file system starts with, in bind order.</summary>
    /// <remarks>
    /// <c>LANGUAGE:</c> and <c>MISSION:</c> are deliberately absent from both titles' sets: both bind
    /// them at run time, when the language is chosen and when a mission loads.
    /// </remarks>
    IReadOnlyList<KeyValuePair<string, string>> StartupAliases { get; }

    /// <summary>Gets the directory the mission scan walks.</summary>
    string MissionRoot { get; }

    /// <summary>Gets the script name each mission directory publishes its descriptor from.</summary>
    string MissionScriptName { get; }

    /// <summary>Gets the script name a level's task tree is built from.</summary>
    string LevelScriptName { get; }

    /// <summary>Gets the id the campaign chain starts at.</summary>
    int FirstMissionId { get; }

    /// <summary>Gets the resource search directory the title falls back to for shared content.</summary>
    string CommonSearchDirectory { get; }

    /// <summary>Gets the archives mounted once per mission, before its own packs open, in mount order.</summary>
    /// <remarks>
    /// Order is behaviour, not preference: the first mount of a name is the one that answers to it,
    /// which is what stops a mission shadowing a shared asset by shipping one of the same name.
    /// </remarks>
    IReadOnlyList<string> SharedArchives { get; }

    /// <summary>Gets the sound directories mounted for every mission, in mount order.</summary>
    IReadOnlyList<string> SoundDirectories { get; }

    /// <summary>Gets the archives of displayable text the shell preloads before any mission.</summary>
    /// <remarks>
    /// Here rather than on whichever renderer shows the strings, because a path into an installation is
    /// what this interface is for and a renderer that knew one would be a renderer only one game could
    /// use. They are preloaded together because the shell reads them on the frame a menu first needs
    /// them, and a synchronous read of a language archive on the render thread is a visible hitch.
    /// </remarks>
    IReadOnlyList<string> StringArchives { get; }

    /// <summary>Gets the task types this title's level data spawns without declaring.</summary>
    /// <remarks>
    /// What makes a type structural is that the title's editor emits no declaration for it, which is a
    /// property of the editor rather than of the engine. I.G.I. 2 declares <c>Container</c> with a
    /// parameter of its own and so must not have the engine's parameterless one pre-registered.
    /// </remarks>
    IReadOnlyList<string> StructuralTaskTypes { get; }
}

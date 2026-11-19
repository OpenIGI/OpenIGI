namespace OpenIGI.Engine.Vfs;

/// <summary>Says whether a folder of the installation is in place yet, for a host that fetches folders only when wanted.</summary>
/// <remarks>
/// A desktop reads the installation from disk, and every folder is always there. A browser holds it in memory and reads a
/// location's shared pack only when it is first wanted, so a reader that reaches into a folder no mission has asked for --
/// a co-op look's body, borrowed from another location's pack -- must ask first. Asking is also the cue to fetch it.
/// </remarks>
public interface IFolderArrival
{
    /// <summary>Gets whether a folder's files are all in place now; asking starts fetching any that are not.</summary>
    /// <param name="directory">The folder, relative to the installation, such as <c>missions/multiplayer/common</c>.</param>
    /// <returns><see langword="true"/> when its files can be read.</returns>
    bool IsInPlace(string directory);
}

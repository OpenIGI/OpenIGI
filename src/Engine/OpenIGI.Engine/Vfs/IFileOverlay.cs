namespace OpenIGI.Engine.Vfs;

/// <summary>Files that stand in for the installation's own at the same paths: the content layers, mods' data among them.</summary>
/// <remarks>
/// <para>
/// Paths are relative to a device's root, as <c>OverridePath.For</c> makes them: <c>LOCAL:common/sounds/x.wav</c> is
/// <c>common/sounds/x.wav</c>.
/// </para>
/// <para>
/// A loose file stands in for the installation's at its path. An archive the installation has is never stood in for whole:
/// its entries are, one by one (<see cref="VirtualFileSystem.ReadArchive"/> and the resource registry). An archive only a
/// layer has -- a new mission's packs -- is read whole, as are the files and folders only a layer has, which the file
/// system lists beside the installation's.
/// </para>
/// </remarks>
public interface IFileOverlay
{
    /// <summary>Gets whether a loose file stands in at a path now.</summary>
    /// <param name="relativePath">The path, relative to its device's root.</param>
    /// <returns>Whether one does.</returns>
    bool Supplies(string relativePath);

    /// <summary>Reads the loose file that stands in at a path.</summary>
    /// <param name="relativePath">The path, relative to its device's root.</param>
    /// <param name="data">Receives its bytes, or empty when nothing stands in.</param>
    /// <returns>Whether a file was read.</returns>
    bool TryRead(string relativePath, out ReadOnlyMemory<byte> data);

    /// <summary>Gets whether a layer in force has a file at exactly a path, an archive included.</summary>
    /// <param name="relativePath">The path, relative to its device's root.</param>
    /// <returns>Whether one does.</returns>
    bool HasFile(string relativePath);

    /// <summary>Reads the file a layer in force has at exactly a path, an archive included, from the highest that has one.</summary>
    /// <param name="relativePath">The path, relative to its device's root.</param>
    /// <param name="data">Receives its bytes, or empty when no layer has one.</param>
    /// <returns>Whether a file was read.</returns>
    bool TryReadFile(string relativePath, out ReadOnlyMemory<byte> data);

    /// <summary>Lists the files the layers in force have in a folder.</summary>
    /// <param name="relativeDirectory">The folder, relative to its device's root.</param>
    /// <param name="searchPattern">A pattern such as <c>*.qvm</c>.</param>
    /// <returns>Their paths, relative to the device's root, each once.</returns>
    IEnumerable<string> EnumerateFiles(string relativeDirectory, string searchPattern);

    /// <summary>Lists the folders the layers in force have in a folder.</summary>
    /// <param name="relativeDirectory">The folder, relative to its device's root.</param>
    /// <returns>Their paths, relative to the device's root, each once.</returns>
    IEnumerable<string> EnumerateDirectories(string relativeDirectory);
}

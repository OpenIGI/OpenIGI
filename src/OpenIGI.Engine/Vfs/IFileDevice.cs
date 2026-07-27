namespace OpenIGI.Engine.Vfs;

/// <summary>
/// A backing store the virtual file system can open files from, addressed by a device name.
/// </summary>
/// <remarks>
/// <para>
/// In the original engine a resolved path always begins with a device name — <c>LOCAL:</c> is the one
/// the retail game uses. The file layer (<c>0x4B1510</c>) strips the device prefix and hands the
/// remainder to the device's own open function; an unknown device name is the fatal
/// <c>"Illegal device name in path: \"%s\"."</c> and a known but unregistered one is
/// <c>"Device not present: %s"</c>.
/// </para>
/// <para>
/// Modelling devices as an interface is what lets the port mount an archive, an asset bundle or a
/// mod directory later without touching the callers.
/// </para>
/// </remarks>
public interface IFileDevice
{
    /// <summary>Gets the device name, without the trailing colon and compared case-insensitively.</summary>
    string Name { get; }

    /// <summary>Determines whether a file exists on this device.</summary>
    /// <param name="path">The path relative to the device, with the device prefix already removed.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    bool Exists(string path);

    /// <summary>Opens a file for reading.</summary>
    /// <param name="path">The path relative to the device, with the device prefix already removed.</param>
    /// <param name="stream">Receives the opened stream, which the caller disposes.</param>
    /// <returns><see langword="true"/> when the file was opened.</returns>
    bool TryOpenRead(string path, out Stream? stream);

    /// <summary>Enumerates the files of a directory on this device.</summary>
    /// <param name="path">The directory path relative to the device.</param>
    /// <param name="searchPattern">A pattern such as <c>*.res</c>.</param>
    /// <returns>The matching paths relative to the device, or an empty sequence when the directory is absent.</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern);
}

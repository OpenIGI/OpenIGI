using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Host.Multiplayer;

/// <summary>The installation as a session plays it, through the game's own file system: what a server lists and sends, and what a client compares.</summary>
/// <param name="files">The game's file system.</param>
/// <param name="titleId">The game.</param>
/// <param name="withoutPins">
/// Whether a server's files put in place are passed over, so the installation's own are read: what a client compares a list
/// against, which tells a file that is its own again from one that only is while the server's stands in.
/// </param>
internal sealed class HostSessionFiles(VirtualFileSystem files, string titleId, bool withoutPins) : ISessionFileSource
{
    private const string Device = LocalFileDevice.DeviceName + ":";

    /// <inheritdoc/>
    public string TitleId { get; } = titleId;

    /// <inheritdoc/>
    public IEnumerable<string> ListFiles(string directory)
    {
        List<string> found = [];
        Stack<string> folders = new([Device + directory]);
        while (folders.Count > 0 && found.Count < SessionFileList.MaxFiles * 4)
        {
            string folder = folders.Pop();
            foreach (string file in files.EnumerateFiles(folder, "*"))
            {
                if (file.StartsWith(Device, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(GameplayFiles.Normalize(file[Device.Length..]));
                }
            }

            foreach (string child in files.EnumerateDirectories(folder))
            {
                if (child.StartsWith(Device, StringComparison.OrdinalIgnoreCase))
                {
                    folders.Push(child);
                }
            }
        }

        return found;
    }

    /// <inheritdoc/>
    public byte[]? Read(string path)
    {
        IReadOnlyDictionary<string, string>? pins = files.Pins;
        if (withoutPins)
        {
            files.Pins = null;
        }

        try
        {
            return files.TryOpenRead(Device + path, out Stream? stream) ? ReadAll(stream) : null;
        }
        catch (Core.Diagnostics.EngineFatalException)
        {
            return null;
        }
        finally
        {
            files.Pins = pins;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using (stream)
        {
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}

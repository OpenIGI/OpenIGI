using System.Buffers.Binary;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>
/// The level's lip-sync tracks, <c>SoundSys_ptLipsyncHashTable</c>: each <c>.syn</c> file the sound directories hold is a run of
/// floats, one a tick, keyed by its name without its path or extension (<c>SoundSys_RegisterLipsync</c>, <c>igi2 0x42DF40</c>).
/// A status message moves its speakers' mouths by the one named as its sound.
/// </summary>
public sealed class Igi2LipSync
{
    private const string Extension = ".syn";

    private readonly Dictionary<string, float[]> _tracks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the number of tracks.</summary>
    public int Count => _tracks.Count;

    /// <summary>Reads every track the directories hold, loose or in their archives.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="directories">The sound directories, in the order they are scanned.</param>
    /// <returns>The tracks.</returns>
    /// <remarks>
    /// The table chains a name's entries in the order they are made and finds the first, so a name's first file is its track.
    /// </remarks>
    public static Igi2LipSync Load(VirtualFileSystem files, IReadOnlyList<string> directories)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(directories);
        Igi2LipSync tracks = new();
        foreach (string directory in directories)
        {
            foreach (string archive in Listed(files, directory, "*.res"))
            {
                IresArchive opened;
                try
                {
                    opened = files.ReadArchive(archive);
                }
                catch (Exception ex) when (ex is IlffFormatException or IOException)
                {
                    continue;
                }

                foreach (IresEntry entry in opened.Entries)
                {
                    tracks.Add(entry.Name, entry.Data.Span);
                }
            }

            foreach (string loose in Listed(files, directory, "*" + Extension))
            {
                tracks.Add(loose, files.ReadAllBytes(loose));
            }
        }

        return tracks;
    }

    /// <summary>Gets a sound's track, <c>SoundSys_GetLipsync</c>.</summary>
    /// <param name="sound">The sound's name.</param>
    /// <returns>Its values, one a tick; empty when it has none.</returns>
    public ReadOnlySpan<float> Track(string sound) =>
        sound is not null && _tracks.TryGetValue(sound, out float[]? values) ? values : [];

    private static IEnumerable<string> Listed(VirtualFileSystem files, string directory, string pattern)
    {
        try
        {
            return files.EnumerateFiles(directory, pattern);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return [];
        }
    }

    // A whole file's floats: its length over four (SoundSys_RegisterLipsync keeps size >> 2 of them).
    private void Add(string path, ReadOnlySpan<byte> data)
    {
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string name = Path.GetFileNameWithoutExtension(path.Replace(':', '/'));
        if (name.Length == 0 || _tracks.ContainsKey(name))
        {
            return;
        }

        float[] values = new float[data.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 4)..]);
        }

        _tracks[name] = values;
    }
}

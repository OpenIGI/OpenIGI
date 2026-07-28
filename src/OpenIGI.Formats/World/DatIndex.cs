using System.Collections.ObjectModel;

namespace OpenIGI.Formats.World;

/// <summary>
/// The machine-generated <c>&lt;pack&gt;.dat</c> text index that sits beside an <c>.mtp</c>: one model
/// name followed by the texture names it uses.
/// </summary>
/// <remarks>
/// <strong>The original never reads this file.</strong> <c>Pack_Open</c> (<c>0x4CEFA0</c>) opens only the
/// <c>.mtp</c> and the two sibling <c>.res</c> archives; the <c>.dat</c> is build output, and its own header
/// says so. OpenIGI parses it for tooling and for cross-checking a pack, never during a level load.
/// </remarks>
public sealed class DatIndex
{
    private DatIndex(IReadOnlyList<DatEntry> entries) => Entries = entries;

    /// <summary>Gets the entries, in file order.</summary>
    public IReadOnlyList<DatEntry> Entries { get; }

    /// <summary>Parses a <c>.dat</c> index.</summary>
    /// <param name="text">The file contents; CRLF or LF line endings both work.</param>
    /// <returns>The parsed index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The text does not match the generated layout.</exception>
    public static DatIndex Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Comment lines start with '***'; blank lines separate the banner from the count.
        List<string> lines = [];
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim('\r', ' ', '\t');
            if (line.Length != 0 && !line.StartsWith("***", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0 || !int.TryParse(lines[0], out int count) || count < 0)
        {
            throw new PackFormatException("A .dat index starts with an entry count.");
        }

        DatEntry[] entries = new DatEntry[count];
        int cursor = 1;
        for (int i = 0; i < count; i++)
        {
            if (cursor + 1 >= lines.Count)
            {
                throw new PackFormatException($"The .dat index ended after {i} of {count} entries.");
            }

            string model = lines[cursor++];
            if (!int.TryParse(lines[cursor++], out int textureCount) || textureCount < 0)
            {
                throw new PackFormatException($"Entry '{model}' is not followed by a texture count.");
            }

            if (cursor + textureCount > lines.Count)
            {
                throw new PackFormatException($"Entry '{model}' declares {textureCount} textures but the file ended.");
            }

            string[] textures = new string[textureCount];
            for (int t = 0; t < textureCount; t++)
            {
                textures[t] = lines[cursor++];
            }

            entries[i] = new DatEntry(model, textures);
        }

        return new DatIndex(new ReadOnlyCollection<DatEntry>(entries));
    }

    /// <inheritdoc/>
    public override string ToString() => $".dat index, {Entries.Count} entries";
}

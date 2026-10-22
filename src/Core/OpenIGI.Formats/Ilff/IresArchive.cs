using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using OpenIGI.Core;

namespace OpenIGI.Formats.Ilff;

/// <summary>
/// An ILFF container with form type <c>IRES</c>: the resource archive behind every <c>.res</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The chunk list is a stream of <c>NAME</c> chunks, each followed by the payload chunks it names —
/// <c>BODY</c> for file contents, <c>PATH</c> for path nodes, <c>CSTR</c> for context strings. A
/// <c>NAME</c> stays in effect until the next one, which is why the walker is stateful.
/// </para>
/// <para>
/// A payload is never copied: every <see cref="IresEntry.Data"/> is a slice of the archive buffer, so
/// the buffer must outlive the archive.
/// </para>
/// </remarks>
public sealed class IresArchive
{
    private readonly Dictionary<string, IresEntry> _byName;

    private IresArchive(IlffContainer container, IReadOnlyList<IresEntry> entries, Dictionary<string, IresEntry> byName)
    {
        Container = container;
        Entries = entries;
        _byName = byName;
    }

    /// <summary>Gets the underlying container.</summary>
    public IlffContainer Container { get; }

    /// <summary>Gets every entry, in file order.</summary>
    public IReadOnlyList<IresEntry> Entries { get; }

    /// <summary>Parses an <c>ILFF</c>+<c>IRES</c> archive.</summary>
    /// <param name="data">The archive bytes.</param>
    /// <returns>The parsed archive.</returns>
    /// <exception cref="IlffFormatException">The bytes are not a valid IRES archive.</exception>
    public static IresArchive Read(ReadOnlyMemory<byte> data) => FromContainer(IlffContainer.Read(data));

    /// <summary>Wraps an already-parsed container as an archive.</summary>
    /// <param name="container">The container, whose form type must be <c>IRES</c>.</param>
    /// <returns>The parsed archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="IlffFormatException">The container is not an IRES archive, or its chunk list is malformed.</exception>
    public static IresArchive FromContainer(IlffContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (container.FormType != IlffTags.Ires)
        {
            throw new IlffFormatException($"Expected an IRES archive, found form type '{container.FormType}'.");
        }

        List<IresEntry> entries = [];
        CollectEntries(container, entries);

        Dictionary<string, IresEntry> byName = new(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in entries)
        {
            // The original hashes names into one table; the first registration wins and a duplicate is
            // reported rather than replacing the original. Keep that ordering.
            _ = byName.TryAdd(entry.Name, entry);
        }

        return new IresArchive(container, new ReadOnlyCollection<IresEntry>(entries), byName);
    }

    /// <summary>Looks up an entry by name.</summary>
    /// <param name="name">The resource name; backslashes are normalized before the lookup.</param>
    /// <param name="entry">Receives the entry when one is found.</param>
    /// <returns><see langword="true"/> when the archive contains the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Matching ignores case: the original shipped on a case-insensitive filesystem and its scripts are
    /// inconsistent about casing. When an archive holds two names differing only in case, the first one
    /// in file order wins.
    /// </remarks>
    public bool TryGetEntry(string name, [NotNullWhen(true)] out IresEntry? entry) =>
        _byName.TryGetValue(ResourcePath.Normalize(name), out entry);

    /// <summary>Finds the first body entry whose file name, wherever its directory, is the one given.</summary>
    /// <param name="fileName">The file name and extension, such as <c>smoke2.spr</c>; case is ignored.</param>
    /// <returns>The entry, or <see langword="null"/> when the archive holds no such body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    public IresEntry? FindBody(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (IresEntry entry in Entries)
        {
            if (entry.Kind == IresEntryKind.Body
                && Path.GetFileName(entry.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Gets a copy of the archive with some of its bodies replaced: a content layer's stand-ins for them.</summary>
    /// <param name="replacement">Gives an entry's new bytes, or <see langword="null"/> to keep its own.</param>
    /// <returns>This archive when nothing is replaced; otherwise the copy, its entries in the same order.</returns>
    public IresArchive WithBodies(Func<IresEntry, ReadOnlyMemory<byte>?> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        List<IresEntry> entries = new(Entries.Count);
        bool replaced = false;
        foreach (IresEntry entry in Entries)
        {
            if (entry.Kind == IresEntryKind.Body && replacement(entry) is { } data)
            {
                entries.Add(new IresEntry(entry.Name, entry.Kind, data));
                replaced = true;
            }
            else
            {
                entries.Add(entry);
            }
        }

        if (!replaced)
        {
            return this;
        }

        Dictionary<string, IresEntry> byName = new(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in entries)
        {
            _ = byName.TryAdd(entry.Name, entry);
        }

        return new IresArchive(Container, new ReadOnlyCollection<IresEntry>(entries), byName);
    }

    /// <inheritdoc/>
    public override string ToString() => $"IRES archive, {Entries.Count} entries, {Container.Size} bytes";

    private static void CollectEntries(IlffContainer container, List<IresEntry> entries)
    {
        string? currentName = null;

        foreach (IlffChunk chunk in container.Chunks)
        {
            if (chunk.Id == IlffTags.Name)
            {
                currentName = ResourcePath.Normalize(DecodeName(chunk));
                continue;
            }

            if (chunk.Id == IlffTags.Body)
            {
                entries.Add(CreateEntry(currentName, IresEntryKind.Body, chunk));
                continue;
            }

            if (chunk.Id == IlffTags.Path)
            {
                entries.Add(CreateEntry(currentName, IresEntryKind.Path, chunk));
                continue;
            }

            if (chunk.Id == IlffTags.Cstr)
            {
                entries.Add(CreateEntry(currentName, IresEntryKind.ContextString, chunk));
                continue;
            }

            if (chunk.IsContainer)
            {
                IlffContainer nested = chunk.AsContainer();
                if (nested.FormType == IlffTags.Ires)
                {
                    // Nested archives contribute their entries to the parent's namespace.
                    CollectEntries(nested, entries);
                }

                // A nested DBUG sub-form only carries debug context strings; the game ignores them and
                // so do we. Any other nested form is data the enclosing BODY already exposes.
                continue;
            }

            // Unknown chunk ids are skipped, matching the original walker's default branch.
        }
    }

    private static IresEntry CreateEntry(string? currentName, IresEntryKind kind, IlffChunk chunk)
    {
        if (currentName is null)
        {
            throw new IlffFormatException(
                $"Chunk '{chunk.Id}' at offset {chunk.OffsetInParent} appears before any NAME chunk.");
        }

        return new IresEntry(currentName, kind, chunk.Payload);
    }

    private static string DecodeName(IlffChunk chunk)
    {
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int terminator = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? span : span[..terminator]);
    }
}

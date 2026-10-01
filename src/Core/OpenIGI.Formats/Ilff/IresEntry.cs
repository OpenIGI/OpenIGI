using System.Text;

namespace OpenIGI.Formats.Ilff;

/// <summary>One named resource inside an <see cref="IresArchive"/>.</summary>
public sealed class IresEntry
{
    internal IresEntry(string name, IresEntryKind kind, ReadOnlyMemory<byte> data)
    {
        Name = name;
        Kind = kind;
        Data = data;
    }

    /// <summary>Gets the resource name, normalized to forward slashes.</summary>
    /// <remarks>
    /// Usually a virtual file system path such as <c>LOCAL:common/textures/shadow1.tex</c>, but the
    /// language archives use bare keys such as <c>OBJ_1_COMPLETE</c>.
    /// </remarks>
    public string Name { get; }

    /// <summary>Gets the chunk kind this entry came from.</summary>
    public IresEntryKind Kind { get; }

    /// <summary>Gets the payload bytes, as a slice of the archive buffer.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Decodes the payload as a NUL-terminated single-byte string.</summary>
    /// <returns>The decoded text, without the terminator.</returns>
    /// <remarks>
    /// Meaningful for <see cref="IresEntryKind.ContextString"/> and <see cref="IresEntryKind.Path"/>
    /// entries. Bytes are decoded as Latin-1, which is lossless for the single-byte text the original
    /// stores; the localized archives are re-checked against the real code page when the language
    /// system lands.
    /// </remarks>
    public string AsText()
    {
        ReadOnlySpan<byte> span = Data.Span;
        int terminator = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? span : span[..terminator]);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} [{Kind}] {Data.Length} bytes";
}

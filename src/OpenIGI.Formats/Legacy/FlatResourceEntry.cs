namespace OpenIGI.Formats.Legacy;

/// <summary>One named payload inside a <see cref="FlatResourceArchive"/>.</summary>
public sealed class FlatResourceEntry
{
    internal FlatResourceEntry(string name, ReadOnlyMemory<byte> data)
    {
        Name = name;
        Data = data;
    }

    /// <summary>Gets the resource name, normalized to forward slashes.</summary>
    public string Name { get; }

    /// <summary>Gets the payload bytes, as a slice of the archive buffer.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} {Data.Length} bytes";
}

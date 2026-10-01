namespace OpenIGI.Engine.Resources;

/// <summary>One named resource held by a <see cref="ResourceRegistry"/>.</summary>
public sealed class ResourceEntry
{
    internal ResourceEntry(string name, ResourceKind kind, ReadOnlyMemory<byte> data, string? parent)
    {
        Name = name;
        Kind = kind;
        Data = data;
        Parent = parent;
    }

    /// <summary>Gets the resource name, normalized to forward slashes.</summary>
    public string Name { get; }

    /// <summary>Gets what this entry holds.</summary>
    public ResourceKind Kind { get; }

    /// <summary>Gets the resource bytes.</summary>
    /// <remarks>
    /// For an entry that came out of an archive this is a slice of the archive buffer, not a copy, so
    /// the archive stays alive as long as any of its entries is registered.
    /// </remarks>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the name of the archive this entry was exploded from, or <see langword="null"/> when it stands alone.</summary>
    public string? Parent { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        Parent is null
            ? $"{Name} [{Kind}] {Data.Length} bytes"
            : $"{Name} [{Kind}] {Data.Length} bytes (from {Parent})";
}

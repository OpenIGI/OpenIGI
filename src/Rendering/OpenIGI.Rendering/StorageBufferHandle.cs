namespace OpenIGI.Rendering;

/// <summary>
/// An opaque reference to a shader-writable buffer owned by an <see cref="IGraphicsDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// The structure every stage from L1 on is built out of: a distance-field brick atlas's page table, a
/// card page list, a light grid, an indirect dispatch's argument list. What distinguishes these from
/// the vertex and index buffers the device already owns is that a <em>shader</em> writes them, which is
/// what makes a pass able to decide how much work the next pass does.
/// </para>
/// <para>
/// A value, like every other handle on this seam.
/// </para>
/// </remarks>
public readonly struct StorageBufferHandle : IEquatable<StorageBufferHandle>
{
    /// <summary>Initialises a new instance of the <see cref="StorageBufferHandle"/> struct.</summary>
    /// <param name="id">The backend-assigned identifier. Zero means <see cref="None"/>.</param>
    public StorageBufferHandle(uint id) => Id = id;

    /// <summary>Gets the backend-assigned identifier.</summary>
    public uint Id { get; }

    /// <summary>Gets the null handle.</summary>
    public static StorageBufferHandle None => default;

    /// <summary>Gets a value indicating whether this handle refers to a buffer.</summary>
    public bool IsValid => Id != 0;

    /// <summary>Determines whether two handles are equal.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(StorageBufferHandle left, StorageBufferHandle right) => left.Equals(right);

    /// <summary>Determines whether two handles differ.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(StorageBufferHandle left, StorageBufferHandle right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(StorageBufferHandle other) => Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StorageBufferHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => IsValid ? $"Storage({Id})" : "Storage(none)";
}

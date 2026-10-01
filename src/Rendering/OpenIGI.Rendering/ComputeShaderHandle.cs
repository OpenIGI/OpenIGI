namespace OpenIGI.Rendering;

/// <summary>
/// An opaque reference to a compute program owned by an <see cref="IGraphicsDevice"/>.
/// </summary>
/// <remarks>
/// A value, like every other handle on this seam: it can be stored and compared without keeping the
/// backend resource alive, and it is only meaningful to the device that produced it.
/// </remarks>
public readonly struct ComputeShaderHandle : IEquatable<ComputeShaderHandle>
{
    /// <summary>Initialises a new instance of the <see cref="ComputeShaderHandle"/> struct.</summary>
    /// <param name="id">The backend-assigned identifier. Zero means <see cref="None"/>.</param>
    public ComputeShaderHandle(uint id) => Id = id;

    /// <summary>Gets the backend-assigned identifier.</summary>
    public uint Id { get; }

    /// <summary>Gets the null handle.</summary>
    public static ComputeShaderHandle None => default;

    /// <summary>Gets a value indicating whether this handle refers to a program.</summary>
    public bool IsValid => Id != 0;

    /// <summary>Determines whether two handles are equal.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ComputeShaderHandle left, ComputeShaderHandle right) => left.Equals(right);

    /// <summary>Determines whether two handles differ.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ComputeShaderHandle left, ComputeShaderHandle right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ComputeShaderHandle other) => Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ComputeShaderHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => IsValid ? $"Compute({Id})" : "Compute(none)";
}

namespace OpenIGI.Rendering;

/// <summary>
/// An opaque reference to a texture owned by an <see cref="IGraphicsDevice"/>.
/// </summary>
/// <remarks>
/// Handles are values, not objects: they can be stored in game state and compared without keeping the
/// backend resource alive. A handle is only meaningful to the device that produced it; passing one to
/// a different device is undefined. <see cref="None"/> is the null handle and unbinds a texture unit.
/// </remarks>
public readonly struct TextureHandle : IEquatable<TextureHandle>
{
    /// <summary>Initializes a new instance of the <see cref="TextureHandle"/> struct.</summary>
    /// <param name="id">The backend-assigned identifier. Zero means <see cref="None"/>.</param>
    public TextureHandle(uint id) => Id = id;

    /// <summary>Gets the backend-assigned identifier.</summary>
    public uint Id { get; }

    /// <summary>Gets the null handle.</summary>
    public static TextureHandle None => default;

    /// <summary>Gets a value indicating whether this handle refers to a texture.</summary>
    public bool IsValid => Id != 0;

    /// <summary>Determines whether two handles are equal.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(TextureHandle left, TextureHandle right) => left.Equals(right);

    /// <summary>Determines whether two handles differ.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(TextureHandle left, TextureHandle right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(TextureHandle other) => Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextureHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => IsValid ? $"Texture({Id})" : "Texture(none)";
}

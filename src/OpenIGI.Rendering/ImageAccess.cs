namespace OpenIGI.Rendering;

/// <summary>
/// How a compute pass intends to touch an image it binds.
/// </summary>
/// <remarks>
/// Declared rather than inferred because the driver cannot see it: an image binding is a raw pointer
/// into a texture's storage, and telling it that a pass only reads is what lets it skip a flush it
/// would otherwise have to assume.
/// </remarks>
public enum ImageAccess
{
    /// <summary>The pass only samples the image.</summary>
    Read,

    /// <summary>The pass only writes to the image, and does not depend on what was there.</summary>
    Write,

    /// <summary>The pass both reads and writes, as an accumulation does.</summary>
    ReadWrite,
}

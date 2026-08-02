namespace OpenIGI.Rendering;

/// <summary>
/// What a compute pass wrote, so the next reader can be made to see it.
/// </summary>
/// <remarks>
/// <para>
/// Compute dispatches are not ordered against each other by default — the driver is free to overlap
/// them, which is most of why compute is fast. A barrier is how a pass that depends on a previous one's
/// output says so, and naming <em>what kind</em> of access has to be visible is what keeps the barrier
/// from being a full pipeline flush.
/// </para>
/// <para>
/// Flags rather than one value: a pass that wrote both a buffer and an image needs both, and issuing
/// two barriers where one would do is a stall the driver cannot optimise away.
/// </para>
/// </remarks>
[Flags]
public enum MemoryBarrierScope
{
    /// <summary>Nothing needs to be made visible.</summary>
    None = 0,

    /// <summary>Writes through a storage buffer, before another shader reads them.</summary>
    StorageBuffer = 1 << 0,

    /// <summary>Writes through an image binding, before another shader reads or samples them.</summary>
    Image = 1 << 1,

    /// <summary>Writes a later <c>DispatchIndirect</c> will read as its group counts.</summary>
    IndirectArguments = 1 << 2,

    /// <summary>Writes that a subsequent ordinary texture fetch has to see.</summary>
    TextureFetch = 1 << 3,

    /// <summary>Writes that a read or write through the texture API has to see.</summary>
    /// <remarks>
    /// Distinct from <see cref="TextureFetch"/>, which covers a shader sampling the texture. Reading a
    /// texture back with the API after a shader wrote it through an image binding needs THIS one, and
    /// the difference is not academic: without it the readback returns the texture's allocated contents
    /// rather than what the pass computed, which reads as a pass that never ran.
    /// </remarks>
    TextureUpdate = 1 << 4,

    /// <summary>Everything. Correct, and the slowest thing on this list.</summary>
    All = StorageBuffer | Image | IndirectArguments | TextureFetch | TextureUpdate,
}

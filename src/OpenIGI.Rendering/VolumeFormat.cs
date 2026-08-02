namespace OpenIGI.Rendering;

/// <summary>
/// What one voxel of a volume texture holds.
/// </summary>
/// <remarks>
/// A short list on purpose. Every volume this renderer builds is one of three things — a distance
/// field, a radiance cache, or an occupancy mask — and each wants a different trade between range and
/// bandwidth. Offering the whole of GL's format table here would let a caller pick something no pass
/// can read.
/// </remarks>
public enum VolumeFormat
{
    /// <summary>One byte per voxel. What a signed distance field is stored in.</summary>
    /// <remarks>
    /// A sphere trace needs to know how far it may step, not the distance to the millimetre — an error
    /// of a fraction of a voxel costs a step, never correctness. Four times the precision would cost
    /// four times the bandwidth on the hottest read in the renderer.
    /// </remarks>
    R8,

    /// <summary>Four bytes per voxel, for a volume carrying a colour and a weight.</summary>
    Rgba8,

    /// <summary>Four half-floats per voxel, for radiance that runs past one.</summary>
    /// <remarks>
    /// The format anything holding <em>light</em> wants. A bounced colour is not bounded by one, and
    /// clamping it to a byte is what turns a bright interior into a flat grey one.
    /// </remarks>
    Rgba16F,
}

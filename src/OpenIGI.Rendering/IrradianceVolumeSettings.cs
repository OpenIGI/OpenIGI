using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// How the baked probe field is applied to the frame.
/// </summary>
/// <remarks>
/// The volume itself is handed over separately and only when it changes, because it is megabytes of
/// baked transfer and does not vary per frame. What varies is here: the colours the sun and sky are
/// this frame, and how strongly the result is applied.
/// </remarks>
/// <param name="Enabled">Whether surfaces take their indirect light from the volume.</param>
/// <param name="Intensity">
/// How strongly the volume's light is applied. One is the amount the bake computed; the useful range
/// is around it, since the bake carries a single bounce and a real room has several.
/// </param>
/// <param name="SkyColour">
/// What the open sky is worth this frame. The bake stored how much sky reaches each probe, not what
/// colour it was, so a mission's own sky decides that here without rebaking anything.
/// </param>
public readonly record struct IrradianceVolumeSettings(
    bool Enabled = false,
    float Intensity = 1f,
    Vector3f SkyColour = default)
{
    /// <summary>Gets settings under which the volume contributes nothing.</summary>
    public static IrradianceVolumeSettings Off { get; }

    /// <summary>Gets the sky colour, defaulting to white when none was set.</summary>
    /// <remarks>
    /// A record struct's default is the zero one, and a zero sky is a level with no indirect light at
    /// all -- which reads as the volume having failed rather than as a setting being unset.
    /// </remarks>
    public Vector3f Sky => SkyColour.LengthSquared > 0f ? SkyColour : new Vector3f(1f, 1f, 1f);
}

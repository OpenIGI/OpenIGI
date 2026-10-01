namespace OpenIGI.Rendering;

/// <summary>
/// The shape a <see cref="LocalFogVolume"/> occupies.
/// </summary>
public enum FogVolumeShape
{
    /// <summary>An axis-aligned box. What a room, a corridor or a courtyard is.</summary>
    Box,

    /// <summary>A sphere. What a pocket of mist or a puff of smoke is.</summary>
    Sphere,
}

using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>The atmosphere's tables as the lighting pass binds them.</summary>
/// <param name="SkyView">The sky's luminance in every direction from the camera.</param>
/// <param name="Transmittance">How much sunlight survives to a point at some altitude and angle.</param>
/// <param name="Irradiance">The sky's light projected onto spherical harmonics.</param>
/// <param name="Sampler">The sampler all three are read through.</param>
internal readonly record struct SkyTables(
    ImageView SkyView,
    ImageView Transmittance,
    ImageView Irradiance,
    Sampler Sampler);

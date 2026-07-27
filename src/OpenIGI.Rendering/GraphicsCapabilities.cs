namespace OpenIGI.Rendering;

/// <summary>
/// What a device can do, as reported by the original's <c>GetGFXCaps</c> render-mode method.
/// </summary>
/// <param name="TextureUnits">The number of simultaneously bindable texture units. The original calls these TMUs and needs at least two for lightmapping.</param>
/// <param name="MaxTextureSize">The largest supported texture dimension in pixels.</param>
/// <param name="SupportsNonPowerOfTwoTextures">Whether textures whose dimensions are not powers of two can be created without padding.</param>
/// <param name="Renderer">A human-readable backend and adapter description, for logs and the options screen.</param>
public readonly record struct GraphicsCapabilities(
    int TextureUnits,
    int MaxTextureSize,
    bool SupportsNonPowerOfTwoTextures,
    string Renderer);

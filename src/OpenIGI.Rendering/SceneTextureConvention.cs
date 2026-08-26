namespace OpenIGI.Rendering;

/// <summary>How a backend's scene depth and normal textures encode what they hold.</summary>
/// <remarks>
/// The two backends rasterise differently and their G-buffers follow: reading one convention as the
/// other reconstructs every pixel at the near plane with its normal pointing somewhere else, so any
/// pass that consumes <see cref="IGraphicsDevice.SceneDepth"/> or <see cref="IGraphicsDevice.SceneNormal"/>
/// must branch on this rather than assume.
/// </remarks>
public enum SceneTextureConvention
{
    /// <summary>Standard depth with the sky at one, view-space normals, no vertical flip.</summary>
    OpenGl = 0,

    /// <summary>Reverse depth with the sky at zero, world-space normals, and a vertical flip.</summary>
    Vulkan = 1,
}

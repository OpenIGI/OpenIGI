namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>Which stage of the pipeline a shader source is for.</summary>
/// <remarks>
/// The backend's own enum rather than shaderc's, so the rest of the code names a stage without a
/// dependency on the compiler library. It is mapped to the compiler's shader kind and to the Vulkan
/// stage flags at the two points that need each.
/// </remarks>
internal enum ShaderStage
{
    /// <summary>The vertex stage.</summary>
    Vertex,

    /// <summary>The fragment stage.</summary>
    Fragment,

    /// <summary>The compute stage.</summary>
    Compute,
}

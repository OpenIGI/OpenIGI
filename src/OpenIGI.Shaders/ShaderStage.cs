namespace OpenIGI.Shaders;

/// <summary>Which stage of the pipeline an entry point is for.</summary>
/// <remarks>
/// Named here rather than in a backend so that both the Vulkan and the WebGPU implementations, and
/// the pass code above them, describe a stage the same way and without a dependency on either API.
/// </remarks>
public enum ShaderStage
{
    /// <summary>The vertex stage.</summary>
    Vertex,

    /// <summary>The fragment stage.</summary>
    Fragment,

    /// <summary>The compute stage.</summary>
    Compute,
}

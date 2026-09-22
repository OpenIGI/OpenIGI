namespace OpenIGI.Shaders;

/// <summary>
/// The shader modules the library carries. One value per <c>.slang</c> source under
/// <c>Source/</c>, whatever stages that source declares.
/// </summary>
/// <remarks>
/// <para>
/// A hand-kept enumeration rather than a generated one, so a module is named in code the way any
/// other constant is and a typo is a compile error. <c>ShaderManifestTests</c> pins it to the
/// manifest and the manifest to the directory, so the three cannot drift apart: adding a
/// <c>.slang</c> file without listing it, or listing one without a value here, fails the build.
/// </para>
/// <para>
/// A module is one source with all of its entry points, not one stage. That is how Slang compiles
/// and how WebGPU binds — a shader module holds every entry point and a pipeline names the one it
/// wants — and the Vulkan backend does the same rather than carry a second convention.
/// </para>
/// </remarks>
public enum ShaderModuleId
{
    /// <summary>The screen-space shader every 2D draw funnels through.</summary>
    Sprite2D,

    /// <summary>Distance-field text: crisp glyph edges at any size, with an outline and a glow.</summary>
    SdfText,

    /// <summary>The loading backdrop: a held frame of the level, defocused and graded.</summary>
    Loading,

    /// <summary>The world pipeline's forward shader: the original's direct-path look.</summary>
    Mesh3D,

    /// <summary>The sun shadow pass: depth only, cutting the holes the G-buffer cuts.</summary>
    Shadow,

    /// <summary>Captures a patch of a model into a page of the card atlas, as material rather than picture.</summary>
    Card,

    /// <summary>The geometry pass: albedo, world normal, material and baked light, all linear.</summary>
    GBuffer,
}

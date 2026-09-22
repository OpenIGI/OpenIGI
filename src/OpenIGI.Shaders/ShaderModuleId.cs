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

    /// <summary>The glazing transmission draw: what a pane lets through.</summary>
    Glass,

    /// <summary>The glazing front-layer draw: which pane is nearest, and which way it faces.</summary>
    GlassLayer,

    /// <summary>The depth-only box an asynchronous visibility query draws.</summary>
    Occlusion,

    /// <summary>The selection outline: a shell pushed out along the silhouette in screen space.</summary>
    Outline,

    /// <summary>Bloom: halve the resolution with the four-tap box that also does the first blur.</summary>
    BloomDownsample,

    /// <summary>Bloom: one axis of the separable gaussian.</summary>
    BloomBlur,

    /// <summary>Bloom: sum the pyramid with the reference's per-stage tints.</summary>
    BloomCombine,

    /// <summary>Temporal resolve: spread the shading rejection to a rejecting pixel's neighbours.</summary>
    UpscaleRejectionDilate,

    /// <summary>Temporal resolve: Mitchell-Netravali downsample of the history into the frame.</summary>
    UpscaleResolveHistory,

    /// <summary>Averages the traced shadow mask over a bounded window of frames.</summary>
    ShadowAccumulate,

    /// <summary>Temporal resolve: the sub-pixel offset that puts a pixel on the right side of an edge.</summary>
    UpscaleSpatialAntiAliasing,

    /// <summary>Temporal resolve: find geometry too fine to resolve, and relax the parallax verdict over it.</summary>
    UpscaleThinGeometry,

    /// <summary>Temporal resolve: how much history survives at each pixel -- the parallax verdict.</summary>
    UpscaleDecimateHistory,

    /// <summary>Motion blur: one velocity per pixel, and each tile reduced to its fastest and slowest.</summary>
    MotionBlurFlatten,

    /// <summary>Motion blur: spread each tile's fastest velocity over the tiles its swept shape covers.</summary>
    MotionBlurDilate,

    /// <summary>Motion blur: gather along the tile's velocity, weighted by depth and reach.</summary>
    MotionBlurApply,

    /// <summary>Volumetric fog: what the medium is at each froxel, and the light it sends at the eye.</summary>
    FogScatter,

    /// <summary>Volumetric fog: each column accumulated front to back into what a pixel sees.</summary>
    FogIntegrate,
}

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

    /// <summary>Temporal resolve: each pixel's motion taken from the closest surface around it.</summary>
    UpscaleDilateVelocity,

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

    /// <summary>temporal upscaler: how much of the reprojected history this frame's shading contradicts.</summary>
    UpscaleRejectShading,

    /// <summary>temporal upscaler: this frame accumulated into the super-sampled history.</summary>
    UpscaleUpdateHistory,

    /// <summary>Lamp shadows: the traced visibility reprojected and accumulated over time.</summary>
    LampDenoiseTemporal,

    /// <summary>Lamp shadows: a variance-guided filter over the accumulated visibility.</summary>
    LampDenoiseSpatial,

    /// <summary>Lamp shadows: one traced ray per pixel, at the lamp a reservoir kept.</summary>
    LampShadow,

    /// <summary>The screen motion each pixel's depth reprojects to.</summary>
    Velocity,

    /// <summary>The screen motion of meshes that move on their own, rasterised over the camera's.</summary>
    ObjectVelocity,

    /// <summary>Screen-space ambient occlusion, gathered over a hemisphere and accumulated.</summary>
    Ssao,

    /// <summary>The traced sun shadow, spread over the penumbra its occluder distance implies.</summary>
    ShadowDenoise,

    /// <summary>The frame's luminance counted into sixty-four logarithmic buckets.</summary>
    ExposureHistogram,

    /// <summary>The sun's traced visibility and the distance to whatever stopped the ray.</summary>
    SunShadow,

    /// <summary>Atmosphere: how much light survives between an altitude and space.</summary>
    SkyTransmittance,

    /// <summary>Atmosphere: the light that arrives having bounced more than once.</summary>
    SkyMultiScatter,

    /// <summary>Atmosphere: the sky's luminance in every direction from the camera.</summary>
    SkyView,

    /// <summary>Atmosphere: the sky projected onto nine spherical-harmonic coefficients.</summary>
    SkyIrradiance,

    /// <summary>Glazing: what every window pane reflects, traced or marched.</summary>
    GlazingReflect,

    /// <summary>Reflections: what each surface reflects, traced or marched.</summary>
    Reflect,

    /// <summary>Reflections: the reflection laid over the lit scene by blending, for either backend.</summary>
    ReflectCompositeDraw,

    /// <summary>Deferred: the lighting subpass, which shades the G-buffer into the scene's colour.</summary>
    Shading,

    /// <summary>Deferred: the lit scene brought to the display, with everything the lens does.</summary>
    Resolve,

    /// <summary>Distance field: one clipmap level read back for the validation.</summary>
    FieldClipmapRead,

    /// <summary>Distance field: the per-mesh fields resampled into one clipmap level.</summary>
    FieldClipmapBuild,

    /// <summary>Surface cache: the accumulated screen probes smoothed across their neighbours.</summary>
    CardsScreenFilter,

    /// <summary>Surface cache: the sparse screen probes upsampled and accumulated over time.</summary>
    CardsScreenResolve,

    /// <summary>Surface cache: the world-space radiance cache, one slab of probes a frame.</summary>
    CardsRadianceCache,

    /// <summary>Surface cache: one screen probe every few pixels, gathered over its hemisphere.</summary>
    CardsScreenGather,

    /// <summary>Surface cache: the screen gather pooled across its workgroup and accumulated.</summary>
    CardsScreenGatherTemporal,

    /// <summary>Surface cache: what a captured card receives, bounces, and finally sends onward.</summary>
    CardsLighting,

    /// <summary>Deferred: this frame's depth copied into a colour target, for the next frame to read.</summary>
    CopyDepth,

    /// <summary>Temporal resolve: the closest-occluder scatter emptied before the dilation builds it.</summary>
    UpscaleClearOccluder,
}

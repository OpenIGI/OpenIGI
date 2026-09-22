namespace OpenIGI.Rendering.Pipeline.Targets;

/// <summary>One of the images a frame is built out of.</summary>
/// <remarks>
/// Named rather than held in a field each, because there are dozens of them and every one used to
/// cost an image, a memory allocation, a view, a property pair and a line in four different methods.
/// What a target <em>is</em> — its format, its size and what it may be used for — is a fact about the
/// renderer, so it is written down once as data and everything else falls out of it.
/// </remarks>
public enum SceneTargetId
{
    /// <summary>The G-buffer's albedo, in sRGB because that is what the textures are authored in.</summary>
    BaseColour,

    /// <summary>The G-buffer's normal, packed into ten bits a channel.</summary>
    Normal,

    /// <summary>The G-buffer's material parameters.</summary>
    Material,

    /// <summary>
    /// The G-buffer's baked light, which the lighting reads as irradiance rather than as a colour.
    /// </summary>
    Baked,

    /// <summary>The scene's depth, reverse-Z.</summary>
    Depth,

    /// <summary>The lit scene, before the resolve.</summary>
    Lit,

    /// <summary>Ambient occlusion, at half the frame in each axis.</summary>
    /// <remarks>
    /// Half because the radius in use makes occlusion a low-frequency field and the lighting's bilinear
    /// read upsamples it.
    /// </remarks>
    Occlusion,

    /// <summary>Screen motion, signed and floating point.</summary>
    /// <remarks>
    /// A small signed offset that spends most of its life very near zero, which is where a float has
    /// precision to spare.
    /// </remarks>
    Velocity,

    /// <summary>The motion field after dilation, which is what the resolve reprojects with.</summary>
    DilatedVelocity,

    /// <summary>The nearest depth of each neighbourhood, for the dilation.</summary>
    ClosestDepth,

    /// <summary>Which neighbour the nearest depth came from, packed.</summary>
    ClosestOccluder,

    /// <summary>The temporal resolve's output, at the size of the frame.</summary>
    ResolveOutput,

    /// <summary>The resolve's history colour, at twice the frame in each axis.</summary>
    /// <remarks>
    /// The scale is the history's super-sampling factor over the <b>rendered</b> frame. Sizing it from
    /// the presented output instead — finer still under an upscale — was measured here and rejected:
    /// with a kernel that only reaches half an input pixel, most of a finer history's texels see no
    /// sample on a given frame and lean on reprojected or bilinear colour, which softened every walk by
    /// a fifth.
    /// </remarks>
    ResolveHistoryColour,

    /// <summary>The resolve's accumulated sample count, at the same scale as the history colour.</summary>
    /// <remarks>
    /// Kept apart from the colour rather than packed into its alpha, and at a different precision: the
    /// count only ever needs eight bits.
    /// </remarks>
    ResolveHistoryMetadata,

    /// <summary>How far the reprojection missed, which is what says the history cannot be trusted.</summary>
    HistoryRejection,

    /// <summary>How far this frame's shading disagrees with the history's.</summary>
    ShadingRejection,

    /// <summary>
    /// The minimum of the median of the above, computed once rather than four times over.
    /// </summary>
    /// <remarks>
    /// The history update runs at twice this resolution in each axis, so a neighbourhood it needed would
    /// be gathered four times for every one of these texels.
    /// </remarks>
    RejectionBlend,

    /// <summary>The shading the rejection is judged against, kept from frame to frame.</summary>
    Guide,

    /// <summary>
    /// The relaxed history rejection and the raw line strength, in one image of two channels.
    /// </summary>
    /// <remarks>
    /// One image because the two are produced together and consumed one pass apart: the update reads the
    /// relaxed rejection, and the shading rejection reads the line strength to widen its box.
    /// </remarks>
    ThinRelaxedRejection,

    /// <summary>Where the spatial filter found an edge, as an offset.</summary>
    SpatialAntiAliasing,

    /// <summary>The depth of the frame before this one, which every reprojection needs.</summary>
    /// <remarks>
    /// Its own target rather than the depth attachment, which the geometry pass overwrites before
    /// anything could read it back.
    /// </remarks>
    PreviousDepth,
}

/// <summary>How a target's size follows the frame's.</summary>
public enum SceneTargetScale
{
    /// <summary>The size of the frame.</summary>
    Full,

    /// <summary>Half the frame in each axis, rounded up.</summary>
    Half,

    /// <summary>Twice the frame in each axis.</summary>
    Double,
}

/// <summary>Whether a target is one image or a pair swapped each frame.</summary>
public enum SceneTargetHistory
{
    /// <summary>One image, written and read within a frame.</summary>
    None,

    /// <summary>
    /// A pair: the frame writes one and reads the other, and they change places next frame.
    /// </summary>
    /// <remarks>
    /// What a temporal filter is: this frame's answer is built from the last one's. One image cannot do
    /// it — a pass reading and writing the same texels sees its own output for half of them, and which
    /// half depends on the order the hardware happened to run the workgroups in.
    /// </remarks>
    PingPong,
}

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The individual passes a checkpoint can name, inside the stages the profiler brackets.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenIGI.Rendering.RenderStage"/> is deliberately coarse -- one entry per budgeted stage --
/// and two different bodies of work open the same <c>TemporalResolve</c> range: the velocity pass and the
/// temporal resolve proper. That is the right granularity for a budget and the wrong one for a fault: a
/// lost device reported "start of TemporalResolve" and left both halves, a dozen dispatches and a raster
/// pass between them, equally suspect.
/// </para>
/// <para>
/// These markers are therefore their own namespace rather than more stages. Nothing is timed by them and
/// no perf-log column moves; they exist so the checkpoint read back from a lost queue names the pass the
/// GPU was actually in.
/// </para>
/// </remarks>
internal enum VulkanPassMarker
{
    /// <summary>The camera-derived screen motion, from depth and the two frames' cameras.</summary>
    CameraVelocity,

    /// <summary>The raster pass that stamps each moving mesh's own motion over the camera's.</summary>
    ObjectVelocity,

    /// <summary>The clear of the closest-occluder scatter.</summary>
    OccluderClear,

    /// <summary>The velocity dilation and closest-depth scatter.</summary>
    DilateVelocity,

    /// <summary>The verdict on the history the update is about to read.</summary>
    DecimateHistory,

    /// <summary>The sub-pixel thin-geometry search.</summary>
    ThinGeometry,

    /// <summary>What this frame's shading makes of the reprojected history.</summary>
    RejectShading,

    /// <summary>The pessimistic filter of that verdict.</summary>
    RejectionDilate,

    /// <summary>The spatial anti-aliaser that feeds the history update.</summary>
    SpatialAntiAliasing,

    /// <summary>The history accumulation itself.</summary>
    UpdateHistory,

    /// <summary>The resolve of the accumulated history to the frame image.</summary>
    ResolveHistory,
}

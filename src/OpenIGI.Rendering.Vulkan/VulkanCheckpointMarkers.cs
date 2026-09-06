using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The markers the backend drops along its command buffers so a lost device can say where it was.
/// </summary>
/// <remarks>
/// <para>
/// <c>VK_EXT_device_fault</c> names an address; it does not name a pass. On a machine the author is not
/// sitting at, that is the difference between a report that says <em>a write to page zero</em> and one
/// that says <em>a write to page zero during the lighting stage</em>. <c>VK_NV_device_diagnostic_checkpoints</c>
/// closes that gap: every stage boundary and every one-off upload batch records a marker, and when the
/// device is lost the queue is asked for the last markers it passed. The reference's Vulkan RHI uses the same
/// extension for the same purpose when GPU crash debugging is on.
/// </para>
/// <para>
/// A marker is an opaque pointer to the driver, so these are small integers cast to one -- nothing is
/// dereferenced, and nothing has to stay alive for the marker to be read back.
/// </para>
/// </remarks>
internal static class VulkanCheckpointMarkers
{
    private const int StageBeginBase = 1;
    private const int StageEndBase = 101;
    private const int PassBase = 301;

    /// <summary>A stage ended whose identity the profiler did not have.</summary>
    public const nint StageEndUnknown = 200;

    /// <summary>The frame command buffer has started recording.</summary>
    public const nint FrameBegin = 201;

    /// <summary>A one-off command buffer (uploads, captures, builds) has started recording.</summary>
    public const nint OneOffBatch = 202;

    /// <summary>The marker recorded as <paramref name="stage"/> begins.</summary>
    public static nint StageBegin(RenderStage stage) => StageBeginBase + (int)stage;

    /// <summary>The marker recorded as <paramref name="stage"/> ends.</summary>
    public static nint StageEnd(RenderStage stage) => StageEndBase + (int)stage;

    /// <summary>The marker recorded as <paramref name="pass"/> is about to be submitted.</summary>
    /// <remarks>
    /// Finer than a stage on purpose. Two bodies of work open the same <c>TemporalResolve</c> range -- the
    /// velocity pass and the temporal resolve proper -- so a stage marker cannot say which of them the GPU
    /// stopped in, nor which of the dozen dispatches between them. A pass marker can.
    /// </remarks>
    public static nint Pass(VulkanPassMarker pass) => PassBase + (int)pass;

    /// <summary>Names a marker read back from the queue.</summary>
    public static string Describe(nint marker)
    {
        int value = (int)marker;
        return value switch
        {
            (int)FrameBegin => "frame begin",
            (int)OneOffBatch => "one-off batch (upload, capture or build)",
            (int)StageEndUnknown => "end of an untimed stage",
            >= PassBase => $"pass {(VulkanPassMarker)(value - PassBase)}",
            >= StageEndBase and < StageEndBase + 99 => $"end of {(RenderStage)(value - StageEndBase)}",
            >= StageBeginBase and < StageEndBase => $"start of {(RenderStage)(value - StageBeginBase)}",
            _ => $"unknown marker {value}",
        };
    }
}

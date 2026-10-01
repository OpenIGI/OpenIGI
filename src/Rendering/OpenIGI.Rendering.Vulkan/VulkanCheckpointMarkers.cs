namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The markers the backend drops along its command buffers so a lost device can say where it was.
/// </summary>
/// <remarks>
/// <para>
/// <c>VK_EXT_device_fault</c> names an address; it does not name a pass. On a machine the author is not
/// sitting at, that is the difference between a report that says <em>a write to page zero</em> and one
/// that says <em>a write to page zero during the lighting</em>. <c>VK_NV_device_diagnostic_checkpoints</c>
/// closes that gap: every pass and every one-off upload batch records a marker, and when the device is
/// lost the queue is asked for the last markers it passed. The reference's Vulkan RHI uses the same
/// extension for the same purpose when GPU crash debugging is on.
/// </para>
/// <para>
/// A marker is an opaque pointer to the driver, so these are small integers cast to one -- nothing is
/// dereferenced, and nothing has to stay alive for the marker to be read back.
/// </para>
/// </remarks>
internal static class VulkanCheckpointMarkers
{
    /// <summary>A one-off command buffer (uploads, captures, builds) has started recording.</summary>
    public const nint OneOffBatch = 1;

    private const int LabelBase = 1001;

    // Every pass label a marker has been made for, in the order they were first seen: a label's marker
    // is its index here, so a marker read back from a lost device names the pass without anything having
    // been kept alive for it. A frame's passes are a fixed few dozen names, so this stays small.
    private static readonly Dictionary<string, int> LabelIndices = new(StringComparer.Ordinal);
    private static readonly List<string> Labels = [];
    private static readonly Lock LabelsLock = new();

    /// <summary>The marker recorded as a pass begins, named by its label.</summary>
    /// <param name="label">The pass's label; an empty one is named as such.</param>
    /// <returns>The marker, the same one every time for the same label.</returns>
    public static nint Label(string label)
    {
        string name = string.IsNullOrEmpty(label) ? "(unlabelled)" : label;

        lock (LabelsLock)
        {
            if (!LabelIndices.TryGetValue(name, out int index))
            {
                index = Labels.Count;
                Labels.Add(name);
                LabelIndices[name] = index;
            }

            return LabelBase + index;
        }
    }

    /// <summary>Names a marker read back from the queue.</summary>
    /// <param name="marker">The marker.</param>
    /// <returns>What it marked.</returns>
    public static string Describe(nint marker)
    {
        int value = (int)marker;

        if (value == OneOffBatch)
        {
            return "one-off batch (upload, capture or build)";
        }

        if (value >= LabelBase)
        {
            lock (LabelsLock)
            {
                int index = value - LabelBase;
                return index < Labels.Count ? $"pass '{Labels[index]}'" : $"unknown pass marker {value}";
            }
        }

        return $"unknown marker {value}";
    }
}

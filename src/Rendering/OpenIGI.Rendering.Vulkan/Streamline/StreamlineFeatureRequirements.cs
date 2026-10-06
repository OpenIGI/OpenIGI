using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>What one Streamline feature needs of the machine and of the Vulkan device.</summary>
/// <remarks>
/// A managed copy rather than the native structure, because the arrays Streamline returns are its
/// own memory and the device builder reads them long after the call that produced them. Copying at
/// the boundary is a handful of small strings once at start-up.
/// </remarks>
/// <param name="Flags">Which APIs the feature supports and what it needs turned on.</param>
/// <param name="InstanceExtensions">Instance extensions the feature needs enabled.</param>
/// <param name="DeviceExtensions">Device extensions the feature needs enabled.</param>
/// <param name="Features12">
/// <c>VkPhysicalDeviceVulkan12Features</c> field names the feature needs set.
/// </param>
/// <param name="Features13">
/// <c>VkPhysicalDeviceVulkan13Features</c> field names the feature needs set.
/// </param>
/// <param name="ComputeQueues">Extra compute queues the feature wants created.</param>
/// <param name="GraphicsQueues">Extra graphics queues the feature wants created.</param>
/// <param name="OpticalFlowQueues">Extra optical-flow queues the feature wants created.</param>
/// <param name="DriverDetected">The driver version found on this machine.</param>
/// <param name="DriverRequired">The driver version the feature needs.</param>
internal sealed record StreamlineFeatureRequirements(
    SlFeatureRequirementFlags Flags,
    IReadOnlyList<string> InstanceExtensions,
    IReadOnlyList<string> DeviceExtensions,
    IReadOnlyList<string> Features12,
    IReadOnlyList<string> Features13,
    uint ComputeQueues,
    uint GraphicsQueues,
    uint OpticalFlowQueues,
    SlVersion DriverDetected,
    SlVersion DriverRequired)
{
    /// <summary>Gets a value indicating whether the feature runs on Vulkan at all.</summary>
    public bool SupportsVulkan => Flags.HasFlag(SlFeatureRequirementFlags.VulkanSupported);

    /// <summary>Gets a value indicating whether the driver on this machine is new enough.</summary>
    /// <remarks>
    /// Streamline reports zero for both when it has not looked, which reads as new enough. That is
    /// the right answer: the feature's own support check is the authority, and this is only here so
    /// the log can say <em>why</em> a machine was turned down.
    /// </remarks>
    public bool DriverIsNewEnough => Compare(DriverDetected, DriverRequired) >= 0;

    private static int Compare(SlVersion left, SlVersion right)
    {
        int major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Build.CompareTo(right.Build);
    }

    /// <summary>Copies a native requirements structure into managed memory.</summary>
    /// <param name="native">The structure Streamline filled.</param>
    /// <returns>The copy.</returns>
    public static unsafe StreamlineFeatureRequirements From(in SlFeatureRequirements native) =>
        new(
            native.Flags,
            ReadStrings(native.VkInstanceExtensions, native.VkNumInstanceExtensions),
            ReadStrings(native.VkDeviceExtensions, native.VkNumDeviceExtensions),
            ReadStrings(native.VkFeatures12, native.VkNumFeatures12),
            ReadStrings(native.VkFeatures13, native.VkNumFeatures13),
            native.VkNumComputeQueuesRequired,
            native.VkNumGraphicsQueuesRequired,
            native.VkNumOpticalFlowQueuesRequired,
            native.DriverVersionDetected,
            native.DriverVersionRequired);

    private static unsafe string[] ReadStrings(nint array, uint count)
    {
        if (array == 0 || count == 0)
        {
            return [];
        }

        byte** entries = (byte**)array;
        List<string> values = new((int)count);
        for (uint i = 0; i < count; i++)
        {
            string? value = Marshal.PtrToStringUTF8((nint)entries[i]);
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        return [.. values];
    }
}

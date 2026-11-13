using System.Text;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>Opens and closes the named regions a profiler shows a command buffer's passes under.</summary>
/// <remarks>Both do nothing without the labelling entry points, which exist only where pass markers were asked for.</remarks>
internal static unsafe class VulkanPassLabels
{
    /// <summary>Opens a region.</summary>
    /// <param name="labels">The labelling entry points, or <see langword="null"/>.</param>
    /// <param name="command">The command buffer.</param>
    /// <param name="label">The pass's label.</param>
    public static void Begin(ExtDebugUtils? labels, CommandBuffer command, string? label)
    {
        if (labels is null)
        {
            return;
        }

        byte[] name = Encoding.UTF8.GetBytes(RhiPassMarkers.NameOf(label) + '\0');
        fixed (byte* text = name)
        {
            DebugUtilsLabelEXT info = new() { SType = StructureType.DebugUtilsLabelExt, PLabelName = text };
            labels.CmdBeginDebugUtilsLabel(command, &info);
        }
    }

    /// <summary>Closes the region opened last.</summary>
    /// <param name="labels">The labelling entry points, or <see langword="null"/>.</param>
    /// <param name="command">The command buffer.</param>
    public static void End(ExtDebugUtils? labels, CommandBuffer command) => labels?.CmdEndDebugUtilsLabel(command);
}

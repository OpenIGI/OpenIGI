using OpenIGI.Rendering.Rhi;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>The timestamps a pass asks for, written the way Vulkan needs them written.</summary>
/// <remarks>
/// <para>
/// WebGPU writes a pass's two timestamps as part of the pass and lets a query be written again without
/// ceremony. Vulkan writes them with commands around the pass, and a query must be reset before every
/// write: written twice without one, what it holds is undefined.
/// </para>
/// <para>
/// Both halves matter. A pass whose end was never written left a query no result could ever arrive in,
/// and the resolve that waited for it waited for ever -- the GPU hung and the game froze on a white
/// window whenever the stage timings were on.
/// </para>
/// </remarks>
internal static class VulkanRhiTimestamps
{
    /// <summary>Writes the time a pass begins, if it asked for one.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="command">The command buffer, outside any rendering.</param>
    /// <param name="writes">What the pass asked for.</param>
    public static void Begin(Vk vk, CommandBuffer command, RhiTimestampWrites? writes)
    {
        if (writes is { } w && w.BeginIndex >= 0)
        {
            Write(vk, command, (VulkanRhiQuerySet)w.QuerySet, w.BeginIndex, PipelineStageFlags.TopOfPipeBit);
        }
    }

    /// <summary>Writes the time a pass ends, if it asked for one.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="command">The command buffer, outside any rendering.</param>
    /// <param name="writes">What the pass asked for.</param>
    public static void End(Vk vk, CommandBuffer command, RhiTimestampWrites? writes)
    {
        if (writes is { } w && w.EndIndex >= 0)
        {
            Write(vk, command, (VulkanRhiQuerySet)w.QuerySet, w.EndIndex, PipelineStageFlags.BottomOfPipeBit);
        }
    }

    private static void Write(Vk vk, CommandBuffer command, VulkanRhiQuerySet set, int index, PipelineStageFlags stage)
    {
        vk.CmdResetQueryPool(command, set.Handle, (uint)index, 1);
        vk.CmdWriteTimestamp(command, stage, set.Handle, (uint)index);
    }
}

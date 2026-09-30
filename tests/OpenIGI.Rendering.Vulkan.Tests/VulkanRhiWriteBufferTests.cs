using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Buffer writes on Vulkan keep WebGPU's promise: each lands before the work submitted after it, and after
/// the work submitted before it, although none of them is waited for.
/// </summary>
public sealed class VulkanRhiWriteBufferTests(ITestOutputHelper output)
{
    private const int Values = 4;

    [VulkanFact]
    public void Each_write_is_seen_by_the_work_submitted_after_it_and_not_by_the_work_before()
    {
        using HeadlessVulkan vulkan = Open(output);
        VulkanRhiDevice device = vulkan.Rhi;

        using IRhiBuffer source = device.CreateBuffer(new RhiBufferDescriptor(
            Values * sizeof(uint), RhiBufferUsage.Storage | RhiBufferUsage.CopySource | RhiBufferUsage.CopyDestination, "source"));
        using IRhiBuffer first = Readback(device, "first");
        using IRhiBuffer second = Readback(device, "second");

        // Written, copied out, written again, copied out again -- all before anything is waited for.
        device.WriteBuffer<uint>(source, 0, [1, 2, 3, 4]);
        Copy(device, source, first);
        device.WriteBuffer<uint>(source, 0, [5, 6, 7, 8]);
        Copy(device, source, second);

        Assert.Equal([1u, 2, 3, 4], Read(device, first));
        Assert.Equal([5u, 6, 7, 8], Read(device, second));
    }

    [VulkanFact]
    public void Many_small_writes_to_one_buffer_all_arrive()
    {
        using HeadlessVulkan vulkan = Open(output);
        VulkanRhiDevice device = vulkan.Rhi;

        const int Count = 4096;
        using IRhiBuffer source = device.CreateBuffer(new RhiBufferDescriptor(
            Count * sizeof(uint), RhiBufferUsage.Storage | RhiBufferUsage.CopySource | RhiBufferUsage.CopyDestination, "source"));
        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            Count * sizeof(uint), RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, "readback"));

        for (uint i = 0; i < Count; i++)
        {
            device.WriteBuffer<uint>(source, i * sizeof(uint), [i * 3]);
        }

        Copy(device, source, readback, Count);

        uint[] values = Read(device, readback, Count);
        for (uint i = 0; i < Count; i++)
        {
            Assert.Equal(i * 3, values[i]);
        }
    }

    private static IRhiBuffer Readback(VulkanRhiDevice device, string label) => device.CreateBuffer(new RhiBufferDescriptor(
        Values * sizeof(uint), RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, label));

    private static void Copy(VulkanRhiDevice device, IRhiBuffer source, IRhiBuffer destination, int values = Values)
    {
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("copy");
        encoder.CopyBufferToBuffer(source, 0, destination, 0, (ulong)(values * sizeof(uint)));
        device.Submit([encoder.Finish("copy")]);
    }

    private static uint[] Read(VulkanRhiDevice device, IRhiBuffer buffer, int values = Values)
    {
        using IRhiReadback data = device.ReadBuffer(buffer, 0, (ulong)(values * sizeof(uint)));
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The readback never finished.");
        return MemoryMarshal.Cast<byte, uint>(data.GetData()).ToArray();
    }

    private static HeadlessVulkan Open(ITestOutputHelper output)
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);
        output.WriteLine($"adapter {vulkan!.AdapterName}");
        return vulkan;
    }
}

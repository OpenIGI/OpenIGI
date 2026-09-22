using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Bindless;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Queues;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// Traces a known set of rays at a known quad and checks where they land, proving the whole hardware
/// path works before anything depends on it.
/// </summary>
/// <remarks>
/// <para>
/// The ray-tracing foundation has a lot of parts that only fail together — a device address taken
/// wrongly, a build flag the driver quietly ignores, a descriptor written at the wrong stride, a barrier
/// missing between the build and the traversal — and every one of them shows up downstream as
/// indirect lighting that is subtly wrong rather than as an error. A test with an answer known in
/// advance turns all of that into a pass or a fail at start-up.
/// </para>
/// <para>
/// The geometry is a unit quad in the plane <c>z&#160;=&#160;0</c>, and the rays are fired at it from one
/// unit away along a line that runs off both its edges. Every ray that starts within the quad must
/// report a hit at exactly one unit, and every ray outside it must report nothing — so the test checks
/// the traversal's geometry, not merely that a dispatch ran.
/// </para>
/// <para>
/// It exercises the whole stack on the way: a buffer's device address, a bottom-level build, an instance
/// transform, a top-level build, the barrier between them, an acceleration structure reached through the
/// bindless heap, a storage buffer reached through a raw pointer in a push constant, and a timeline
/// semaphore to know when it all finished.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRayQuerySelfTest
{
    private const uint RayCount = 64;

    // Where along the sweep a ray is unambiguously on or off the quad. The rays are spaced far more
    // widely than this, so no sample ever lands in the excluded band; it is there so the test states what
    // it is asserting rather than relying on floating-point luck at the edges.
    private const float EdgeTolerance = 0.001f;

    // The quad sits one unit beyond the ray origins, so every hit is at exactly this distance. The shader
    // reports thousandths of a unit, which is precise enough to catch a transform that scaled or moved
    // the geometry and coarse enough not to trip on rounding.
    private const uint ExpectedHitDistance = 1000;
    private const uint DistanceTolerance = 2;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanSubmissionQueue _submission;
    private readonly VulkanBindlessDescriptors _bindless;
    private readonly VulkanRayTracingApi _api;

    /// <summary>Initialises a new instance of the <see cref="VulkanRayQuerySelfTest"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to run on.</param>
    /// <param name="allocator">The allocator to build the test's resources from.</param>
    /// <param name="commands">Where the test's command buffer comes from.</param>
    /// <param name="submission">Where the recorded work is handed over to be submitted.</param>
    /// <param name="bindless">The descriptor heaps the scene is reached through.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    public VulkanRayQuerySelfTest(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        VulkanSubmissionQueue submission,
        VulkanBindlessDescriptors bindless,
        VulkanRayTracingApi api)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _submission = submission;
        _bindless = bindless;
        _api = api;
    }

    /// <summary>The compute shader the test dispatches.</summary>
    /// <remarks>
    /// A ray query is an inline traversal inside an ordinary compute shader: no ray-tracing pipeline, no
    /// shader binding table, no separate dispatch. It is the path every indirect-lighting pass will take,
    /// which is why it is the one proved here.
    /// </remarks>
    /// <summary>Runs the test and reports what happened.</summary>
    /// <param name="logger">Where to report the outcome.</param>
    /// <returns><see langword="true"/> when every ray landed where it should have.</returns>
    public bool Run(IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return Execute(logger);
        }
        catch (GraphicsDeviceException exception)
        {
            logger.Error("Vulkan", $"The ray-query self-test could not run: {exception.Message}");
            return false;
        }
    }

    private bool Execute(IIgiLogger logger)
    {
        // A unit quad in the plane the rays cross, as two triangles.
        float[] vertices =
        [
            0f, 0f, 0f,
            1f, 0f, 0f,
            1f, 1f, 0f,
            0f, 1f, 0f,
        ];
        ushort[] indices = [0, 1, 2, 0, 2, 3];

        using VulkanBuffer vertexBuffer = CreateGeometryBuffer<float>(vertices);
        using VulkanBuffer indexBuffer = CreateGeometryBuffer<ushort>(indices);
        using VulkanBuffer results = new(
            _vk,
            _device,
            _allocator,
            RayCount * sizeof(uint),
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit,
            _allocator.UploadMemoryProperties(),
            persistentlyMapped: true);

        results.Write<uint>(new uint[RayCount]);

        using VulkanRayTracingScene scene = new(_vk, _api, _device, _allocator, instanceCapacity: 1);

        VulkanBottomLevelStructure geometry = scene.AddGeometry(
        [
            new VulkanTriangleGeometry(
                vertexBuffer.DeviceAddress,
                VertexStride: 3 * sizeof(float),
                VertexCount: 4,
                indexBuffer.DeviceAddress,
                PrimitiveCount: 2),
        ]);

        scene.SetInstances(
        [
            VulkanRayTracingInstance.Create(
                Matrix4f.Identity,
                customIndex: 0,
                VulkanRayTracingInstance.VisibleToAll,
                hitGroupOffset: 0,
                GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
                geometry.DeviceAddress),
        ]);

        using VulkanShaderModule module = new(
            _vk,
            _device.Device,
            ShaderCatalog.GetSpirv(ShaderModuleId.RayQuerySelfTest).Span,
            Vulkan.Shaders.ShaderStage.Compute,
            ShaderCatalog.GetReflection(ShaderModuleId.RayQuerySelfTest)
                .EntryPointFor(OpenIGI.Shaders.ShaderStage.Compute).Name);

        Pipeline pipeline = CreatePipeline(module);
        try
        {
            VulkanQueue queue = _device.GraphicsQueue;
            CommandBuffer commandBuffer = _commands.AllocatePrimary(1)[0];

            CommandBufferBeginInfo beginInfo = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            VkCheck.Ok(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");

            // The heaps are bound once for the whole command buffer, which is what lets the descriptor
            // written below be picked up by a dispatch recorded before it.
            _bindless.Bind(commandBuffer, PipelineBindPoint.Compute);

            scene.RecordUpdate(commandBuffer, queue);

            if (scene.TopLevel is null)
            {
                logger.Error("Vulkan", "The ray-query self-test built no scene to trace against.");
                return false;
            }

            VulkanDescriptorHandle sceneHandle =
                _bindless.CreateAccelerationStructure(scene.TopLevel.DeviceAddress);

            _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);

            PushConstants push = new()
            {
                ResultsAddress = results.DeviceAddress,
                SceneIndex = sceneHandle.Index,
                RayCount = RayCount,
            };
            _vk.CmdPushConstants(
                commandBuffer,
                _bindless.PipelineLayout,
                ShaderStageFlags.All,
                0,
                (uint)sizeof(PushConstants),
                &push);

            _vk.CmdDispatch(commandBuffer, 1, 1, 1);
            VkCheck.Ok(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

            VulkanPayload payload = new(queue);
            payload.CommandBuffers.Add(commandBuffer);

            // Handed over rather than submitted directly, so the test travels the same path a render pass
            // will. The flush is what makes the wait below meaningful: without it there would be nothing
            // to wait for yet when the submission runs on a thread of its own.
            _submission.Enqueue(payload);
            _submission.Flush();
            queue.WaitForValue(payload.TimelineValue);

            uint[] observed = new uint[RayCount];
            results.Read<uint>(observed);
            _bindless.Free(sceneHandle);

            return Verify(observed, logger);
        }
        finally
        {
            _device.WaitIdle();
            _vk.DestroyPipeline(_device.Device, pipeline, null);
        }
    }

    private static bool Verify(uint[] observed, IIgiLogger logger)
    {
        int hits = 0;
        int misses = 0;
        List<string> failures = [];

        for (uint i = 0; i < RayCount; i++)
        {
            float t = i / (float)(RayCount - 1);
            float x = -0.5f + (2.0f * t);
            uint value = observed[i];

            bool shouldHit = x > EdgeTolerance && x < 1.0f - EdgeTolerance;
            bool shouldMiss = x < -EdgeTolerance || x > 1.0f + EdgeTolerance;

            if (shouldHit)
            {
                hits++;
                uint difference = value > ExpectedHitDistance
                    ? value - ExpectedHitDistance
                    : ExpectedHitDistance - value;
                if (value == 0)
                {
                    failures.Add($"ray {i} at x={x.ToString("0.###", CultureInfo.InvariantCulture)} missed the quad");
                }
                else if (difference > DistanceTolerance)
                {
                    failures.Add($"ray {i} hit at {value} thousandths, expected {ExpectedHitDistance}");
                }
            }
            else if (shouldMiss)
            {
                misses++;
                if (value != 0)
                {
                    failures.Add(
                        $"ray {i} at x={x.ToString("0.###", CultureInfo.InvariantCulture)} hit nothing but reported {value}");
                }
            }
        }

        if (failures.Count > 0)
        {
            logger.Error(
                "Vulkan",
                $"Ray-query self-test FAILED ({failures.Count} of {RayCount} rays wrong): " +
                string.Join("; ", failures.Take(5)));
            return false;
        }

        logger.Information(
            "Vulkan",
            $"Ray query verified: {hits} rays hit the quad at {ExpectedHitDistance} thousandths and " +
            $"{misses} passed it by, traced against a hardware acceleration structure.");
        return true;
    }

    private VulkanBuffer CreateGeometryBuffer<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        VulkanBuffer buffer = new(
            _vk,
            _device,
            _allocator,
            (ulong)(data.Length * sizeof(T)),
            BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit
                | BufferUsageFlags.StorageBufferBit,
            _allocator.UploadMemoryProperties(),
            persistentlyMapped: true);
        buffer.Write(data);
        return buffer;
    }

    private Pipeline CreatePipeline(VulkanShaderModule module)
    {
        nint entryPoint = SilkMarshal.StringToPtr(module.EntryPoint);
        try
        {
            ComputePipelineCreateInfo createInfo = new()
            {
                SType = StructureType.ComputePipelineCreateInfo,

                // Without this flag the pipeline expects descriptor sets rather than the heaps, and every
                // resource it names reads as undefined.
                Flags = VulkanBindlessDescriptors.PipelineCreateFlag,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = module.StageFlags,
                    Module = module.Handle,
                    PName = (byte*)entryPoint,
                },
                Layout = _bindless.PipelineLayout,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateComputePipelines(_device.Device, _device.PipelineCache, 1, &createInfo, null, &pipeline),
                "vkCreateComputePipelines (ray-query self-test)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private struct PushConstants
    {
        public ulong ResultsAddress;
        public uint SceneIndex;
        public uint RayCount;
    }
}

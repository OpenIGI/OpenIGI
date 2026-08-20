using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// A compute pass that works out, for every pixel, where the surface under it was on the previous frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the primitive every temporal technique is built on. Accumulating anything across frames —
/// an indirect-lighting estimate gathered from too few rays, a reflection traced at quarter resolution,
/// an anti-aliased edge — means finding last frame's value for the surface now under this pixel, and
/// that surface has moved on screen because the camera moved. Without this, accumulation either smears
/// (reusing whatever was at the same pixel) or is thrown away (reusing nothing).
/// </para>
/// <para>
/// It is derived from depth rather than written by the geometry pass. For a world that does not move,
/// the only thing that changes where a surface lands on screen is the camera, so the previous position
/// follows from this frame's depth and the two frames' cameras — no per-object previous transform, no
/// second position interpolator, nothing pushed per draw. Geometry that moves under its own power needs
/// its own velocity written during the base pass, and that is a later addition; static world geometry is
/// all of this scene.
/// </para>
/// <para>
/// The arithmetic is deliberately kept camera-relative. This world's coordinates run to tens of
/// thousands of units, and subtracting two nearby absolute positions in single precision would throw
/// away most of the significant digits of a difference that is often a fraction of a unit. Working in
/// positions relative to the camera, and applying only the small camera-to-camera delta, keeps the
/// magnitudes near the values that matter.
/// </para>
/// <para>
/// The result is the offset from this pixel to where it was, in texture coordinates, so a consumer reads
/// its history at <c>uv - velocity</c>. It is stored signed and floating-point rather than in the
/// reference's biased fixed-point encoding: the values cluster tightly around zero, which is exactly
/// where a float has precision to spare and a fixed-point encoding has least.
/// </para>
/// </remarks>
internal sealed unsafe class VelocityPass : IDisposable
{
    private const int Frames = 2;

    // Two 4x4 matrices and four 4-vectors.
    private const int ParamSize = (2 * 64) + (4 * 16);

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanShaderModule _module;
    private readonly DescriptorSetLayout _setLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly Pipeline _pipeline;
    private readonly DescriptorPool _pool;
    private readonly DescriptorSet[] _sets = new DescriptorSet[Frames];
    private readonly VulkanBuffer[] _params = new VulkanBuffer[Frames];
    private readonly Sampler _depthSampler;
    private bool _disposed;

    private const string Source =
        """
        #version 450

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1, rg16f) uniform writeonly image2D uVelocity;
        layout(set = 0, binding = 2) uniform Params
        {
            mat4 uViewToWorld;       // this frame's view rotation, camera-relative
            mat4 uPrevWorldToView;   // the previous frame's inverse view rotation
            vec4 uUnproject;         // xScale, yScale, near, far
            vec4 uResolution;        // width, height, 1/width, 1/height
            vec4 uCameraDelta;       // this frame's camera minus the previous frame's, w unused
            vec4 uPrevProjection;    // xScale, yScale, near, far of the previous unjittered projection
        } p;

        // The view-space position a depth sample came from. Reverse-Z, so the stored depth is mapped back
        // to the normalised z the closed-form inverse expects; the vertical is negated because the
        // G-buffer was rasterised through the vertex stage's Y flip.
        vec3 ViewPositionAt(vec2 uv, float depth)
        {
            float ndcZ = 1.0 - 2.0 * depth;
            float near = p.uUnproject.z;
            float far = p.uUnproject.w;
            float viewZ = (2.0 * near * far) / ((far + near) - ndcZ * (far - near));
            vec2 ndc = uv * 2.0 - 1.0;
            return vec3(ndc.x * viewZ / p.uUnproject.x, -ndc.y * viewZ / p.uUnproject.y, viewZ);
        }

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            ivec2 size = ivec2(p.uResolution.xy);
            if (px.x >= size.x || px.y >= size.y)
            {
                return;
            }

            vec2 uv = (vec2(px) + 0.5) * p.uResolution.zw;
            float depth = texture(uDepth, uv).r;

            // Reverse-Z clears to zero, so a pixel the geometry never covered reads exactly zero. The sky
            // has no surface to have moved, and reporting a velocity for it would drag whatever is behind
            // it across the frame.
            if (depth <= 0.0)
            {
                imageStore(uVelocity, px, vec4(0.0));
                return;
            }

            vec3 viewPos = ViewPositionAt(uv, depth);

            // Camera-relative throughout: the world position is never formed, only the offset from this
            // frame's camera, and then the offset from the previous one.
            vec3 relativeToCamera = (p.uViewToWorld * vec4(viewPos, 0.0)).xyz;
            vec3 relativeToPrevCamera = relativeToCamera + p.uCameraDelta.xyz;
            vec3 prevView = (p.uPrevWorldToView * vec4(relativeToPrevCamera, 0.0)).xyz;

            // Behind the previous camera, so it had no screen position at all — nothing to reproject onto.
            if (prevView.z <= p.uPrevProjection.z)
            {
                imageStore(uVelocity, px, vec4(0.0));
                return;
            }

            // The projection applied by hand rather than as a matrix, from the same four numbers the
            // reconstruction above used, so the two are exact inverses of one another.
            vec2 prevNdc = vec2(
                prevView.x * p.uPrevProjection.x / prevView.z,
                -prevView.y * p.uPrevProjection.y / prevView.z);
            vec2 prevUv = prevNdc * 0.5 + 0.5;

            imageStore(uVelocity, px, vec4(uv - prevUv, 0.0, 0.0));
        }
        """;

    /// <summary>Initialises a new instance of the <see cref="VelocityPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <exception cref="GraphicsDeviceException">The pass could not be created.</exception>
    public VelocityPass(Vk vk, VulkanDevice device, VulkanAllocator allocator, ShaderCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(compiler);

        _vk = vk;
        _device = device.Device;

        _module = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(Source, ShaderStage.Compute, "velocity.comp"),
            ShaderStage.Compute);
        _setLayout = CreateSetLayout();
        _pipelineLayout = CreatePipelineLayout();
        _pipeline = CreatePipeline();
        _pool = CreatePool();
        _depthSampler = CreateDepthSampler();

        for (int i = 0; i < Frames; i++)
        {
            _sets[i] = AllocateSet();
            _params[i] = new VulkanBuffer(
                vk,
                device,
                allocator,
                ParamSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
    }

    /// <summary>Records the velocity dispatch for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index, selecting the set and parameter buffer.</param>
    /// <param name="depthView">The scene depth view to read.</param>
    /// <param name="velocityView">The velocity image to write.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's view-to-world rotation.</param>
    /// <param name="cameraDelta">This frame's camera position minus the previous frame's.</param>
    /// <param name="unprojection">This frame's projection scales and planes.</param>
    /// <param name="previousUnprojection">The previous frame's, unjittered.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView velocityView,
        int width,
        int height,
        Matrix3f viewToWorld,
        Matrix3f previousViewToWorld,
        Vector3f cameraDelta,
        DepthUnprojection unprojection,
        DepthUnprojection previousUnprojection)
    {
        // A rotation's inverse is its transpose, which is what turns a world-relative offset back into the
        // previous frame's view space without forming an inverse.
        Matrix3f previousWorldToView = previousViewToWorld.Transposed();

        Span<float> data = stackalloc float[ParamSize / sizeof(float)];
        WriteRotation(data, 0, viewToWorld);
        WriteRotation(data, 16, previousWorldToView);

        data[32] = unprojection.XScale;
        data[33] = unprojection.YScale;
        data[34] = unprojection.Near;
        data[35] = unprojection.Far;

        data[36] = width;
        data[37] = height;
        data[38] = 1f / Math.Max(1, width);
        data[39] = 1f / Math.Max(1, height);

        data[40] = cameraDelta.X;
        data[41] = cameraDelta.Y;
        data[42] = cameraDelta.Z;
        data[43] = 0f;

        data[44] = previousUnprojection.XScale;
        data[45] = previousUnprojection.YScale;
        data[46] = previousUnprojection.Near;
        data[47] = previousUnprojection.Far;

        _params[frame].Write<float>(data);
        WriteSet(frame, depthView, velocityView);

        DescriptorSet set = _sets[frame];
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _pipeline);
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _pipelineLayout, 0, 1, &set, 0, null);
        _vk.CmdDispatch(cmd, (uint)((width + 7) / 8), (uint)((height + 7) / 8), 1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (VulkanBuffer buffer in _params)
        {
            buffer?.Dispose();
        }

        _vk.DestroySampler(_device, _depthSampler, null);
        _vk.DestroyDescriptorPool(_device, _pool, null);
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
        _module.Dispose();
    }

    /// <summary>
    /// Writes a three-by-three rotation into the four-by-four a shader reads, column by column.
    /// </summary>
    /// <param name="data">The parameter block.</param>
    /// <param name="offset">Where in the block the matrix starts.</param>
    /// <param name="m">The rotation, stored row-major with vectors as columns.</param>
    private static void WriteRotation(Span<float> data, int offset, in Matrix3f m)
    {
        data[offset + 0] = m.M11; data[offset + 1] = m.M21; data[offset + 2] = m.M31; data[offset + 3] = 0f;
        data[offset + 4] = m.M12; data[offset + 5] = m.M22; data[offset + 6] = m.M32; data[offset + 7] = 0f;
        data[offset + 8] = m.M13; data[offset + 9] = m.M23; data[offset + 10] = m.M33; data[offset + 11] = 0f;
        data[offset + 12] = 0f; data[offset + 13] = 0f; data[offset + 14] = 0f; data[offset + 15] = 1f;
    }

    private void WriteSet(int frame, ImageView depthView, ImageView velocityView)
    {
        DescriptorImageInfo depthInfo = new()
        {
            Sampler = _depthSampler,
            ImageView = depthView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        DescriptorImageInfo velocityInfo = new() { ImageView = velocityView, ImageLayout = ImageLayout.General };
        DescriptorBufferInfo paramInfo = new() { Buffer = _params[frame].Handle, Offset = 0, Range = ParamSize };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[3];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _sets[frame],
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &depthInfo,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _sets[frame],
            DstBinding = 1,
            DescriptorType = DescriptorType.StorageImage,
            DescriptorCount = 1,
            PImageInfo = &velocityInfo,
        };
        writes[2] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _sets[frame],
            DstBinding = 2,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &paramInfo,
        };

        _vk.UpdateDescriptorSets(_device, 3, writes, 0, null);
    }

    private DescriptorSetLayout CreateSetLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit,
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.StorageImage,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit,
        };
        bindings[2] = new DescriptorSetLayoutBinding
        {
            Binding = 2,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (velocity)");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        DescriptorSetLayout layout = _setLayout;
        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &layout,
        };

        PipelineLayout pipelineLayout;
        VkCheck.Ok(
            _vk.CreatePipelineLayout(_device, &createInfo, null, &pipelineLayout),
            "vkCreatePipelineLayout (velocity)");
        return pipelineLayout;
    }

    private Pipeline CreatePipeline()
    {
        nint entryPoint = SilkMarshal.StringToPtr("main");
        try
        {
            ComputePipelineCreateInfo createInfo = new()
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = _module.Handle,
                    PName = (byte*)entryPoint,
                },
                Layout = _pipelineLayout,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateComputePipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateComputePipelines (velocity)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private DescriptorPool CreatePool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3];
        sizes[0] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = Frames };
        sizes[1] = new DescriptorPoolSize { Type = DescriptorType.StorageImage, DescriptorCount = Frames };
        sizes[2] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = Frames };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = Frames,
            PoolSizeCount = 3,
            PPoolSizes = sizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool (velocity)");
        return pool;
    }

    private DescriptorSet AllocateSet()
    {
        DescriptorSetLayout layout = _setLayout;
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };

        DescriptorSet set;
        VkCheck.Ok(_vk.AllocateDescriptorSets(_device, &allocInfo, &set), "vkAllocateDescriptorSets (velocity)");
        return set;
    }

    private Sampler CreateDepthSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler (velocity depth)");
        return sampler;
    }
}

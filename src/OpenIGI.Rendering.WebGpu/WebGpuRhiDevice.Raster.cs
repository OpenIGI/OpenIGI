using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>The raster half of the device: render pipelines and the passes they draw in.</summary>
public sealed unsafe partial class WebGpuRhiDevice
{
    /// <summary>Creates a graphics pipeline.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="InvalidOperationException">The pipeline did not build.</exception>
    /// <remarks>
    /// Everything a WebGPU pipeline carries is fixed at creation except the viewport, the scissor and
    /// the blend constant, which the pass sets — so one pipeline survives a resize and a pipeline per
    /// window size is a mistake this cannot make.
    /// </remarks>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using WebGpuString label = new(descriptor.Label);
        using WebGpuString vertexEntry = new(descriptor.Vertex.EntryPoint);
        using WebGpuString fragmentEntry = new(descriptor.Fragment?.EntryPoint ?? string.Empty);

        // Held until the pipeline exists: an override is keyed by a name, and the name has to still be
        // there when the driver reads it.
        using WebGpuConstants vertexConstants = new(descriptor.Vertex);
        using WebGpuConstants fragmentConstants = new(descriptor.Fragment);

        // Flattened first, because every pointer below has to stay valid until the create call returns
        // and a nested fixed block would close too early.
        int attributeCount = 0;
        foreach (RhiVertexBufferLayout buffer in descriptor.VertexBuffers)
        {
            attributeCount += buffer.Attributes.Count;
        }

        WGPUVertexAttribute[] attributes = new WGPUVertexAttribute[Math.Max(attributeCount, 1)];
        WGPUVertexBufferLayout[] buffers = new WGPUVertexBufferLayout[Math.Max(descriptor.VertexBuffers.Count, 1)];
        WGPUColorTargetState[] targets = new WGPUColorTargetState[Math.Max(descriptor.ColorTargets.Count, 1)];
        WGPUBlendState[] blends = new WGPUBlendState[targets.Length];

        for (int i = 0; i < descriptor.ColorTargets.Count; i++)
        {
            RhiColorTargetState target = descriptor.ColorTargets[i];
            targets[i] = new WGPUColorTargetState
            {
                format = WebGpuTranslation.Format(target.Format),
                writeMask = WebGpuTranslation.ColorWrite(target.Write),
            };

            if (target.Blend is { } blend)
            {
                blends[i] = new WGPUBlendState
                {
                    color = Component(blend.Color),
                    alpha = Component(blend.Alpha),
                };
            }
        }

        fixed (WGPUVertexAttribute* firstAttribute = attributes)
        fixed (WGPUVertexBufferLayout* firstBuffer = buffers)
        fixed (WGPUColorTargetState* firstTarget = targets)
        fixed (WGPUBlendState* firstBlend = blends)
        {
            int written = 0;
            for (int i = 0; i < descriptor.VertexBuffers.Count; i++)
            {
                RhiVertexBufferLayout buffer = descriptor.VertexBuffers[i];
                int start = written;

                foreach (RhiVertexElement element in buffer.Attributes)
                {
                    attributes[written++] = new WGPUVertexAttribute
                    {
                        format = WebGpuTranslation.VertexFormat(element.Format),
                        offset = (ulong)element.Offset,
                        shaderLocation = (uint)element.Location,
                    };
                }

                buffers[i] = new WGPUVertexBufferLayout
                {
                    stepMode = WebGpuTranslation.StepMode(buffer.StepMode),
                    arrayStride = (ulong)buffer.Stride,
                    attributeCount = (nuint)buffer.Attributes.Count,
                    attributes = firstAttribute + start,
                };
            }

            for (int i = 0; i < descriptor.ColorTargets.Count; i++)
            {
                if (descriptor.ColorTargets[i].Blend is not null)
                {
                    targets[i].blend = firstBlend + i;
                }
            }

            WGPUFragmentState fragment = default;
            if (descriptor.Fragment is { } stage)
            {
                fragment = new WGPUFragmentState
                {
                    module = ((WebGpuShaderModule)stage.Module).Handle,
                    entryPoint = fragmentEntry.View,
                    constantCount = fragmentConstants.Count,
                    constants = fragmentConstants.Entries,
                    targetCount = (nuint)descriptor.ColorTargets.Count,
                    targets = descriptor.ColorTargets.Count > 0 ? firstTarget : null,
                };
            }

            WGPUDepthStencilState depth = default;
            if (descriptor.DepthStencil is { } state)
            {
                depth = new WGPUDepthStencilState
                {
                    format = WebGpuTranslation.Format(state.Format),

                    // A three-valued bool, because WebGPU distinguishes "off" from "not stated" and a
                    // pipeline with a depth attachment must state it.
                    depthWriteEnabled = state.DepthWrite ? WGPUOptionalBool.True : WGPUOptionalBool.False,
                    depthCompare = WebGpuTranslation.Compare(state.DepthCompare),
                    depthBias = (int)state.DepthBiasConstant,
                    depthBiasSlopeScale = state.DepthBiasSlopeScale,

                    // Stencil is not used by anything here, and WebGPU wants the faces stated all the
                    // same; Always with Keep everywhere is the identity.
                    stencilFront = Keep(),
                    stencilBack = Keep(),
                    stencilReadMask = 0xFFFFFFFF,
                    stencilWriteMask = 0xFFFFFFFF,
                };
            }

            WGPURenderPipelineDescriptor native = new()
            {
                label = label.View,
                layout = ((WebGpuPipelineLayout)descriptor.Layout).Handle,
                vertex = new WGPUVertexState
                {
                    module = ((WebGpuShaderModule)descriptor.Vertex.Module).Handle,
                    entryPoint = vertexEntry.View,
                    constantCount = vertexConstants.Count,
                    constants = vertexConstants.Entries,
                    bufferCount = (nuint)descriptor.VertexBuffers.Count,
                    buffers = descriptor.VertexBuffers.Count > 0 ? firstBuffer : null,
                },
                primitive = new WGPUPrimitiveState
                {
                    topology = WebGpuTranslation.Topology(descriptor.Primitive.Topology),
                    stripIndexFormat = descriptor.Primitive.StripIndexFormat is { } strip
                        ? WebGpuTranslation.IndexFormat(strip)
                        : WGPUIndexFormat.Undefined,
                    frontFace = WebGpuTranslation.FrontFace(descriptor.Primitive.FrontFace),
                    cullMode = WebGpuTranslation.CullMode(descriptor.Primitive.CullMode),
                },
                multisample = new WGPUMultisampleState
                {
                    count = (uint)Math.Max(1, descriptor.SampleCount),
                    mask = 0xFFFFFFFF,
                },
                depthStencil = descriptor.DepthStencil is null ? null : &depth,
                fragment = descriptor.Fragment is null ? null : &fragment,
            };

            WGPURenderPipeline handle = Wgpu.wgpuDeviceCreateRenderPipeline(_context.Device, &native);
            return handle.IsValid
                ? new WebGpuRenderPipeline(handle, descriptor.Layout, descriptor.Label)
                : throw new InvalidOperationException($"The render pipeline '{descriptor.Label}' did not build.");
        }
    }

    private static WGPUBlendComponent Component(in RhiBlendComponent component) => new()
    {
        operation = WebGpuTranslation.BlendOperation(component.Operation),
        srcFactor = WebGpuTranslation.BlendFactor(component.Source),
        dstFactor = WebGpuTranslation.BlendFactor(component.Destination),
    };

    private static WGPUStencilFaceState Keep() => new()
    {
        compare = WGPUCompareFunction.Always,
        failOp = WGPUStencilOperation.Keep,
        depthFailOp = WGPUStencilOperation.Keep,
        passOp = WGPUStencilOperation.Keep,
    };
}

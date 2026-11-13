using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Rhi.Push;
using OpenIGI.Shaders;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Which of OpenGL's binding namespaces a unit is in.</summary>
internal enum GlUnitKind
{
    UniformBuffer,
    StorageBuffer,
    Image,
    Texture,
    Sampler,
}

/// <summary>One OpenGL unit a bind group entry is bound to.</summary>
/// <param name="Binding">The entry's binding in its group.</param>
/// <param name="Kind">Which namespace the unit is in.</param>
/// <param name="Unit">The unit.</param>
internal readonly record struct GlUnit(int Binding, GlUnitKind Kind, int Unit);

/// <summary>Where a pipeline's bind groups land: for each group, the units each of its entries is bound to.</summary>
/// <remarks>
/// Read from the module's reflection, which the shader build wrote the GLSL units into. A texture read through two
/// samplers is two texture units, so one entry may be bound to several; a sampler is bound to every unit whose pair
/// names it; a texture fetched without a sampler is a unit with the sampler object cleared.
/// </remarks>
internal sealed class GlBindingPlan
{
    private GlBindingPlan(GlUnit[][] groups, int[] unsampledUnits, bool writes)
    {
        Groups = groups;
        UnsampledUnits = unsampledUnits;
        Writes = writes;
    }

    /// <summary>Gets whether a pipeline under this plan can write memory a later command reads: a storage buffer or image not
    /// declared read-only. Only then does the next command need a barrier; a read-only one, such as the per-instance push
    /// blocks every forward mesh reads, needs none, and a barrier after each of those serialised the whole frame.</summary>
    public bool Writes { get; }

    /// <summary>Gets the units of each group, indexed by group.</summary>
    public GlUnit[][] Groups { get; }

    /// <summary>Gets the texture units read with no sampler, whose sampler object is cleared when the pipeline is bound.</summary>
    public int[] UnsampledUnits { get; }

    /// <summary>Builds the plan for a module under a layout.</summary>
    /// <param name="reflection">The module's reflection, with its GLSL units.</param>
    /// <param name="layout">The pipeline layout, which says how push data is read.</param>
    /// <returns>The plan.</returns>
    public static GlBindingPlan For(ShaderReflection reflection, GlPipelineLayout layout)
    {
        Dictionary<int, List<GlUnit>> groups = [];

        void Add(int group, GlUnit unit)
        {
            if (!groups.TryGetValue(group, out List<GlUnit>? list))
            {
                groups[group] = list = [];
            }

            list.Add(unit);
        }

        IReadOnlyList<ShaderGlslSampler> pairs = reflection.GlslSamplers ?? [];

        foreach (ShaderBinding binding in reflection.Bindings)
        {
            switch (binding.Kind)
            {
                case ShaderBindingKind.UniformBuffer when binding.GlslUnit is int unit:
                    Add(binding.Group, new GlUnit(binding.Binding, GlUnitKind.UniformBuffer, unit));
                    break;
                case ShaderBindingKind.StorageBuffer when binding.GlslUnit is int unit:
                    Add(binding.Group, new GlUnit(binding.Binding, GlUnitKind.StorageBuffer, unit));
                    break;
                case ShaderBindingKind.StorageTexture when binding.GlslUnit is int unit:
                    Add(binding.Group, new GlUnit(binding.Binding, GlUnitKind.Image, unit));
                    break;
                case ShaderBindingKind.Texture:
                    foreach (ShaderGlslSampler pair in pairs.Where(p => p.Texture == binding.Name))
                    {
                        Add(binding.Group, new GlUnit(binding.Binding, GlUnitKind.Texture, pair.Unit));
                    }

                    break;
                case ShaderBindingKind.Sampler:
                    foreach (ShaderGlslSampler pair in pairs.Where(p => p.Sampler == binding.Name))
                    {
                        Add(binding.Group, new GlUnit(binding.Binding, GlUnitKind.Sampler, pair.Unit));
                    }

                    break;
            }
        }

        // The push block is a group of its own on this target, bound from the ring the way any other group is.
        if (layout.PushDataBytes > 0 && reflection.PushBlock is { GlslUnit: int pushUnit } push)
        {
            Add(push.Group, new GlUnit(
                push.Binding,
                layout.PushDataPerInstance ? GlUnitKind.StorageBuffer : GlUnitKind.UniformBuffer,
                pushUnit));
        }

        int count = groups.Count == 0 ? 0 : groups.Keys.Max() + 1;
        GlUnit[][] table = new GlUnit[count][];
        for (int i = 0; i < count; i++)
        {
            table[i] = groups.TryGetValue(i, out List<GlUnit>? units) ? [.. units] : [];
        }

        bool writes = reflection.Bindings.Any(binding => binding.Kind switch
        {
            ShaderBindingKind.StorageBuffer => EntryFor(layout, binding) is not { Type: RhiBindingType.ReadOnlyStorageBuffer },
            ShaderBindingKind.StorageTexture => EntryFor(layout, binding) is not { StorageAccess: RhiStorageAccess.ReadOnly },
            _ => false,
        });

        return new GlBindingPlan(table, [.. pairs.Where(p => p.Sampler is null).Select(p => p.Unit)], writes);
    }

    // The layout's entry for a binding, which says how it is accessed; none found is taken to write, to be safe.
    private static RhiBindGroupLayoutEntry? EntryFor(GlPipelineLayout layout, ShaderBinding binding)
    {
        if ((uint)binding.Group >= (uint)layout.Groups.Count || layout.Groups[binding.Group] is not GlBindGroupLayout group)
        {
            return null;
        }

        foreach (RhiBindGroupLayoutEntry entry in group.Entries)
        {
            if (entry.Binding == binding.Binding)
            {
                return entry;
            }
        }

        return null;
    }
}

/// <summary>A vertex buffer slot's stride and step.</summary>
internal readonly record struct GlVertexSlot(int Stride, bool PerInstance);

/// <summary>Fixed-function state a render pipeline sets, compared as a whole by the state cache.</summary>
internal sealed record GlRasterState(
    GLEnum Topology,
    RhiCullMode Cull,
    RhiFrontFace FrontFace,
    bool PrimitiveRestart,
    bool DepthTest,
    bool DepthWrite,
    GLEnum DepthCompare,
    float DepthBiasConstant,
    float DepthBiasSlope,
    RhiColorTargetState[] Targets,
    int SampleCount);

/// <summary>A linked program with the state and vertex layout of a render pipeline.</summary>
internal sealed class GlRenderPipeline : GlObject, IRhiRenderPipeline
{
    public GlRenderPipeline(GlRhiDevice device, RhiRenderPipelineDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        GlShaderModule module = (GlShaderModule)descriptor.Vertex.Module;
        if (descriptor.Fragment is { } fragment && !ReferenceEquals(fragment.Module, module)
            && ((GlShaderModule)fragment.Module).Module != module.Module)
        {
            throw new NotSupportedException(
                $"'{descriptor.Label}' takes its stages from two modules; OpenGL units are numbered per module.");
        }

        GlPipelineLayout layout = (GlPipelineLayout)descriptor.Layout;
        Layout = layout;
        Plan = GlBindingPlan.For(module.Reflection, layout);
        Slots = [.. descriptor.VertexBuffers.Select(b => new GlVertexSlot(b.Stride, b.StepMode == RhiVertexStepMode.Instance))];

        RhiDepthStencilState? depth = descriptor.DepthStencil;
        State = new GlRasterState(
            GlTranslation.Topology(descriptor.Primitive.Topology),
            descriptor.Primitive.CullMode,
            descriptor.Primitive.FrontFace,
            descriptor.Primitive.StripIndexFormat is not null,
            depth is { } d && (d.DepthWrite || d.DepthCompare != RhiCompare.Always),
            depth?.DepthWrite ?? false,
            GlTranslation.Compare(depth?.DepthCompare ?? RhiCompare.Always),
            depth?.DepthBiasConstant ?? 0f,
            depth?.DepthBiasSlopeScale ?? 0f,
            [.. descriptor.ColorTargets],
            descriptor.SampleCount);

        List<(ShaderStage, IReadOnlyDictionary<int, RhiConstant>?)> stages = [(ShaderStage.Vertex, descriptor.Vertex.Constants)];
        if (descriptor.Fragment is { } fragmentStage)
        {
            stages.Add((ShaderStage.Fragment, fragmentStage.Constants));
        }

        device.Thread.Run(() =>
        {
            GL gl = device.Gl;
            Program = GlProgram.Build(gl, module, stages, Label);
            device.Label(GLEnum.Program, Program.Name, Label);

            // The vertex layout lives in a vertex array of the pipeline's own; buffers are bound to it per draw.
            VertexArray = gl.GenVertexArray();
            gl.BindVertexArray(VertexArray);
            for (int slot = 0; slot < descriptor.VertexBuffers.Count; slot++)
            {
                RhiVertexBufferLayout buffer = descriptor.VertexBuffers[slot];
                foreach (RhiVertexElement element in buffer.Attributes)
                {
                    GlVertexFormat format = GlTranslation.Vertex(element.Format);
                    uint location = (uint)element.Location;
                    gl.EnableVertexAttribArray(location);
                    if (format.Integer)
                    {
                        gl.VertexAttribIFormat(location, format.Components, format.Type, (uint)element.Offset);
                    }
                    else
                    {
                        gl.VertexAttribFormat(location, format.Components, format.Type, format.Normalized, (uint)element.Offset);
                    }

                    gl.VertexAttribBinding(location, (uint)slot);
                }

                gl.VertexBindingDivisor((uint)slot, buffer.StepMode == RhiVertexStepMode.Instance ? 1u : 0u);
            }

            gl.BindVertexArray(0);
            device.State.ForgetVertexArray();
        });
    }

    public IRhiPipelineLayout Layout { get; }

    public GlBindingPlan Plan { get; }

    public GlVertexSlot[] Slots { get; }

    public GlRasterState State { get; }

    public GlProgram.Linked Program { get; private set; }

    public uint VertexArray { get; private set; }

    /// <summary>Gets the ring a per-instance layout's pushes go to; null for a layout pushing through the uniform ring.</summary>
    public RhiInstancePushRing? InstanceRing => ((GlPipelineLayout)Layout).PushDataPerInstance
        ? Device.InstancePushRing(((GlPipelineLayout)Layout).PushDataBytes)
        : null;

    protected override void Release(GL gl)
    {
        Device.State.ForgetProgram(Program.Name);
        Device.State.ForgetVertexArray();
        gl.DeleteProgram(Program.Name);
        gl.DeleteVertexArray(VertexArray);
    }
}

/// <summary>A linked compute program.</summary>
internal sealed class GlComputePipeline : GlObject, IRhiComputePipeline
{
    public GlComputePipeline(GlRhiDevice device, RhiComputePipelineDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        GlShaderModule module = (GlShaderModule)descriptor.Compute.Module;
        GlPipelineLayout layout = (GlPipelineLayout)descriptor.Layout;
        Layout = layout;
        Plan = GlBindingPlan.For(module.Reflection, layout);

        device.Thread.Run(() =>
        {
            Program = GlProgram.Build(device.Gl, module, [(ShaderStage.Compute, descriptor.Compute.Constants)], Label);
            device.Label(GLEnum.Program, Program.Name, Label);
        });
    }

    public IRhiPipelineLayout Layout { get; }

    public GlBindingPlan Plan { get; }

    public GlProgram.Linked Program { get; private set; }

    protected override void Release(GL gl)
    {
        Device.State.ForgetProgram(Program.Name);
        gl.DeleteProgram(Program.Name);
    }
}

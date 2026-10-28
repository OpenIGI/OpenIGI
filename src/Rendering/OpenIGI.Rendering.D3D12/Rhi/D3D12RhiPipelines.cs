using System.Buffers;
using System.Text;
using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.Shaders.Dxil;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>A root signature, and where it put every group's parameters.</summary>
/// <remarks>
/// <para>
/// Built from a pipeline layout and the HLSL registers the pipeline's shaders read, because Slang assigns
/// those registers itself: two modules that share a layout do not share register numbers, so they do not
/// share a root signature. They do share one parameter ORDER, which is the layout's -- push data, then the
/// specialisation constants, then each group's tables and root descriptors -- so binding a group never
/// depends on which shader is bound, only where the signature put it.
/// </para>
/// <para>
/// Every range is one descriptor at its own register with its own offset into the group's run, so the
/// order a bind group lists its entries in is free of the register numbers altogether.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12RootSignature : IDisposable
{
    private ID3D12RootSignature* _signature;

    /// <summary>Initialises a new instance of the <see cref="D3D12RootSignature"/> class.</summary>
    /// <param name="signature">The root signature, which this owns.</param>
    /// <param name="fingerprint">A hash of its serialised form, the same in every run.</param>
    /// <param name="push">The push data's root constants parameter, or -1.</param>
    /// <param name="pushWords">How many 32-bit words of push data it takes.</param>
    /// <param name="constants">The specialisation constants' root CBV parameter, or -1.</param>
    /// <param name="groups">Each group's parameters, by group index.</param>
    public D3D12RootSignature(ID3D12RootSignature* signature, byte[] fingerprint, int push, int pushWords, int constants, D3D12GroupParameters[] groups)
    {
        _signature = signature;
        Fingerprint = fingerprint;
        Push = push;
        PushWords = pushWords;
        Constants = constants;
        Groups = groups;
    }

    /// <summary>Gets the root signature.</summary>
    public ID3D12RootSignature* Native => _signature;

    /// <summary>Gets a hash of its serialised form, which names it to the pipeline library across runs.</summary>
    public byte[] Fingerprint { get; }

    /// <summary>Gets the push data's root parameter, or -1 where no shader reads push data.</summary>
    public int Push { get; }

    /// <summary>Gets how many words of push data the parameter holds.</summary>
    public int PushWords { get; }

    /// <summary>Gets the specialisation constants' root parameter, or -1.</summary>
    public int Constants { get; }

    /// <summary>Gets each group's parameters.</summary>
    public D3D12GroupParameters[] Groups { get; }

    /// <inheritdoc/>
    public void Dispose() => D3D12Check.Release(ref _signature);

    /// <summary>Builds the root signature for a layout and the shaders bound through it.</summary>
    /// <param name="device">The device.</param>
    /// <param name="layout">The pipeline layout.</param>
    /// <param name="reflections">What each stage's module binds, and where its DXIL form binds it.</param>
    /// <param name="graphics">Whether it serves a graphics pipeline, which reads an input layout.</param>
    /// <param name="label">A name.</param>
    /// <returns>The root signature.</returns>
    /// <exception cref="GraphicsDeviceException">The layout needs more than a root signature holds.</exception>
    public static D3D12RootSignature Build(
        D3D12RhiDevice device,
        D3D12RhiPipelineLayout layout,
        IReadOnlyList<ShaderReflection> reflections,
        bool graphics,
        string label)
    {
        List<RootParameter1> parameters = [];
        List<DescriptorRange1[]> rangeArrays = [];
        int words = 0;

        // Push data: root constants at the register the push block landed in.
        int push = -1;
        int pushWords = 0;
        ShaderPushBlock? block = reflections.Select(r => r.PushBlock).FirstOrDefault(b => b?.Hlsl is not null);
        if (layout.PushDataBytes > 0 && block?.Hlsl is { } pushRegister)
        {
            pushWords = (Math.Max(layout.PushDataBytes, block.Size) + 3) / 4;
            push = parameters.Count;
            parameters.Add(new RootParameter1
            {
                ParameterType = RootParameterType.Type32BitConstants,
                ShaderVisibility = ShaderVisibility.All,
                Constants = new RootConstants
                {
                    ShaderRegister = (uint)pushRegister.Register,
                    RegisterSpace = (uint)pushRegister.Space,
                    Num32BitValues = (uint)pushWords,
                },
            });
            words += pushWords;
        }

        // The specialisation constants: the constant buffer Slang gathered them into.
        int constants = -1;
        if (reflections.Select(r => r.HlslConstants).FirstOrDefault(c => c is not null) is { } constantRegister)
        {
            constants = parameters.Count;
            parameters.Add(RootDescriptor(RootParameterType.TypeCbv, constantRegister));
            words += 2;
        }

        D3D12GroupParameters[] groups = new D3D12GroupParameters[layout.Groups.Count];
        for (int group = 0; group < layout.Groups.Count; group++)
        {
            D3D12RhiBindGroupLayout groupLayout = layout.Groups[group];
            List<DescriptorRange1> table = [];
            List<DescriptorRange1> samplers = [];
            (int, RootParameterType)[] dynamic = new (int, RootParameterType)[groupLayout.DynamicCount];

            foreach (D3D12BindingSlot slot in groupLayout.Slots)
            {
                ShaderHlslRegister? register = Find(reflections, group, slot.Entry.Binding);
                if (slot.Dynamic >= 0)
                {
                    if (register is null)
                    {
                        dynamic[slot.Dynamic] = (-1, RootParameterType.TypeCbv);
                        continue;
                    }

                    RootParameterType type = register.Class switch
                    {
                        ShaderHlslClass.ConstantBuffer => RootParameterType.TypeCbv,
                        ShaderHlslClass.UnorderedAccess => RootParameterType.TypeUav,
                        _ => RootParameterType.TypeSrv,
                    };
                    dynamic[slot.Dynamic] = (-1, type);
                    continue;
                }

                if (register is null)
                {
                    // An entry no stage reads: its descriptor stays in the group's run, unaddressed.
                    continue;
                }

                DescriptorRange1 range = new()
                {
                    RangeType = register.Class switch
                    {
                        ShaderHlslClass.ShaderResource => DescriptorRangeType.Srv,
                        ShaderHlslClass.UnorderedAccess => DescriptorRangeType.Uav,
                        ShaderHlslClass.ConstantBuffer => DescriptorRangeType.Cbv,
                        _ => DescriptorRangeType.Sampler,
                    },
                    NumDescriptors = 1,
                    BaseShaderRegister = (uint)register.Register,
                    RegisterSpace = (uint)register.Space,

                    Flags = RangeFlags(register.Class),
                    OffsetInDescriptorsFromTableStart = (uint)slot.Table,
                };

                if (register.Class == ShaderHlslClass.Sampler)
                {
                    samplers.Add(range);
                }
                else
                {
                    table.Add(range);
                }
            }

            int tableParameter = -1;
            if (table.Count > 0)
            {
                tableParameter = parameters.Count;
                DescriptorRange1[] ranges = [.. table];
                rangeArrays.Add(ranges);
                parameters.Add(new RootParameter1 { ParameterType = RootParameterType.TypeDescriptorTable, ShaderVisibility = ShaderVisibility.All });
                words += 1;
            }

            int samplerParameter = -1;
            if (samplers.Count > 0)
            {
                samplerParameter = parameters.Count;
                DescriptorRange1[] ranges = [.. samplers];
                rangeArrays.Add(ranges);
                parameters.Add(new RootParameter1 { ParameterType = RootParameterType.TypeDescriptorTable, ShaderVisibility = ShaderVisibility.All });
                words += 1;
            }

            for (int i = 0; i < dynamic.Length; i++)
            {
                D3D12BindingSlot slot = groupLayout.Slots.First(s => s.Dynamic == i);
                if (Find(reflections, group, slot.Entry.Binding) is not { } register)
                {
                    continue;
                }

                dynamic[i] = (parameters.Count, dynamic[i].Item2);
                parameters.Add(RootDescriptor(dynamic[i].Item2, register));
                words += 2;
            }

            groups[group] = new D3D12GroupParameters(tableParameter, samplerParameter, dynamic);
        }

        if (words > 64)
        {
            throw new GraphicsDeviceException(
                $"'{label}' needs {words} root signature words, and Direct3D 12 holds 64. Move push data into a "
                + "bound buffer or fold dynamic buffers into a table.");
        }

        RootParameter1[] array = [.. parameters];
        System.Runtime.InteropServices.GCHandle[] pins = new System.Runtime.InteropServices.GCHandle[rangeArrays.Count];
        try
        {
            int next = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].ParameterType != RootParameterType.TypeDescriptorTable)
                {
                    continue;
                }

                pins[next] = System.Runtime.InteropServices.GCHandle.Alloc(rangeArrays[next], System.Runtime.InteropServices.GCHandleType.Pinned);
                array[i].DescriptorTable = new RootDescriptorTable1
                {
                    NumDescriptorRanges = (uint)rangeArrays[next].Length,
                    PDescriptorRanges = (DescriptorRange1*)pins[next].AddrOfPinnedObject(),
                };
                next++;
            }

            return Create(device, array, graphics, label, push, pushWords, constants, groups);
        }
        finally
        {
            foreach (System.Runtime.InteropServices.GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    private static D3D12RootSignature Create(
        D3D12RhiDevice device,
        RootParameter1[] parameters,
        bool graphics,
        string label,
        int push,
        int pushWords,
        int constants,
        D3D12GroupParameters[] groups)
    {
        fixed (RootParameter1* first = parameters)
        {
            VersionedRootSignatureDesc description = new() { Version = D3DRootSignatureVersion.Version11 };
            description.Desc11 = new RootSignatureDesc1
            {
                NumParameters = (uint)parameters.Length,
                PParameters = first,
                Flags = (graphics ? RootSignatureFlags.AllowInputAssemblerInputLayout : RootSignatureFlags.None)

                    // A traced hit reaches its mesh through ResourceDescriptorHeap, which a signature must allow.
                    | RootSignatureFlags.CbvSrvUavHeapDirectlyIndexed,
            };

            ID3D10Blob* blob = null;
            ID3D10Blob* error = null;
            // The serialiser must be the Agility runtime's, which the device's configuration interface is. The
            // exported D3D12SerializeVersionedRootSignature is the in-box d3d12.dll's, and an older Windows
            // refuses CbvSrvUavHeapDirectlyIndexed there although the device runs it.
            ID3D12DeviceConfiguration* configuration = null;
            Guid configurationIid = ID3D12DeviceConfiguration.Guid;
            int serialised;
            if (((IUnknown*)device.Native)->QueryInterface(&configurationIid, (void**)&configuration) >= 0 && configuration is not null)
            {
                serialised = configuration->SerializeVersionedRootSignature(&description, &blob, &error);
                configuration->Release();
            }
            else
            {
                serialised = device.Api.SerializeVersionedRootSignature(&description, &blob, &error);
            }

            if (serialised < 0)
            {
                string message = error is null
                    ? string.Empty
                    : Encoding.UTF8.GetString((byte*)error->GetBufferPointer(), (int)error->GetBufferSize());
                if (error is not null)
                {
                    error->Release();
                }

                throw new GraphicsDeviceException($"The root signature for '{label}' did not serialise (0x{serialised:X8}): {message}");
            }

            try
            {
                ID3D12RootSignature* signature = null;
                Guid iid = ID3D12RootSignature.Guid;
                D3D12Check.Ok(
                    device.Native->CreateRootSignature(0, blob->GetBufferPointer(), blob->GetBufferSize(), &iid, (void**)&signature),
                    $"CreateRootSignature ({label})");
                D3D12Names.Set((ID3D12Object*)signature, label);
                byte[] fingerprint = System.Security.Cryptography.SHA256.HashData(
                    new ReadOnlySpan<byte>(blob->GetBufferPointer(), (int)blob->GetBufferSize()));
                return new D3D12RootSignature(signature, fingerprint, push, pushWords, constants, groups);
            }
            finally
            {
                blob->Release();
            }
        }
    }

    /// <summary>What a range promises the driver, by what it holds.</summary>
    /// <remarks>
    /// <para>
    /// What a shader only reads -- a texture, a constant or read-only buffer -- does not change while a draw or
    /// dispatch that reads it runs, which lets the driver prefetch it; what a shader stores to may change under
    /// it, so it is promised nothing. A dispatch that reads what the one before it wrote is still correct: the
    /// promise covers one draw or dispatch, not the table's whole time bound.
    /// </para>
    /// <para>
    /// The descriptors themselves are volatile: a group's run holds a descriptor for every entry of its
    /// layout, including those no shader of this pipeline reads, and a static range would oblige every one of
    /// them to be valid while the table is bound.
    /// </para>
    /// </remarks>
    private static DescriptorRangeFlags RangeFlags(ShaderHlslClass kind) => kind switch
    {
        ShaderHlslClass.Sampler => DescriptorRangeFlags.DescriptorsVolatile,
        ShaderHlslClass.UnorderedAccess => DescriptorRangeFlags.DataVolatile | DescriptorRangeFlags.DescriptorsVolatile,
        _ => DescriptorRangeFlags.DataStaticWhileSetATExecute | DescriptorRangeFlags.DescriptorsVolatile,
    };

    private static RootParameter1 RootDescriptor(RootParameterType type, ShaderHlslRegister register) => new()
    {
        ParameterType = type,
        ShaderVisibility = ShaderVisibility.All,
        Descriptor = new RootDescriptor1
        {
            ShaderRegister = (uint)register.Register,
            RegisterSpace = (uint)register.Space,
            // Constants are written before the work that reads them and left alone until it is done; a
            // root view a shader stores through promises nothing.
            Flags = type == RootParameterType.TypeUav ? RootDescriptorFlags.DataVolatile : RootDescriptorFlags.DataStaticWhileSetATExecute,
        },
    };

    private static ShaderHlslRegister? Find(IReadOnlyList<ShaderReflection> reflections, int group, int binding)
    {
        foreach (ShaderReflection reflection in reflections)
        {
            foreach (ShaderBinding candidate in reflection.Bindings)
            {
                if (candidate.Group == group && candidate.Binding == binding && candidate.Hlsl is { } register)
                {
                    return register;
                }
            }
        }

        return null;
    }
}

/// <summary>What a pipeline of either kind binds through: its root signature and its constants.</summary>
internal abstract class D3D12RhiPipeline : IDisposable
{
    /// <summary>Gets or sets the root signature, shared with every pipeline of the same layout and shaders.</summary>
    public D3D12RootSignature RootSignature { get; protected set; } = null!;

    /// <summary>Gets or sets the specialisation constants' buffer, or null where the shaders have none.</summary>
    public D3D12RhiBuffer? Constants { get; protected set; }

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <summary>Writes the constant buffer HLSL reads a pipeline's specialisation constants from.</summary>
    /// <param name="device">The device.</param>
    /// <param name="stages">Each stage's reflection and the constants the pipeline supplies for it.</param>
    /// <param name="label">A name.</param>
    /// <returns>The buffer, or null where no stage declares a constant.</returns>
    /// <remarks>
    /// Every constant the shaders declare, at the offset Slang gave it: the one the pipeline supplies, or the
    /// value the source declared. Vulkan and WebGPU fall back to that value on their own; here nothing would,
    /// and a constant left out would read nought.
    /// </remarks>
    protected static unsafe D3D12RhiBuffer? WriteConstants(
        D3D12RhiDevice device,
        IEnumerable<(ShaderReflection Reflection, IReadOnlyDictionary<int, RhiConstant>? Supplied)> stages,
        string label)
    {
        // One buffer serves every stage, where Vulkan specialises each on its own. A value a stage supplies
        // wins over the declared default another stage would fall back to -- the two stages of one module see
        // the same constants, and a stage that does not read one has no reason to supply it. Only two stages
        // supplying different values for one constant cannot be expressed here.
        Dictionary<int, uint> words = [];
        Dictionary<int, uint> defaults = [];
        foreach ((ShaderReflection reflection, IReadOnlyDictionary<int, RhiConstant>? supplied) in stages)
        {
            foreach (ShaderSpecializationConstant constant in reflection.SpecializationConstants)
            {
                if (constant.HlslOffset is not { } offset)
                {
                    continue;
                }

                if (supplied is null || !supplied.TryGetValue(constant.Id, out RhiConstant given))
                {
                    defaults.TryAdd(offset, constant.Default);
                    continue;
                }

                uint value = D3D12Translation.ConstantBits(given);
                if (words.TryGetValue(offset, out uint existing) && existing != value)
                {
                    throw new GraphicsDeviceException(
                        $"'{label}': two stages supply the specialisation constant at offset {offset} with different values.");
                }

                words[offset] = value;
            }
        }

        foreach ((int offset, uint value) in defaults)
        {
            words.TryAdd(offset, value);
        }

        if (words.Count == 0)
        {
            return null;
        }

        int bytes = (words.Keys.Max() + 4 + 255) & ~255;
        D3D12RhiBuffer buffer = (D3D12RhiBuffer)device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)bytes, RhiBufferUsage.Uniform | RhiBufferUsage.MapWrite, $"{label} constants"));

        foreach ((int offset, uint value) in words)
        {
            *(uint*)(buffer.Mapped + offset) = value;
        }

        return buffer;
    }
}

/// <summary>A graphics pipeline state.</summary>
internal sealed unsafe class D3D12RhiRenderPipeline : D3D12RhiPipeline, IRhiRenderPipeline
{
    private static readonly byte[] Semantic = "TEXCOORD\0"u8.ToArray();
    private ID3D12PipelineState* _state;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiRenderPipeline"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="descriptor">What it is created as.</param>
    public D3D12RhiRenderPipeline(D3D12RhiDevice device, RhiRenderPipelineDescriptor descriptor)
    {
        Layout = descriptor.Layout;
        Label = descriptor.Label;
        D3D12RhiPipelineLayout layout = (D3D12RhiPipelineLayout)descriptor.Layout;

        D3D12RhiShaderModule vertex = (D3D12RhiShaderModule)descriptor.Vertex.Module;
        D3D12RhiShaderModule? fragment = descriptor.Fragment?.Module as D3D12RhiShaderModule;
        List<ShaderReflection> reflections = [vertex.Reflection];
        if (fragment is not null && fragment.Reflection != vertex.Reflection)
        {
            reflections.Add(fragment.Reflection);
        }

        RootSignature = device.RootSignatureFor(layout, reflections, graphics: true, descriptor.Label);
        Constants = WriteConstants(
            device,
            [(vertex.Reflection, descriptor.Vertex.Constants), .. fragment is null ? [] : new[] { (fragment.Reflection, descriptor.Fragment!.Constants) }],
            descriptor.Label);

        Topology = D3D12Translation.Topology(descriptor.Primitive.Topology);
        Strides = [.. descriptor.VertexBuffers.Select(buffer => buffer.Stride)];

        ReadOnlyMemory<byte> vs = DxilCatalog.Get(vertex.Module, vertex.Variant, StageOf(vertex, descriptor.Vertex.EntryPoint));
        ReadOnlyMemory<byte> ps = fragment is null
            ? ReadOnlyMemory<byte>.Empty
            : DxilCatalog.Get(fragment.Module, fragment.Variant, StageOf(fragment, descriptor.Fragment!.EntryPoint));

        List<InputElementDesc> elements = [];
        using MemoryHandle semantic = Semantic.AsMemory().Pin();
        for (int slot = 0; slot < descriptor.VertexBuffers.Count; slot++)
        {
            RhiVertexBufferLayout buffer = descriptor.VertexBuffers[slot];
            bool instanced = buffer.StepMode == RhiVertexStepMode.Instance;
            foreach (RhiVertexElement attribute in buffer.Attributes)
            {
                // The vertex stage names each input TEXCOORDn after its location: one numbering for both APIs.
                elements.Add(new InputElementDesc
                {
                    SemanticName = (byte*)semantic.Pointer,
                    SemanticIndex = (uint)attribute.Location,
                    Format = D3D12Translation.VertexFormat(attribute.Format),
                    InputSlot = (uint)slot,
                    AlignedByteOffset = (uint)attribute.Offset,
                    InputSlotClass = instanced ? InputClassification.PerInstanceData : InputClassification.PerVertexData,
                    InstanceDataStepRate = instanced ? 1u : 0u,
                });
            }
        }

        InputElementDesc[] inputs = [.. elements];
        using MemoryHandle vsPin = vs.Pin();
        using MemoryHandle psPin = ps.Pin();
        fixed (InputElementDesc* firstInput = inputs)
        {
            GraphicsPipelineStateDesc state = new()
            {
                PRootSignature = RootSignature.Native,
                VS = new ShaderBytecode(vsPin.Pointer, (nuint)vs.Length),
                PS = ps.IsEmpty ? default : new ShaderBytecode(psPin.Pointer, (nuint)ps.Length),
                SampleMask = uint.MaxValue,
                InputLayout = new InputLayoutDesc { PInputElementDescs = firstInput, NumElements = (uint)inputs.Length },
                IBStripCutValue = descriptor.Primitive.StripIndexFormat switch
                {
                    RhiIndexFormat.Uint16 => IndexBufferStripCutValue.Value0xFfff,
                    RhiIndexFormat.Uint32 => IndexBufferStripCutValue.Value0xFfffffff,
                    _ => IndexBufferStripCutValue.ValueDisabled,
                },
                PrimitiveTopologyType = D3D12Translation.TopologyType(descriptor.Primitive.Topology),
                NumRenderTargets = (uint)descriptor.ColorTargets.Count,
                SampleDesc = new SampleDesc((uint)Math.Max(1, descriptor.SampleCount), 0),
                RasterizerState = new RasterizerDesc
                {
                    FillMode = FillMode.Solid,
                    CullMode = D3D12Translation.CullMode(descriptor.Primitive.CullMode),
                    FrontCounterClockwise = descriptor.Primitive.FrontFace == RhiFrontFace.CounterClockwise,
                    DepthBias = (int)MathF.Round(descriptor.DepthStencil?.DepthBiasConstant ?? 0f),
                    SlopeScaledDepthBias = descriptor.DepthStencil?.DepthBiasSlopeScale ?? 0f,
                    DepthClipEnable = true,
                    MultisampleEnable = descriptor.SampleCount > 1,
                },
            };

            if (descriptor.DepthStencil is { } depth)
            {
                state.DSVFormat = D3D12Translation.ViewFormat(depth.Format);
                state.DepthStencilState = new DepthStencilDesc
                {
                    DepthEnable = true,
                    DepthWriteMask = depth.DepthWrite ? DepthWriteMask.All : DepthWriteMask.Zero,
                    DepthFunc = D3D12Translation.Compare(depth.DepthCompare),
                };
            }

            state.BlendState.IndependentBlendEnable = descriptor.ColorTargets.Count > 1;
            for (int i = 0; i < descriptor.ColorTargets.Count; i++)
            {
                RhiColorTargetState target = descriptor.ColorTargets[i];
                state.RTVFormats[i] = D3D12Translation.ViewFormat(target.Format);
                ref RenderTargetBlendDesc blend = ref state.BlendState.RenderTarget[i];
                blend.RenderTargetWriteMask = (byte)target.Write;
                if (target.Blend is { } mode)
                {
                    blend.BlendEnable = true;
                    blend.SrcBlend = D3D12Translation.BlendFactor(mode.Color.Source, alpha: false);
                    blend.DestBlend = D3D12Translation.BlendFactor(mode.Color.Destination, alpha: false);
                    blend.BlendOp = D3D12Translation.BlendOperation(mode.Color.Operation);
                    blend.SrcBlendAlpha = D3D12Translation.BlendFactor(mode.Alpha.Source, alpha: true);
                    blend.DestBlendAlpha = D3D12Translation.BlendFactor(mode.Alpha.Destination, alpha: true);
                    blend.BlendOpAlpha = D3D12Translation.BlendOperation(mode.Alpha.Operation);
                }
            }

            ID3D12PipelineState* pipeline = device.Pipelines.Graphics(&state, RootSignature.Fingerprint, descriptor.Label);
            _state = pipeline;
            D3D12Names.Set((ID3D12Object*)pipeline, descriptor.Label);
        }

        _device = device;
    }

    private readonly D3D12RhiDevice _device;

    /// <summary>Gets the pipeline state.</summary>
    public ID3D12PipelineState* Native => _state;

    /// <summary>Gets the topology a draw sets on the input assembler.</summary>
    public D3DPrimitiveTopology Topology { get; }

    /// <summary>Gets each vertex buffer slot's stride, which Direct3D 12 sets with the buffer rather than the pipeline.</summary>
    public int[] Strides { get; }

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public override void Dispose()
    {
        ID3D12PipelineState* state = _state;
        _state = null;
        D3D12RhiBuffer? constants = Constants;
        Constants = null;
        if (state is not null)
        {
            _device.Retire(() => ((IUnknown*)state)->Release());
        }

        constants?.Dispose();
    }

    /// <summary>The stage an entry point of a module is.</summary>
    /// <param name="module">The module.</param>
    /// <param name="entryPoint">The entry point's name.</param>
    /// <returns>Its stage.</returns>
    public static ShaderStage StageOf(D3D12RhiShaderModule module, string entryPoint)
    {
        foreach (ShaderEntryPoint candidate in module.Reflection.EntryPoints)
        {
            if (candidate.Name == entryPoint)
            {
                return candidate.Stage;
            }
        }

        throw new GraphicsDeviceException($"{module.Module} has no entry point '{entryPoint}'.");
    }
}

/// <summary>A compute pipeline state.</summary>
internal sealed unsafe class D3D12RhiComputePipeline : D3D12RhiPipeline, IRhiComputePipeline
{
    private readonly D3D12RhiDevice _device;
    private ID3D12PipelineState* _state;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiComputePipeline"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="descriptor">What it is created as.</param>
    public D3D12RhiComputePipeline(D3D12RhiDevice device, RhiComputePipelineDescriptor descriptor)
    {
        _device = device;
        Layout = descriptor.Layout;
        Label = descriptor.Label;
        D3D12RhiShaderModule module = (D3D12RhiShaderModule)descriptor.Compute.Module;

        RootSignature = device.RootSignatureFor((D3D12RhiPipelineLayout)descriptor.Layout, [module.Reflection], graphics: false, descriptor.Label);
        Constants = WriteConstants(device, [(module.Reflection, descriptor.Compute.Constants)], descriptor.Label);

        ReadOnlyMemory<byte> cs = DxilCatalog.Get(module.Module, module.Variant, ShaderStage.Compute);
        using MemoryHandle pin = cs.Pin();
        ComputePipelineStateDesc state = new()
        {
            PRootSignature = RootSignature.Native,
            CS = new ShaderBytecode(pin.Pointer, (nuint)cs.Length),
        };

        ID3D12PipelineState* pipeline = device.Pipelines.Compute(&state, RootSignature.Fingerprint, descriptor.Label);
        _state = pipeline;
        D3D12Names.Set((ID3D12Object*)pipeline, descriptor.Label);
    }

    /// <summary>Gets the pipeline state.</summary>
    public ID3D12PipelineState* Native => _state;

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public override void Dispose()
    {
        ID3D12PipelineState* state = _state;
        _state = null;
        D3D12RhiBuffer? constants = Constants;
        Constants = null;
        if (state is not null)
        {
            _device.Retire(() => ((IUnknown*)state)->Release());
        }

        constants?.Dispose();
    }
}

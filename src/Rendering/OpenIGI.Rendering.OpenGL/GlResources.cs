using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Something the device made, which gives its OpenGL name back on the owning thread when disposed.</summary>
internal abstract class GlObject(GlRhiDevice device, string label) : IRhiResource
{
    private bool _disposed;

    public string Label { get; } = label;

    protected GlRhiDevice Device { get; } = device;

    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Device.Thread.Run(() => Release(Device.Gl));
    }

    /// <summary>Deletes the OpenGL objects, on the owning thread.</summary>
    protected abstract void Release(GL gl);
}

/// <summary>A buffer object.</summary>
internal sealed class GlBuffer : GlObject, IRhiBuffer
{
    public GlBuffer(GlRhiDevice device, in RhiBufferDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;

        GLEnum hint = (descriptor.Usage & RhiBufferUsage.MapRead) != 0 ? GLEnum.StreamRead : GLEnum.DynamicDraw;
        ulong size = Math.Max(4UL, descriptor.SizeInBytes);

        device.Thread.Run(() =>
        {
            GL gl = device.Gl;
            Name = gl.GenBuffer();
            gl.BindBuffer(GLEnum.CopyWriteBuffer, Name);
            unsafe
            {
                gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)size, null, hint);
            }

            device.Label(GLEnum.Buffer, Name, Label);
        });
    }

    public uint Name { get; private set; }

    public ulong SizeInBytes { get; }

    public RhiBufferUsage Usage { get; }

    protected override void Release(GL gl)
    {
        Device.State.ForgetBuffer(Name);
        gl.DeleteBuffer(Name);
    }
}

/// <summary>A texture, its storage immutable so views can be made of it.</summary>
internal sealed class GlTexture : GlObject, IRhiTexture
{
    private readonly List<GlTextureView> _views = [];

    public GlTexture(GlRhiDevice device, in RhiTextureDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Width = descriptor.Width;
        Height = descriptor.Height;
        DepthOrArrayLayers = Math.Max(1, descriptor.DepthOrArrayLayers);
        MipLevelCount = Math.Max(1, descriptor.MipLevelCount);
        SampleCount = Math.Max(1, descriptor.SampleCount);
        Format = descriptor.Format;
        Usage = descriptor.Usage;
        Dimension = descriptor.Dimension;
        Gl = GlTranslation.Format(descriptor.Format);

        // Every layered flat texture is stored as an array, cubes included, because an array is what views of any
        // flat shape -- one face, one layer, a cube -- may all be made from.
        Target = descriptor.Dimension switch
        {
            RhiTextureViewDimension.Texture3D => GLEnum.Texture3D,
            _ when SampleCount > 1 => GLEnum.Texture2DMultisample,
            RhiTextureViewDimension.Cube or RhiTextureViewDimension.Texture2DArray => GLEnum.Texture2DArray,
            _ when DepthOrArrayLayers > 1 => GLEnum.Texture2DArray,
            _ => GLEnum.Texture2D,
        };

        device.Thread.Run(() =>
        {
            GL gl = device.Gl;
            Name = gl.GenTexture();
            device.BindForEdit(Target, Name);

            switch (Target)
            {
                case GLEnum.Texture3D:
                case GLEnum.Texture2DArray:
                    gl.TexStorage3D(Target, (uint)MipLevelCount, Gl.Internal, (uint)Width, (uint)Height, (uint)DepthOrArrayLayers);
                    break;
                case GLEnum.Texture2DMultisample:
                    gl.TexStorage2DMultisample(Target, (uint)SampleCount, Gl.Internal, (uint)Width, (uint)Height, true);
                    break;
                default:
                    gl.TexStorage2D(Target, (uint)MipLevelCount, Gl.Internal, (uint)Width, (uint)Height);
                    break;
            }

            if (Target != GLEnum.Texture2DMultisample)
            {
                // The range a sampler object cannot set: without it, a texture with fewer levels than the default
                // thousand is incomplete to a driver that checks.
                gl.TexParameter(Target, GLEnum.TextureMaxLevel, MipLevelCount - 1);
                GlTextureView.NearestByDefault(gl, Target);
            }

            device.Label(GLEnum.Texture, Name, Label);
        });

        View = CreateView(new RhiTextureViewDescriptor(Dimension: Dimension, Label: Label));
    }

    public uint Name { get; private set; }

    public GLEnum Target { get; }

    public GlFormat Gl { get; }

    public RhiTextureViewDimension Dimension { get; }

    public int Width { get; }

    public int Height { get; }

    public int DepthOrArrayLayers { get; }

    public int MipLevelCount { get; }

    public int SampleCount { get; }

    public RhiTextureFormat Format { get; }

    public RhiTextureUsage Usage { get; }

    public IRhiTextureView View { get; }

    public IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor)
    {
        GlTextureView view = new(Device, this, descriptor);
        _views.Add(view);
        return view;
    }

    protected override void Release(GL gl)
    {
        foreach (GlTextureView view in _views)
        {
            view.ReleaseWith(gl);
        }

        _views.Clear();
        Device.State.ForgetTexture(Name);
        gl.DeleteTexture(Name);
    }
}

/// <summary>A view: the texture itself where it covers the whole of it as stored, a texture view object otherwise.</summary>
internal sealed class GlTextureView : GlObject, IRhiTextureView
{
    private readonly GlTexture _texture;
    private bool _released;

    public GlTextureView(GlRhiDevice device, GlTexture texture, in RhiTextureViewDescriptor descriptor)
        : base(device, string.IsNullOrEmpty(descriptor.Label) ? texture.Label : descriptor.Label)
    {
        _texture = texture;
        Format = descriptor.Format == RhiTextureFormat.Undefined ? texture.Format : descriptor.Format;
        Gl = GlTranslation.Format(Format);
        BaseMip = descriptor.BaseMipLevel;
        Levels = descriptor.MipLevelCount > 0 ? descriptor.MipLevelCount : texture.MipLevelCount - BaseMip;
        BaseLayer = descriptor.BaseArrayLayer;
        Layers = texture.Target == GLEnum.Texture3D
            ? 1
            : descriptor.ArrayLayerCount > 0 ? descriptor.ArrayLayerCount : texture.DepthOrArrayLayers - BaseLayer;

        // A flat view over more than one layer can only be an array: OpenGL's 2D view holds exactly one.
        Target = descriptor.Dimension switch
        {
            RhiTextureViewDimension.Texture3D => GLEnum.Texture3D,
            RhiTextureViewDimension.Cube => GLEnum.TextureCubeMap,
            RhiTextureViewDimension.Texture2DArray => GLEnum.Texture2DArray,
            _ when texture.SampleCount > 1 => GLEnum.Texture2DMultisample,
            _ when Layers > 1 => GLEnum.Texture2DArray,
            _ => GLEnum.Texture2D,
        };

        Layered = Target is GLEnum.Texture2DArray or GLEnum.TextureCubeMap or GLEnum.Texture3D;

        bool whole = Target == texture.Target
            && Format == texture.Format
            && BaseMip == 0
            && Levels == texture.MipLevelCount
            && BaseLayer == 0
            && (Layers == texture.DepthOrArrayLayers || texture.Target == GLEnum.Texture3D);

        Owned = !whole;
        device.Thread.Run(() =>
        {
            if (!Owned)
            {
                Name = texture.Name;
                return;
            }

            GL gl = device.Gl;
            Name = gl.GenTexture();
            gl.TextureView(
                Name,
                Target,
                texture.Name,
                Gl.Internal,
                (uint)BaseMip,
                (uint)Levels,
                (uint)(texture.Target == GLEnum.Texture3D ? 0 : BaseLayer),
                (uint)(texture.Target == GLEnum.Texture3D ? 1 : Layers));

            if (Target != GLEnum.Texture2DMultisample)
            {
                device.BindForEdit(Target, Name);
                NearestByDefault(gl, Target);
            }

            device.Label(GLEnum.Texture, Name, Label);
        });
    }

    /// <summary>Sets a texture object's own filters to nearest, which is what a unit with no sampler object reads it with.</summary>
    /// <remarks>
    /// A texture fetched texel by texel is bound with no sampler, so its own parameters decide whether it is complete.
    /// The default minification filter blends mips, which makes an integer texture incomplete -- and an incomplete
    /// texture fetches nought. Nearest is what a fetch means anyway, and a sampler object overrides it everywhere else.
    /// </remarks>
    internal static void NearestByDefault(GL gl, GLEnum target)
    {
        gl.TexParameter(target, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(target, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
    }

    /// <summary>Gets the name to bind: the texture's own, or the view object's.</summary>
    public uint Name { get; private set; }

    /// <summary>Gets whether the name is a view object of its own, deleted with the view.</summary>
    public bool Owned { get; }

    public GLEnum Target { get; }

    public GlFormat Gl { get; }

    public bool Layered { get; }

    public int BaseMip { get; }

    public int Levels { get; }

    public int BaseLayer { get; }

    public int Layers { get; }

    public GlTexture GlTexture => _texture;

    public IRhiTexture Texture => _texture;

    public RhiTextureFormat Format { get; }

    /// <summary>Gets the view's size at its first level.</summary>
    public (int Width, int Height) Size => (Math.Max(1, _texture.Width >> BaseMip), Math.Max(1, _texture.Height >> BaseMip));

    public void ReleaseWith(GL gl)
    {
        if (_released)
        {
            return;
        }

        _released = true;
        Device.Framebuffers.Forget(this);
        if (Owned)
        {
            Device.State.ForgetTexture(Name);
            gl.DeleteTexture(Name);
        }
    }

    protected override void Release(GL gl) => ReleaseWith(gl);
}

/// <summary>A sampler object.</summary>
internal sealed class GlSampler : GlObject, IRhiSampler
{
    public GlSampler(GlRhiDevice device, RhiSamplerDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        device.Thread.Run(() =>
        {
            GL gl = device.Gl;
            Name = gl.GenSampler();
            gl.SamplerParameter(Name, GLEnum.TextureMinFilter, (int)GlTranslation.MinFilter(descriptor.MinFilter, descriptor.MipFilter));
            gl.SamplerParameter(Name, GLEnum.TextureMagFilter, (int)GlTranslation.MagFilter(descriptor.MagFilter));
            gl.SamplerParameter(Name, GLEnum.TextureWrapS, (int)GlTranslation.Address(descriptor.AddressU));
            gl.SamplerParameter(Name, GLEnum.TextureWrapT, (int)GlTranslation.Address(descriptor.AddressV));
            gl.SamplerParameter(Name, GLEnum.TextureWrapR, (int)GlTranslation.Address(descriptor.AddressW));
            gl.SamplerParameter(Name, GLEnum.TextureMinLod, descriptor.LodMin);
            gl.SamplerParameter(Name, GLEnum.TextureMaxLod, descriptor.LodMax);
            gl.SamplerParameter(Name, GLEnum.TextureLodBias, descriptor.MipLodBias);

            if (device.Caps.Anisotropy && descriptor.MaxAnisotropy > 1)
            {
                gl.SamplerParameter(Name, GLEnum.TextureMaxAnisotropy, Math.Min(descriptor.MaxAnisotropy, device.Caps.MaxAnisotropy));
            }

            if (descriptor.Compare is { } compare)
            {
                gl.SamplerParameter(Name, GLEnum.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
                gl.SamplerParameter(Name, GLEnum.TextureCompareFunc, (int)GlTranslation.Compare(compare));
            }

            device.Label(GLEnum.Sampler, Name, Label);
        });
    }

    public uint Name { get; private set; }

    protected override void Release(GL gl)
    {
        Device.State.ForgetSampler(Name);
        gl.DeleteSampler(Name);
    }
}

/// <summary>A block of query objects.</summary>
internal sealed class GlQuerySet : GlObject, IRhiQuerySet
{
    public GlQuerySet(GlRhiDevice device, in RhiQuerySetDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Type = descriptor.Type;
        Count = descriptor.Count;
        Names = new uint[descriptor.Count];
        device.Thread.Run(() => device.Gl.GenQueries(Names));
    }

    public RhiQueryType Type { get; }

    public int Count { get; }

    /// <summary>Gets one query object per slot.</summary>
    public uint[] Names { get; }

    /// <summary>Gets whether each slot has been written since it was made, which a resolve of an unwritten slot needs to know.</summary>
    public bool[] Written => _written ??= new bool[Count];

    private bool[]? _written;

    protected override void Release(GL gl) => gl.DeleteQueries(Names);
}

/// <summary>One module of the library: its GLSL is compiled per pipeline, because constants are fixed then.</summary>
internal sealed class GlShaderModule(GlRhiDevice device, ShaderModuleId module, ShaderVariant variant, ShaderReflection reflection, string label)
    : GlObject(device, label), IRhiShaderModule
{
    public ShaderModuleId Module { get; } = module;

    public ShaderVariant Variant { get; } = variant;

    public ShaderReflection Reflection { get; } = reflection;

    protected override void Release(GL gl)
    {
    }
}

/// <summary>A bind group layout: its entries, by binding.</summary>
internal sealed class GlBindGroupLayout(GlRhiDevice device, RhiBindGroupLayoutDescriptor descriptor)
    : GlObject(device, descriptor.Label), IRhiBindGroupLayout
{
    public IReadOnlyList<RhiBindGroupLayoutEntry> Entries { get; } = descriptor.Entries;

    protected override void Release(GL gl)
    {
    }
}

/// <summary>What one entry of a bind group binds, resolved to OpenGL objects.</summary>
/// <param name="Binding">The entry's binding in its group.</param>
/// <param name="Buffer">The buffer, for a buffer entry.</param>
/// <param name="Offset">Where the buffer range starts.</param>
/// <param name="Size">How long the range is.</param>
/// <param name="View">The view, for a texture entry.</param>
/// <param name="Sampler">The sampler, for a sampler entry.</param>
/// <param name="DynamicIndex">Which of the group's dynamic offsets is added to the offset, or -1 for none.</param>
/// <param name="Access">How a storage texture entry is reached.</param>
internal readonly record struct GlBoundEntry(
    int Binding,
    GlBuffer? Buffer,
    ulong Offset,
    ulong Size,
    GlTextureView? View,
    GlSampler? Sampler,
    int DynamicIndex,
    RhiStorageAccess Access);

/// <summary>A bind group: its entries resolved, indexed by binding.</summary>
internal sealed class GlBindGroup : GlObject, IRhiBindGroup
{
    public GlBindGroup(GlRhiDevice device, RhiBindGroupDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        GlBindGroupLayout layout = (GlBindGroupLayout)descriptor.Layout;

        // Dynamic offsets are given in binding order, so each dynamic entry's index is its rank among them.
        int[] dynamicBindings = [.. layout.Entries.Where(e => e.HasDynamicOffset).Select(e => e.Binding).Order()];

        int highest = descriptor.Entries.Count == 0 ? -1 : descriptor.Entries.Max(e => e.Binding);
        Entries = new GlBoundEntry?[highest + 1];

        foreach (RhiBindGroupEntry entry in descriptor.Entries)
        {
            RhiBindGroupLayoutEntry? declared = layout.Entries.FirstOrDefault(e => e.Binding == entry.Binding);
            ulong size = entry.Buffer is { } buffer
                ? entry.Size > 0 ? entry.Size : buffer.SizeInBytes - entry.Offset
                : 0;

            Entries[entry.Binding] = new GlBoundEntry(
                entry.Binding,
                (GlBuffer?)entry.Buffer,
                entry.Offset,
                size,
                (GlTextureView?)entry.TextureView,
                (GlSampler?)entry.Sampler,
                Array.IndexOf(dynamicBindings, entry.Binding),
                declared?.StorageAccess ?? RhiStorageAccess.ReadWrite);
        }
    }

    /// <summary>Gets the entries, indexed by binding; null where the group has no entry at that binding.</summary>
    public GlBoundEntry?[] Entries { get; }

    protected override void Release(GL gl)
    {
    }
}

/// <summary>A pipeline layout: the group layouts, and how push data reaches the shader.</summary>
internal sealed class GlPipelineLayout(GlRhiDevice device, RhiPipelineLayoutDescriptor descriptor)
    : GlObject(device, descriptor.Label), IRhiPipelineLayout
{
    public int PushDataBytes { get; } = descriptor.PushDataBytes;

    public bool PushDataPerInstance { get; } = descriptor.PushDataPerInstance;

    /// <summary>Gets the layout of each group, indexed by group.</summary>
    public IReadOnlyList<IRhiBindGroupLayout> Groups { get; } = descriptor.BindGroupLayouts;

    protected override void Release(GL gl)
    {
    }
}

/// <summary>Recorded commands, replayed when submitted.</summary>
internal sealed class GlCommandBuffer(GlRhiDevice device, GlCommandList commands, string label)
    : GlObject(device, label), IRhiCommandBuffer
{
    private GlCommandList? _commands = commands;

    /// <summary>Takes the commands to replay, once: the list goes back to the device's pool after it.</summary>
    public GlCommandList Take()
    {
        GlCommandList taken = _commands ?? throw new InvalidOperationException($"The command buffer '{Label}' was already submitted.");
        _commands = null;
        return taken;
    }

    protected override void Release(GL gl)
    {
    }
}

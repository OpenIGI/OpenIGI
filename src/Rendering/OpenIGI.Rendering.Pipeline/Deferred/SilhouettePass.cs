using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>One body marked for the silhouette highlight, as it was drawn.</summary>
/// <param name="Vertices">The pose it was drawn from.</param>
/// <param name="PreviousVertices">The pose before it, which the blend mixes with the first.</param>
/// <param name="Indices">The mesh's indices.</param>
/// <param name="Ranges">The index ranges that make up the model.</param>
/// <param name="Transform">Its model-view-projection, unjittered: the mask is laid over the finished picture.</param>
/// <param name="PoseBlend">How much of the previous pose is in the position drawn.</param>
/// <param name="Group">Which group it is highlighted as.</param>
internal readonly record struct SilhouetteDraw(
    IRhiBuffer Vertices,
    IRhiBuffer PreviousVertices,
    IRhiBuffer Indices,
    MeshDrawRange[] Ranges,
    Matrix4f Transform,
    float PoseBlend,
    SilhouetteGroup Group);

/// <summary>One group's look: its fill and its outline, each a colour and an opacity.</summary>
/// <param name="Fill">The fill, red, green, blue and opacity from nought to one, as the display shows it.</param>
/// <param name="Outline">The outline, likewise.</param>
public readonly record struct SilhouetteLook(
    (float R, float G, float B, float A) Fill,
    (float R, float G, float B, float A) Outline);

/// <summary>The silhouette highlight: the marked bodies' silhouettes found in a mask and laid over the finished picture.</summary>
/// <remarks>
/// <para>
/// Two passes. The marked bodies are drawn, each in the pose it was drawn in, into a mask the size of the screen: one channel a
/// group, written by taking the greater, so limbs over limbs and bodies over bodies of a group merge into one silhouette and
/// nothing counts twice. No depth test, which is what lets the highlight be seen through walls. Then one full-screen pass
/// (<c>Post/Silhouette.slang</c>) finds each group's edges in a five-by-five neighbourhood and lays the group's fill and outline
/// over the picture, premultiplied, fading towards the screen's edges.
/// </para>
/// <para>
/// After the tone map and before the interface: the highlight is a mark on the picture rather than light in the scene, so it is
/// neither exposed, bloomed nor graded.
/// </para>
/// </remarks>
internal sealed class SilhouettePass : IDisposable
{
    /// <summary>The hostile look: a red outline at seven-tenths over a faint dark-red fill, the look of a marked target.</summary>
    public static readonly SilhouetteLook Hostile = new((0.322f, 0f, 0f, 0.392f), (0.812f, 0f, 0f, 0.686f));

    /// <summary>The team's look: a half-opaque green outline over a faint green fill.</summary>
    public static readonly SilhouetteLook Team = new((0f, 0.361f, 0f, 0.196f), (0f, 1f, 0f, 0.498f));

    /// <summary>The disabled look: a half-opaque blue outline over a deeper blue fill.</summary>
    public static readonly SilhouetteLook Disabled = new((0f, 0f, 0.247f, 0.498f), (0.016f, 0.247f, 1f, 0.498f));

    /// <summary>Every group's outline width in pixels: full opacity about a pixel either side of the edge, gone by three.</summary>
    public const float OutlineWidth = 2f;

    /// <summary>The hostile's screen-edge fade's radii: none, so a marked target is as plain at the edge of the view as at its centre.</summary>
    public static readonly (float X, float Y) HostileVignetteRadii = (0f, 0f);

    /// <summary>The other groups' screen-edge fade's radii, across and up, as shares of the screen's height: wide and flat.</summary>
    public static readonly (float X, float Y) VignetteRadii = (1.5f, 0.75f);

    private const RhiTextureFormat MaskFormat = RhiTextureFormat.Rgba8Unorm;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly IRhiShaderModule _maskModule;
    private readonly IRhiPipelineLayout _maskLayout;
    private readonly IRhiRenderPipeline _maskPipeline;
    private readonly IRhiBindGroupLayout _compositeLayout;
    private readonly ShaderBindings _compositeBindings;
    private readonly Dictionary<RhiTextureFormat, FullscreenPipeline> _composites = [];
    private IRhiTexture? _mask;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SilhouettePass"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="retired">Where a mask a resize replaces goes until the frames reading it finish.</param>
    public SilhouettePass(IRhiDevice device, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);
        _device = device;
        _retired = retired;

        // The mask is the selection outline's own vertex stage pushed out by nothing: the body exactly where it was drawn.
        ShaderReflection outline = ShaderCatalog.GetReflection(ShaderModuleId.Outline);
        _maskModule = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Outline, Label: "silhouette.mask"));
        _maskLayout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            OutlinePipelineCache.PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "silhouette.mask"));
        RhiBlendComponent greater = new(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Max);
        _maskPipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _maskLayout,
            new RhiShaderStage(
                _maskModule,
                outline.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(outline, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_maskModule, outline.EntryPointFor(ShaderStage.Fragment).Name),
            OutlinePipelineCache.VertexLayouts(),
            [new RhiColorTargetState(MaskFormat, new RhiBlendState(greater, greater), RhiColorWrite.All)],
            null,
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            "silhouette.mask"));

        ShaderReflection composite = ShaderCatalog.GetReflection(ShaderModuleId.Silhouette);
        _compositeLayout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(composite, RhiShaderStages.Fragment),
            "silhouette.composite"));
        _compositeBindings = new ShaderBindings(device, composite, [_compositeLayout], RhiShaderStages.Fragment, "silhouette.composite");
    }

    /// <summary>Records both passes over a finished picture.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="target">The finished picture, which the highlight is laid over.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="draws">The bodies marked this frame.</param>
    /// <param name="timestamps">Where to time the composite, or null.</param>
    public void Record(
        IRhiCommandEncoder encoder,
        IRhiTextureView target,
        int width,
        int height,
        IReadOnlyList<SilhouetteDraw> draws,
        RhiTimestampWrites? timestamps)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(draws);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (draws.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        IRhiTexture mask = Ensure(width, height);
        Span<float> push = stackalloc float[OutlinePipelineCache.PushDataBytes / sizeof(float)];
        using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(mask.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Label: "silhouette.mask")))
        {
            pass.SetPipeline(_maskPipeline);
            pass.SetViewport(0, 0, width, height, 0f, 1f);
            pass.SetScissor(0, 0, width, height);
            foreach (SilhouetteDraw draw in draws)
            {
                push.Clear();
                draw.Transform.CopyColumnMajorTo(push[..16]);
                push[16 + (int)draw.Group] = 1f;
                push[21] = draw.PoseBlend;
                push[24] = width;
                push[25] = height;
                pass.SetPushData(MemoryMarshal.AsBytes(push));
                pass.SetVertexBuffer(0, draw.Vertices);
                pass.SetVertexBuffer(1, draw.PreviousVertices);
                pass.SetIndexBuffer(draw.Indices, RhiIndexFormat.Uint16);
                foreach (MeshDrawRange range in draw.Ranges)
                {
                    pass.DrawIndexed(range.IndexCount, 1, range.IndexOffset);
                }
            }

            pass.EndPass();
        }

        RhiTextureFormat format = target.Texture.Format;
        Span<float> block = stackalloc float[32];
        Write(block, width, height, format.IsSrgb());
        _compositeBindings.Bind("uMask", mask.View);
        using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(target, RhiLoadOp.Load, RhiStoreOp.Store)],
            Timestamps: timestamps,
            Label: "silhouette.composite")))
        {
            pass.SetViewport(0, 0, width, height, 0f, 1f);
            pass.SetScissor(0, 0, width, height);
            CompositeFor(format).Draw(pass, _compositeBindings.GroupFor(0), MemoryMarshal.AsBytes(block));
            pass.EndPass();
        }
    }

    /// <summary>The composite's push block: the frame and outline width, the screen-edge fades, and each group's fill and outline.</summary>
    /// <param name="block">Thirty-two floats.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="targetEncodes">Whether the target stores sRGB, so blends in linear light and is given linear colours.</param>
    public static void Write(Span<float> block, int width, int height, bool targetEncodes)
    {
        block[0] = width;
        block[1] = height;
        block[2] = 2f / OutlineWidth;
        block[3] = 0f;
        block[4] = Inverse(HostileVignetteRadii.X);
        block[5] = Inverse(HostileVignetteRadii.Y);
        block[6] = Inverse(VignetteRadii.X);
        block[7] = Inverse(VignetteRadii.Y);
        SilhouetteLook[] looks = [Hostile, Team, Disabled];
        for (int group = 0; group < looks.Length; group++)
        {
            Colour(block[(8 + (group * 8))..], looks[group].Fill, targetEncodes);
            Colour(block[(12 + (group * 8))..], looks[group].Outline, targetEncodes);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (FullscreenPipeline pipeline in _composites.Values)
        {
            pipeline.Dispose();
        }

        _composites.Clear();
        _compositeBindings.Dispose();
        _compositeLayout.Dispose();
        _maskPipeline.Dispose();
        _maskLayout.Dispose();
        _maskModule.Dispose();
        _mask?.Dispose();
    }

    // A colour as the target wants it: linear light for one that encodes as it stores, as the display shows it otherwise.
    private static void Colour(Span<float> into, (float R, float G, float B, float A) colour, bool targetEncodes)
    {
        into[0] = targetEncodes ? SrgbToLinear(colour.R) : colour.R;
        into[1] = targetEncodes ? SrgbToLinear(colour.G) : colour.G;
        into[2] = targetEncodes ? SrgbToLinear(colour.B) : colour.B;
        into[3] = colour.A;
    }

    // One over a radius, or nought -- no fade -- for none.
    private static float Inverse(float radius) => radius > 0f ? 1f / radius : 0f;

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private FullscreenPipeline CompositeFor(RhiTextureFormat format)
    {
        if (!_composites.TryGetValue(format, out FullscreenPipeline? pipeline))
        {
            // Premultiplied over, the picture's own alpha left as it was.
            RhiBlendComponent over = new(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add);
            RhiBlendComponent keep = new(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add);
            pipeline = new FullscreenPipeline(_device, ShaderModuleId.Silhouette, _compositeLayout, format, blend: new RhiBlendState(over, keep));
            _composites[format] = pipeline;
        }

        return pipeline;
    }

    private IRhiTexture Ensure(int width, int height)
    {
        if (_mask is { } mask && mask.Width == width && mask.Height == height)
        {
            return mask;
        }

        if (_mask is not null)
        {
            _retired.Retire(_mask);
        }

        _mask = _device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            MaskFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
            Label: "silhouette.mask"));
        return _mask;
    }
}

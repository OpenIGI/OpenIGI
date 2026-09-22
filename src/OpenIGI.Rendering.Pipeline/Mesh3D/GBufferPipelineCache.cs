using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The render state that identifies one G-buffer variant.</summary>
/// <param name="Depth">How the draw interacts with the depth buffer.</param>
/// <param name="Cull">Which triangle facings the draw discards.</param>
/// <param name="Blend">How the draw combines with what a pass before it wrote.</param>
/// <param name="AlphaReference">The cut-out threshold out of 255, baked into the variant.</param>
public readonly record struct GBufferPipelineKey(
    DepthState Depth,
    CullMode Cull,
    BlendMode Blend,
    byte AlphaReference);

/// <summary>
/// The geometry pipeline: opaque meshes into the four G-buffer targets, one per depth-and-cull state,
/// built on demand and kept.
/// </summary>
/// <remarks>
/// <para>
/// Geometry is opaque — translucency belongs to the forward pass — so blending does not vary the way it
/// does there. What varies is depth behaviour, culling, the cut-out threshold, and the two ways the
/// terrain's material passes combine with the pass beneath them.
/// </para>
/// <para>
/// The material group is the same shape the forward path binds, which is why the two had to move
/// together: a group is built per texture triple and used by whichever pipeline draws it, so one
/// widened layout would be a binding every world draw in the frame carried.
/// </para>
/// </remarks>
public sealed class GBufferPipelineCache : IDisposable
{
    /// <summary>Where the G-buffer's base colour is attached, in the order the pass declares.</summary>
    public const int BaseColourTarget = 0;

    /// <summary>Where the G-buffer's baked light is attached, in the same order.</summary>
    public const int BakedTarget = 3;

    /// <summary>How many targets the geometry pass writes.</summary>
    public const int TargetCount = 4;

    /// <summary>How many bytes of push data the G-buffer shader reads.</summary>
    public const int PushDataBytes = 128;

    private readonly IRhiDevice _device;
    private readonly IReadOnlyList<RhiTextureFormat> _targetFormats;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly ShaderReflection _reflection;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private readonly Dictionary<GBufferPipelineKey, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GBufferPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group, shared with the forward mesh path.</param>
    /// <param name="surfaceLayout">The per-frame surface block's layout, bound as group one.</param>
    /// <param name="targetFormats">The formats of the four targets, in attachment order.</param>
    /// <param name="depthFormat">The format of the depth attachment.</param>
    /// <exception cref="ArgumentException">The wrong number of target formats was given.</exception>
    public GBufferPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IRhiBindGroupLayout surfaceLayout,
        IReadOnlyList<RhiTextureFormat> targetFormats,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(surfaceLayout);
        ArgumentNullException.ThrowIfNull(targetFormats);

        if (targetFormats.Count != TargetCount)
        {
            throw new ArgumentException(
                $"The geometry pass writes exactly {TargetCount} targets.",
                nameof(targetFormats));
        }

        _device = device;
        _targetFormats = targetFormats;
        _depthFormat = depthFormat;

        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.GBuffer);
        _vertexEntryPoint = _reflection.EntryPointFor(ShaderStage.Vertex).Name;
        _fragmentEntryPoint = _reflection.EntryPointFor(ShaderStage.Fragment).Name;

        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.GBuffer, Label: "GBuffer"));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, surfaceLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "GBuffer"));
    }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets how many variants have been built.</summary>
    public int Count => _pipelines.Count;

    /// <summary>Gets the pipeline for a draw's state, building it on first use.</summary>
    /// <param name="depth">The draw's depth test and write.</param>
    /// <param name="cull">Which facing the draw culls.</param>
    /// <param name="blend">
    /// How the draw combines with what is already there. Everything but the terrain's two material
    /// modes is drawn opaque.
    /// </param>
    /// <param name="alphaReference">
    /// How much texture alpha a cut-out fragment needs to survive, out of 255. <b>The shadow pass has to
    /// be asked for the same number</b>, or a surface shadows itself along the edge where the two tests
    /// disagree.
    /// </param>
    /// <returns>The matching pipeline.</returns>
    public IRhiRenderPipeline ForState(
        DepthState depth,
        CullMode cull,
        BlendMode blend = BlendMode.Opaque,
        byte alphaReference = Mesh3DConstants.DefaultAlphaReference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        GBufferPipelineKey key = new(depth, cull, blend, alphaReference);

        if (_pipelines.TryGetValue(key, out IRhiRenderPipeline? existing))
        {
            return existing;
        }

        IRhiRenderPipeline pipeline = Create(key);
        _pipelines[key] = pipeline;
        return pipeline;
    }

    /// <summary>The two vertex streams the G-buffer shader reads.</summary>
    /// <returns>The current pose at slot zero, the previous pose at slot one.</returns>
    /// <remarks>
    /// The same shape as the forward path's, with the surface normal added: the geometry pass is the one
    /// that writes it. A static mesh reports one buffer as both streams, so the delta the vertex stage
    /// forms is zero and nothing has to say which kind of mesh it is.
    /// </remarks>
    public static IReadOnlyList<RhiVertexBufferLayout> VertexLayouts() =>
    [
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(0, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(1, RhiVertexFormat.Float32x3, 12),
                new RhiVertexElement(2, RhiVertexFormat.Float32x2, 24),
                new RhiVertexElement(3, RhiVertexFormat.Float32x2, 32),
                new RhiVertexElement(4, RhiVertexFormat.Unorm8x4, Vertex3D.ColourOffset),
                new RhiVertexElement(5, RhiVertexFormat.Unorm8x4, Vertex3D.SurfaceNormalOffset),
            ]),
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(6, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(7, RhiVertexFormat.Float32x3, 12),
            ]),
    ];

    /// <summary>How the four targets are written for one blend mode.</summary>
    /// <param name="blend">How the draw combines with what is already there.</param>
    /// <param name="formats">The formats of the four targets, in attachment order.</param>
    /// <returns>The four target states.</returns>
    /// <remarks>
    /// <para>
    /// Additive and Modulate2X are the ground's two ways of combining with a pass before them, and on a
    /// deferred path they do <b>not</b> land in the same place.
    /// </para>
    /// <para>
    /// Additive is one more material adding its share of the ground's colour, so it belongs on the base
    /// colour with the base pass it is finishing. Modulate2X is the tile's bake, and a bake is not a
    /// colour: the base colour attachment holds an albedo, and a bake multiplied into an albedo is lit a
    /// second time by whatever lights the frame. So it goes to the baked attachment, which the base pass
    /// left neutral and which the lighting reads as irradiance — and the doubling MODULATE2X means falls
    /// out of the blend for free, because src * dst + dst * src over a neutral destination is twice the
    /// bake.
    /// </para>
    /// <para>
    /// Either way one attachment's colour and nothing else: not its alpha, and none of the three the
    /// opaque pass beneath it wrote for the same surface.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RhiColorTargetState> TargetsFor(
        BlendMode blend,
        IReadOnlyList<RhiTextureFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        if (blend is not (BlendMode.Additive or BlendMode.Modulate2X))
        {
            return [.. formats.Select(format => new RhiColorTargetState(format))];
        }

        bool modulate = blend is BlendMode.Modulate2X;
        int written = modulate ? BakedTarget : BaseColourTarget;

        RhiBlendState combine = new(
            new RhiBlendComponent(
                modulate ? RhiBlendFactor.Destination : RhiBlendFactor.One,
                modulate ? RhiBlendFactor.Source : RhiBlendFactor.One,
                RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add));

        List<RhiColorTargetState> targets = [];

        for (int i = 0; i < formats.Count; i++)
        {
            targets.Add(i == written
                ? new RhiColorTargetState(
                    formats[i],
                    combine,
                    RhiColorWrite.Red | RhiColorWrite.Green | RhiColorWrite.Blue)
                : new RhiColorTargetState(formats[i], Blend: null, RhiColorWrite.None));
        }

        return targets;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiRenderPipeline pipeline in _pipelines.Values)
        {
            pipeline.Dispose();
        }

        _pipelines.Clear();
        Layout.Dispose();
        _module.Dispose();
    }

    /// <summary>The target's own constants, kept to what this module declares.</summary>
    private IReadOnlyDictionary<int, RhiConstant> TargetConstants() =>
        ShaderConstants.Declared(_reflection, Mesh3DConstants.TargetConstants);

    private IRhiRenderPipeline Create(GBufferPipelineKey key)
    {
        // Only the fragment stage tests the cut-out, so only it is specialised -- unlike the forward
        // shader, whose vertex stage reads the reflection constant too.
        IReadOnlyDictionary<int, RhiConstant> constants = ShaderConstants.Declared(
            _reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.AlphaReference] = RhiConstant.Of(key.AlphaReference / 255f),
            });

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, _vertexEntryPoint, TargetConstants()),
            new RhiShaderStage(_module, _fragmentEntryPoint, constants),
            VertexLayouts(),
            TargetsFor(key.Blend, _targetFormats),
            Mesh3DPipelineCache.DepthStateFor(key.Depth, _depthFormat),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, Mesh3DPipelineCache.CullFor(key.Cull)),
            SampleCount: 1,
            $"GBuffer-{key.Depth}-{key.Cull}-{key.Blend}-{key.AlphaReference}"));
    }
}

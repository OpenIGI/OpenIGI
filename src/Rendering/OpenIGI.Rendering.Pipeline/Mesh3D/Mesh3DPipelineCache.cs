using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// The world pipeline: one graphics pipeline per distinct render state of the 3D forward draw, built on
/// demand and kept, over a layout that binds a material and an environment cube.
/// </summary>
/// <remarks>
/// <para>
/// Depth behaviour, face culling and blending are baked into a pipeline, so each combination the world
/// draws with is its own object. Rather than enumerate the whole product up front — most combinations
/// never occur — one is built the first time its state is asked for. In practice the world uses a
/// handful.
/// </para>
/// <para>
/// Two vertex streams: the pose the mesh holds now and the pose it held before. The first-person rig is
/// skinned like a body and drawn through here, so it blends between the two as bodies do.
/// </para>
/// </remarks>
public sealed class Mesh3DPipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the forward mesh shader reads.</summary>
    /// <remarks>
    /// Exactly the 128 the hardware guarantees, and every float is spoken for: the model-view fills
    /// sixteen, the normal matrix nine, and the rest carry the marker, the exposure, the pose blend, the
    /// key colour, the material and the lightmap scale. Nothing more fits, which is why the cut-out
    /// threshold, the fog and the sun are constants instead.
    /// </remarks>
    public const int PushDataBytes = sizeof(float) * 32;

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly ShaderReflection _reflection;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    // One set of variants per sun they were built for (and per fog, for a module that bakes it in), the
    // oldest dropped past a handful.
    private const int MaxEnvironments = 4;

    private readonly Dictionary<Environment, Dictionary<Mesh3DPipelineKey, IRhiRenderPipeline>> _sets = [];
    private readonly List<Environment> _order = [];
    private readonly RetiredResources? _retired;
    private readonly bool _bakesFog;
    private Environment _environment;
    private Dictionary<Mesh3DPipelineKey, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="Mesh3DPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">
    /// The material group, which is <b>shared</b> with the geometry, shadow and card paths and
    /// therefore handed in rather than made here. A group is built per texture triple and used by
    /// whichever pipeline draws it, so a second layout of the same shape would mean a second group per
    /// triple in every frame — and one of a different shape would mean the two could silently disagree.
    /// </param>
    /// <param name="cubeLayout">The environment cube's group, which only this path binds.</param>
    /// <param name="targetFormat">The format of the colour attachment they draw into.</param>
    /// <param name="depthFormat">The format of the depth attachment.</param>
    /// <param name="retired">
    /// Where discarded variants wait for the frames that bound them, or null to free them at once -- which
    /// only a caller with no frame in flight can afford.
    /// </param>
    public Mesh3DPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IRhiBindGroupLayout cubeLayout,
        RhiTextureFormat targetFormat,
        RhiTextureFormat depthFormat,
        RetiredResources? retired = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(cubeLayout);

        _device = device;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;
        _retired = retired;
        _sets[_environment] = _pipelines;
        _order.Add(_environment);

        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.Mesh3D);

        // The fog became push data; a module that still declared it as a constant would bake it in.
        _bakesFog = ShaderConstants.Declared(
            _reflection,
            new Dictionary<int, RhiConstant> { [Mesh3DConstants.FogDensity] = RhiConstant.Of(0f) }).Count > 0;
        _vertexEntryPoint = _reflection.EntryPointFor(ShaderStage.Vertex).Name;
        _fragmentEntryPoint = _reflection.EntryPointFor(ShaderStage.Fragment).Name;

        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Mesh3D, Label: "Mesh3D"));

        MaterialLayout = materialLayout;
        CubeLayout = cubeLayout;

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, cubeLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "Mesh3D"));
    }

    /// <summary>Gets the layout binding the diffuse, lightmap and detail textures.</summary>
    /// <remarks>Owned by the caller that handed it in, and shared with the other mesh paths.</remarks>
    public IRhiBindGroupLayout MaterialLayout { get; }

    /// <summary>Gets the layout binding the environment cube a reflective draw samples.</summary>
    /// <remarks>
    /// A second group rather than more slots on the material's, because that one is shared with the
    /// shadow and geometry paths: a group is built per texture pairing and reused by whichever pipeline
    /// draws it, so widening it would make every world draw in the frame carry a binding only water
    /// reads.
    /// </remarks>
    public IRhiBindGroupLayout CubeLayout { get; }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets how many variants have been built.</summary>
    public int Count => _pipelines.Count;

    /// <summary>Sets the scene's fog, drawing with the variants built for it.</summary>
    /// <param name="colour">The colour a surface tends to with distance.</param>
    /// <param name="density">The extinction per metre; zero is no fog.</param>
    /// <remarks>
    /// The shader reads the fog as push data now, so a fog changes no variant: the retail path sets the
    /// terrain's fog and the objects' alternately within a frame, and each of those used to throw every
    /// variant away and build it again. Only a module that still bakes it in keys its variants on it.
    /// </remarks>
    public void SetFog(Vector3f colour, float density) =>
        Select(_bakesFog ? _environment with { FogColour = colour, FogDensity = density } : _environment);

    /// <summary>Sets the fixed-function sun's colours, drawing with the variants built for them.</summary>
    /// <param name="ambient">The flat light an unlightmapped vertex receives.</param>
    /// <param name="colour">The sun's diffuse colour.</param>
    /// <remarks>
    /// Baked in -- there is no push room -- so each sun has variants of its own. The sets already built
    /// are kept, so returning to a sun costs nothing, and only past a handful is the oldest retired,
    /// never freed, since the frame being recorded may have bound it.
    /// </remarks>
    public void SetSun(Vector3f ambient, Vector3f colour) =>
        Select(_environment with { SunAmbient = ambient, SunColour = colour });

    /// <summary>Gets the pipeline for one render state, building it on first use.</summary>
    /// <param name="depth">How the draw interacts with the depth buffer.</param>
    /// <param name="cull">Which triangle facings the draw discards.</param>
    /// <param name="blend">How fragments combine with the target.</param>
    /// <param name="alphaReference">The cut-out threshold out of 255, baked into the variant.</param>
    /// <param name="reflective">Whether the variant samples the bound environment cube.</param>
    /// <returns>The matching pipeline.</returns>
    public IRhiRenderPipeline ForState(
        DepthState depth,
        CullMode cull,
        BlendMode blend,
        byte alphaReference = Mesh3DConstants.DefaultAlphaReference,
        bool reflective = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mesh3DPipelineKey key = new(depth, cull, blend, alphaReference, reflective);

        if (_pipelines.TryGetValue(key, out IRhiRenderPipeline? existing))
        {
            return existing;
        }

        IRhiRenderPipeline pipeline = Create(key);
        _pipelines[key] = pipeline;
        return pipeline;
    }

    /// <summary>The two vertex streams the forward mesh shader reads.</summary>
    /// <returns>The current pose at slot zero, the previous pose at slot one.</returns>
    /// <remarks>
    /// The previous stream carries only a position and a normal: everything else about a vertex — its
    /// texture coordinates, its colour — is the same in both poses, so reading it twice would be two
    /// fetches for one value.
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
            ]),
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(5, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(6, RhiVertexFormat.Float32x3, 12),
            ]),
    ];

    /// <summary>The layout binding a draw's material: three textures, each with its own sampler.</summary>
    /// <param name="device">The device to create it on.</param>
    /// <returns>The layout.</returns>
    /// <remarks>
    /// The diffuse at zero, the baked lightmap at two and the derived surface detail at four, each with
    /// its sampler in the slot after it. The detail pair is declared although the forward shader never
    /// samples it: only the geometry pass reads the detail, and reserving its slots keeps one shape for
    /// a group that several pipelines share.
    /// </remarks>
    public static IRhiBindGroupLayout CreateMaterialLayout(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        List<RhiBindGroupLayoutEntry> entries = [];

        for (int i = 0; i < 6; i++)
        {
            entries.Add(new RhiBindGroupLayoutEntry(
                i,
                i % 2 == 0 ? RhiBindingType.SampledTexture : RhiBindingType.Sampler,
                RhiShaderStages.Fragment));
        }

        return device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(entries, "mesh3d-material"));
    }

    /// <summary>The layout binding the environment cube.</summary>
    /// <param name="device">The device to create it on.</param>
    /// <returns>The layout.</returns>
    public static IRhiBindGroupLayout CreateCubeLayout(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [
                new RhiBindGroupLayoutEntry(
                    0,
                    RhiBindingType.SampledTexture,
                    RhiShaderStages.Fragment,
                    ViewDimension: RhiTextureViewDimension.Cube),
                new RhiBindGroupLayoutEntry(1, RhiBindingType.Sampler, RhiShaderStages.Fragment),
            ],
            "mesh3d-cube"));
    }

    /// <summary>How a depth state is stated to the RHI.</summary>
    /// <param name="state">The state.</param>
    /// <param name="format">The depth format that will be attached.</param>
    /// <returns>The depth state, or null for a pipeline that neither tests nor writes depth.</returns>
    /// <remarks>
    /// Reverse-Z: nearer geometry has the greater depth value, so the test keeps the greater one.
    /// </remarks>
    public static RhiDepthStencilState? DepthStateFor(DepthState state, RhiTextureFormat format) =>
        state is DepthState.Disabled
            ? new RhiDepthStencilState(format, DepthWrite: false, RhiCompare.Always)
            : new RhiDepthStencilState(
                format,
                state is DepthState.TestAndWrite,
                RhiCompare.GreaterEqual);

    /// <summary>How a blend mode is stated to the RHI, or null for one that writes outright.</summary>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The blend state.</returns>
    public static RhiBlendState? BlendFor(BlendMode blend) => blend switch
    {
        BlendMode.Opaque => null,
        BlendMode.AlphaBlend => new RhiBlendState(
            RhiBlendComponent.AlphaBlend,
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add)),
        BlendMode.Additive => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.SourceAlpha, RhiBlendFactor.One, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add)),

        // src*dst + dst*src. The alpha is left alone: this pass multiplies a colour in and has no
        // coverage of its own.
        BlendMode.Modulate2X => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.Destination, RhiBlendFactor.Source, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add)),
        BlendMode.SourcePlusAttenuatedDestination => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.SourceAlpha, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.SourceAlpha, RhiBlendOperation.Add)),

        // ZERO, SRCCOLOR: dst*src. The alpha is left alone, as the doubled one leaves it.
        BlendMode.Modulate => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.Source, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add)),

        // ZERO, INVSRCCOLOR: dst*(1-src), the alpha left alone.
        BlendMode.Darken => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.OneMinusSource, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add)),

        // ONE, ONE: src + dst, the source's alpha ignored.
        BlendMode.AddColour => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add)),
        _ => throw new ArgumentOutOfRangeException(nameof(blend), blend, "There is no world pipeline for that blend mode."),
    };

    /// <summary>Which facing a cull mode discards.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The RHI's cull mode.</returns>
    /// <remarks>
    /// The front face is counter-clockwise, as OpenGL's is, so the rule maps exactly as GL's does: cull
    /// front for Clockwise, back for CounterClockwise.
    /// </remarks>
    public static RhiCullMode CullFor(CullMode mode) => mode switch
    {
        CullMode.Clockwise => RhiCullMode.Front,
        CullMode.CounterClockwise => RhiCullMode.Back,
        _ => RhiCullMode.None,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The two group layouts are the caller's; every mesh path shares them and none of them owns
        // them, which is the point of their being handed in.
        foreach (Dictionary<Mesh3DPipelineKey, IRhiRenderPipeline> set in _sets.Values)
        {
            foreach (IRhiRenderPipeline pipeline in set.Values)
            {
                pipeline.Dispose();
            }
        }

        _sets.Clear();
        _order.Clear();
        Layout.Dispose();
        _module.Dispose();
    }

    private IRhiRenderPipeline Create(Mesh3DPipelineKey key)
    {
        // The vertex stage declares the reflection constant as well -- it is what builds the direction --
        // and a constant supplied to one stage and not the other is two shaders that disagree about
        // whether the varying between them means anything.
        // Kept to what the module declares: the fog colour became push data and its constants stayed
        // behind, which Vulkan ignores in silence and WebGPU refuses outright.
        IReadOnlyDictionary<int, RhiConstant> constants = ShaderConstants.Declared(
            _reflection,
            Mesh3DConstants.Describe(
                key.AlphaReference,
                key.Reflective,
                _environment.FogColour,
                _environment.FogDensity,
                _environment.SunAmbient,
                _environment.SunColour,
                _targetFormat.IsSrgb(),
                _targetFormat is RhiTextureFormat.Rgba16Float or RhiTextureFormat.Rgba32Float));

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, _vertexEntryPoint, constants),
            new RhiShaderStage(_module, _fragmentEntryPoint, constants),
            VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, BlendFor(key.Blend))],
            DepthStateFor(key.Depth, _depthFormat),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, CullFor(key.Cull)),
            SampleCount: 1,
            $"Mesh3D-{key.Depth}-{key.Cull}-{key.Blend}-{key.AlphaReference}{(key.Reflective ? "-reflective" : string.Empty)}"));
    }

    // Makes an environment the one variants are drawn with, keeping the sets already built.
    private void Select(Environment environment)
    {
        if (environment == _environment)
        {
            return;
        }

        _environment = environment;

        if (_sets.TryGetValue(environment, out Dictionary<Mesh3DPipelineKey, IRhiRenderPipeline>? existing))
        {
            _pipelines = existing;
            _order.Remove(environment);
            _order.Add(environment);
            return;
        }

        _pipelines = [];
        _sets[environment] = _pipelines;
        _order.Add(environment);

        if (_order.Count > MaxEnvironments)
        {
            Environment oldest = _order[0];
            _order.RemoveAt(0);
            Discard(_sets[oldest]);
            _sets.Remove(oldest);
        }
    }

    // Freed only once no frame can still be drawing with them: a pipeline destroyed while a command
    // buffer that bound it is recording invalidates that buffer, and submitting it loses the device --
    // which is what switching the menu to the vanilla preset did.
    private void Discard(Dictionary<Mesh3DPipelineKey, IRhiRenderPipeline> set)
    {
        foreach (IRhiRenderPipeline pipeline in set.Values)
        {
            if (_retired is { } retired)
            {
                retired.Retire(pipeline);
            }
            else
            {
                pipeline.Dispose();
            }
        }

        set.Clear();
    }

    /// <summary>The fog and the sun a set of variants was built with.</summary>
    private readonly record struct Environment(
        Vector3f FogColour,
        float FogDensity,
        Vector3f SunAmbient,
        Vector3f SunColour);
}

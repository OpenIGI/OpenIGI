using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// The shadow pipeline: casters into a depth map and nothing else, one per cut-out threshold.
/// </summary>
/// <remarks>
/// <para>
/// A depth-only pass, so there are no colour targets at all and the only state that varies is the
/// threshold a cut-out fragment must clear. That it varies at all is the point: <b>the shadow pass must
/// be asked for the same number the G-buffer was.</b> A pass that cuts on less keeps casters the frame
/// never draws; one that cuts on more drops shadows from things you can see. Either way the two
/// disagree and the ground goes patchy along the edge where they do.
/// </para>
/// <para>
/// It binds the same material group the other two paths do, although it samples only the diffuse: a
/// group is built per texture triple and used by whichever pipeline draws it, and a second shape here
/// would mean a second group per triple for the sake of one binding.
/// </para>
/// </remarks>
public sealed class ShadowPipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the shadow shader reads.</summary>
    public const int PushDataBytes = sizeof(float) * 24;

    /// <summary>The constant part of the depth bias, in the target's own units.</summary>
    /// <remarks>
    /// With the slope-scaled part, this is what pushes a caster's stored depth away from the light so
    /// that a lit surface does not read as its own shadow.
    /// </remarks>
    public const float DepthBiasConstant = 1.25f;

    /// <summary>The part of the depth bias proportional to the surface's slope.</summary>
    public const float DepthBiasSlopeScale = 2.75f;

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private readonly Dictionary<byte, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group, shared with the other mesh paths.</param>
    /// <param name="depthFormat">The format of the shadow map.</param>
    public ShadowPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);

        _device = device;
        _depthFormat = depthFormat;

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Shadow);
        _vertexEntryPoint = reflection.EntryPointFor(ShaderStage.Vertex).Name;
        _fragmentEntryPoint = reflection.EntryPointFor(ShaderStage.Fragment).Name;

        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Shadow, Label: "Shadow"));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "Shadow"));
    }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets how many variants have been built.</summary>
    public int Count => _pipelines.Count;

    /// <summary>Gets the pipeline that cuts out at one threshold, building it on first use.</summary>
    /// <param name="alphaReference">The threshold out of 255.</param>
    /// <returns>The matching pipeline.</returns>
    /// <remarks>
    /// <b>Must be asked for the same number the G-buffer was</b>, for the reason this class's own
    /// summary gives.
    /// </remarks>
    public IRhiRenderPipeline ForAlphaReference(byte alphaReference = Mesh3DConstants.DefaultAlphaReference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pipelines.TryGetValue(alphaReference, out IRhiRenderPipeline? existing))
        {
            return existing;
        }

        IRhiRenderPipeline pipeline = Create(alphaReference);
        _pipelines[alphaReference] = pipeline;
        return pipeline;
    }

    /// <summary>The two vertex streams the shadow shader reads.</summary>
    /// <returns>The current pose at slot zero, the previous pose's position at slot one.</returns>
    /// <remarks>
    /// The locations are the forward path's, not a fresh run: the same Slang source declares them, and a
    /// depth-only pass simply leaves out what it does not need. It takes the terrain's morph offset and
    /// the texture offset that travels with it, because the ground must cast the shape it is drawn as,
    /// and only the previous pose's position — a depth-only pass needs no normal from it.
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
            [new RhiVertexElement(5, RhiVertexFormat.Float32x3, 0)]),
    ];

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

    private IRhiRenderPipeline Create(byte alphaReference) =>
        _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, _vertexEntryPoint, Mesh3DConstants.TargetConstants),
            new RhiShaderStage(
                _module,
                _fragmentEntryPoint,
                new Dictionary<int, RhiConstant>
                {
                    [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                    [Mesh3DConstants.AlphaReference] = RhiConstant.Of(alphaReference / 255f),
                }),
            VertexLayouts(),
            ColorTargets: [],
            new RhiDepthStencilState(
                _depthFormat,
                DepthWrite: true,

                // The shadow map is drawn the ordinary way round, not reverse-Z like the scene: it is
                // its own target with its own projection, and nothing samples it expecting otherwise.
                RhiCompare.LessEqual,
                DepthBiasConstant,
                DepthBiasSlopeScale),

            // No culling. The world's meshes are not reliably wound one way, and the bias handles the
            // acne culling would.
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            $"Shadow-{alphaReference}"));
}

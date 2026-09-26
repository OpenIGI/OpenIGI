using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The pipeline that draws a model into a page of the card atlas.</summary>
/// <remarks>
/// <para>
/// One pipeline, no variants. Every card capture wants the same state: opaque, depth-tested so the
/// nearest surface wins, and no face culling at all — the port's meshes do not wind consistently, and a
/// card that dropped half its triangles would leave holes in the atlas that later read as empty space.
/// </para>
/// <para>
/// It binds the same material group as the rest of the mesh path, which is why it moved with them.
/// </para>
/// </remarks>
public sealed class CardPipeline : IDisposable
{
    /// <summary>How many bytes of push data the card shader reads.</summary>
    public const int PushDataBytes = 128;

    /// <summary>How many targets a card capture writes.</summary>
    public const int TargetCount = 4;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="CardPipeline"/> class.</summary>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="materialLayout">The material group, shared with the other mesh paths.</param>
    /// <param name="targetFormats">The formats of the atlas's pages, in attachment order.</param>
    /// <param name="depthFormat">The format of the capture's depth buffer.</param>
    /// <exception cref="ArgumentException">The wrong number of target formats was given.</exception>
    public CardPipeline(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IReadOnlyList<RhiTextureFormat> targetFormats,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(targetFormats);

        if (targetFormats.Count != TargetCount)
        {
            throw new ArgumentException(
                $"A card capture writes exactly {TargetCount} targets.",
                nameof(targetFormats));
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Card);

        Module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Card, Label: "Card"));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "Card"));

        Pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(
                Module,
                reflection.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(reflection, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(
                Module,
                reflection.EntryPointFor(ShaderStage.Fragment).Name,

                // Every texture here is an sRGB image, so it arrives linear already and the capture must
                // not decode it again.
                ShaderConstants.Declared(
                    reflection,
                    new Dictionary<int, RhiConstant> { [Mesh3DConstants.SamplerDecodes] = RhiConstant.Of(1) })),
            VertexLayouts(),
            [.. targetFormats.Select(format => new RhiColorTargetState(format))],

            // A card's depth runs the ordinary way rather than reversed, so the nearest surface is the
            // one with the smaller value. It is its own target with its own projection, and nothing
            // samples it expecting the scene's convention.
            new RhiDepthStencilState(depthFormat, DepthWrite: true, RhiCompare.LessEqual),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            "Card"));
    }

    /// <summary>Gets the compiled module both stages come from.</summary>
    public IRhiShaderModule Module { get; }

    /// <summary>Gets the pipeline layout.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the pipeline.</summary>
    public IRhiRenderPipeline Pipeline { get; }

    /// <summary>The one vertex stream a card capture reads.</summary>
    /// <returns>The layout.</returns>
    /// <remarks>
    /// One stream, not two: a card is a photograph of a model where it stands, so there is no previous
    /// pose to form a delta against and nothing in the atlas moves.
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
    ];

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pipeline.Dispose();
        Layout.Dispose();
        Module.Dispose();
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>I.G.I. 2's water: one pipeline per facing culled and per level light it is built for.</summary>
/// <remarks>
/// <para>
/// The states are <c>WaterRender_SetGenericState</c> and <c>WaterRender_SetBlendState</c> above quality nought:
/// depth tested less-or-equal and written, blended <c>ONE</c> and <c>SRCALPHA</c>, and the facing culled that the
/// eye is not on.
/// </para>
/// <para>
/// The level's sun and ambient are constants of the pipeline: the presenter reads them once a level, as the
/// world pipeline bakes its own sun.
/// </para>
/// </remarks>
public sealed class WaterPipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the water shader reads: its transform and four vectors.</summary>
    public const int PushDataBytes = sizeof(float) * 32;

    private const int SunX = 1;
    private const int SunY = 2;
    private const int SunZ = 3;

    // A level is lit one way, so the cache holds a handful; the oldest goes past that.
    private const int MaxPipelines = 8;

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly bool _linearScene;
    private readonly IRhiShaderModule _module;
    private readonly ShaderReflection _reflection;
    private readonly RetiredResources? _retired;
    private readonly Dictionary<Key, IRhiRenderPipeline> _pipelines = [];
    private readonly List<Key> _order = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WaterPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group, shared with the other mesh paths.</param>
    /// <param name="cubeLayout">The cube group the reflection is bound in.</param>
    /// <param name="targetFormat">The format drawn into.</param>
    /// <param name="depthFormat">The depth's format.</param>
    /// <param name="linearScene">Whether the target holds the scene in linear light before exposure.</param>
    /// <param name="retired">Where a pipeline dropped while frames may still use it waits.</param>
    public WaterPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IRhiBindGroupLayout cubeLayout,
        RhiTextureFormat targetFormat,
        RhiTextureFormat depthFormat,
        bool linearScene,
        RetiredResources? retired = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(cubeLayout);

        _device = device;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;
        _linearScene = linearScene;
        _retired = retired;
        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.Water);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Water, Label: nameof(ShaderModuleId.Water)));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, cubeLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.Water)));
    }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the pipeline for a sheet, building it on first use.</summary>
    /// <param name="shading">What the sheet is lit by, and which side of it the eye is on.</param>
    /// <returns>The pipeline.</returns>
    public IRhiRenderPipeline For(in WaterShading shading)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Key key = new(shading.IsAboveWater, shading.SunDirection, shading.SunColour, shading.Ambient);
        if (_pipelines.TryGetValue(key, out IRhiRenderPipeline? pipeline))
        {
            return pipeline;
        }

        if (_order.Count >= MaxPipelines)
        {
            Key oldest = _order[0];
            _order.RemoveAt(0);
            if (_pipelines.Remove(oldest, out IRhiRenderPipeline? dropped))
            {
                if (_retired is { } retired)
                {
                    retired.Retire(dropped);
                }
                else
                {
                    dropped.Dispose();
                }
            }
        }

        pipeline = Create(key);
        _pipelines[key] = pipeline;
        _order.Add(key);
        return pipeline;
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
        _order.Clear();
        Layout.Dispose();
        _module.Dispose();
    }

    private IRhiRenderPipeline Create(Key key)
    {
        IReadOnlyDictionary<int, RhiConstant> constants = ShaderConstants.Declared(
            _reflection,
            new Dictionary<int, RhiConstant>
            {
                [SunX] = RhiConstant.Of(key.Sun.X),
                [SunY] = RhiConstant.Of(key.Sun.Y),
                [SunZ] = RhiConstant.Of(key.Sun.Z),
                [Mesh3DConstants.SunAmbientRed] = RhiConstant.Of(key.Ambient.X),
                [Mesh3DConstants.SunAmbientGreen] = RhiConstant.Of(key.Ambient.Y),
                [Mesh3DConstants.SunAmbientBlue] = RhiConstant.Of(key.Ambient.Z),
                [Mesh3DConstants.SunColourRed] = RhiConstant.Of(key.Colour.X),
                [Mesh3DConstants.SunColourGreen] = RhiConstant.Of(key.Colour.Y),
                [Mesh3DConstants.SunColourBlue] = RhiConstant.Of(key.Colour.Z),
                [Mesh3DConstants.LinearScene] = RhiConstant.Of(_linearScene ? 1 : 0),
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.SamplerDecodes] = RhiConstant.Of(_targetFormat.IsSrgb() ? 0 : 1),
            });

        // WaterRender_SetGenericState culls the side of the sheet the eye is not on: the underside from above, the
        // top from below. The sheet's triangles face up, and in this pipeline's frame an up-facing triangle seen
        // from above winds the way CounterClockwise keeps -- the opposite of the original's CULL_CW, whose screen
        // has its Y the other way up.
        CullMode cull = key.IsAboveWater ? CullMode.CounterClockwise : CullMode.Clockwise;

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Vertex).Name, constants),
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Fragment).Name, constants),
            Mesh3DPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, Mesh3DPipelineCache.BlendFor(BlendMode.SourcePlusAttenuatedDestination))],
            Mesh3DPipelineCache.DepthStateFor(DepthState.TestAndWrite, _depthFormat),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, Mesh3DPipelineCache.CullFor(cull)),
            SampleCount: 1,
            $"Water-{(key.IsAboveWater ? "above" : "below")}"));
    }

    private readonly record struct Key(bool IsAboveWater, Vector3f Sun, Vector3f Colour, Vector3f Ambient);
}

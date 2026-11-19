using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>One body the thermal sensor sees, as it was drawn.</summary>
/// <param name="Body">The body.</param>
/// <param name="DepthRow">Its model-view transform's third row: a point's distance ahead of the eye, a normal's turn towards it.</param>
/// <param name="Heat">How hot it is: one for the living, less as a body cools.</param>
internal readonly record struct ThermalBodyDraw(PosedBody Body, System.Numerics.Vector4 DepthRow, float Heat);

/// <summary>What the thermal sensor's picture is made against beyond its own settings.</summary>
/// <param name="Width">The picture's width.</param>
/// <param name="Height">Its height.</param>
/// <param name="DepthWidth">The scene depth's width, smaller than the picture's after the scene is upscaled.</param>
/// <param name="DepthHeight">Its height.</param>
/// <param name="Unprojection">The projection's scales and its near and far planes, in metres.</param>
/// <param name="Exposure">The exposure the resolve will apply to the picture: the sensor's gain reads the scene by it.</param>
/// <param name="Frame">The frame's number, which moves the sensor's grain.</param>
internal readonly record struct ThermalFrame(
    int Width,
    int Height,
    int DepthWidth,
    int DepthHeight,
    DepthUnprojection Unprojection,
    float Exposure,
    int Frame);

/// <summary>The remastered thermal sensor: the bodies it sees drawn into a heat mask, and the lit scene re-imaged as heat.</summary>
/// <remarks>
/// <para>
/// Two passes. The bodies are drawn, each in the pose it was drawn in, into a heat mask the size of the picture
/// (<c>World/ThermalMask.slang</c>): their surface heat, their flat heat and how near they are, every channel by the greater,
/// with no depth test. Then one compute pass (<c>Post/Thermal.slang</c>) reads the lit scene, its depth and the mask and writes
/// the sensor's picture, which goes on to the bloom and the tone map in the scene's place.
/// </para>
/// <para>
/// After the temporal resolve, so the history never holds the sensor's picture and nothing ghosts as it is raised or lowered.
/// </para>
/// </remarks>
internal sealed class ThermalPass : IDisposable
{
    /// <summary>How many floats the sensor's parameter block holds.</summary>
    public const int ParameterFloats = 20;

    /// <summary>How many floats of push data the heat mask's draw reads: its transform, its depth row and its control vector.</summary>
    public const int MaskPushFloats = 16 + 4 + 4;

    /// <summary>
    /// How far behind cover a body is still seen, in metres: the original's see-through draw is brought up to ten metres nearer
    /// the eye (<c>Direct3DRender_DrawBoneMesh</c>), and the remaster fades it out over the same reach.
    /// </summary>
    public const float SeenThroughReachMetres = 10f;

    /// <summary>How many frames the grain's phase counts before it wraps, so a float holds it exactly.</summary>
    public const int GrainCycle = 1024;

    private const RhiTextureFormat HeatFormat = RhiTextureFormat.Rgba16Float;

    private readonly IRhiShaderModule _maskModule;
    private readonly IRhiPipelineLayout _maskLayout;
    private readonly IRhiRenderPipeline _maskPipeline;
    private readonly ComputePass _composite;
    private readonly ComputeParameters _parameters;
    private readonly ScreenImage _heat;
    private readonly ScreenImage _output;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ThermalPass"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames the parameters are kept for at once.</param>
    /// <param name="retired">Where an image a resize replaces goes until the frames reading it finish.</param>
    public ThermalPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _heat = new ScreenImage(device, retired, HeatFormat, RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled, "thermal.heat");
        _output = new ScreenImage(device, retired, HeatFormat, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, "thermal.picture");

        ShaderReflection mask = ShaderCatalog.GetReflection(ShaderModuleId.ThermalMask);
        _maskModule = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.ThermalMask, Label: "thermal.heat"));
        _maskLayout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            MaskPushFloats * sizeof(float),
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "thermal.heat"));
        RhiBlendComponent greater = new(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Max);
        _maskPipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _maskLayout,
            new RhiShaderStage(
                _maskModule,
                mask.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(mask, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_maskModule, mask.EntryPointFor(ShaderStage.Fragment).Name),
            OutlinePipelineCache.VertexLayouts(),
            [new RhiColorTargetState(HeatFormat, new RhiBlendState(greater, greater), RhiColorWrite.All)],
            null,
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            "thermal.heat"));

        _composite = new ComputePass(device, ShaderModuleId.Thermal, "thermal");
        _parameters = new ComputeParameters(device, ParameterFloats * sizeof(float), framesInFlight, "thermal");
    }

    /// <summary>Draws the heat mask, ahead of the compute pass that reads it.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="draws">The bodies the sensor sees this frame; none leaves the mask empty.</param>
    public void RecordHeat(IRhiCommandEncoder encoder, int width, int height, IReadOnlyList<ThermalBodyDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(draws);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture heat = _heat.Ensure(width, height);
        Span<float> push = stackalloc float[MaskPushFloats];
        using IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(heat.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Label: "thermal.heat"));
        pass.SetPipeline(_maskPipeline);
        pass.SetViewport(0, 0, width, height, 0f, 1f);
        pass.SetScissor(0, 0, width, height);
        foreach (ThermalBodyDraw draw in draws)
        {
            WriteMask(push, draw);
            pass.SetPushData(MemoryMarshal.AsBytes(push));
            draw.Body.Draw(pass);
        }

        pass.EndPass();
    }

    /// <summary>Makes the sensor's picture from the lit scene, its depth and the heat mask drawn this frame.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="source">The lit scene, linear and unexposed.</param>
    /// <param name="depth">The scene's depth.</param>
    /// <param name="frame">What the picture is made against.</param>
    /// <param name="settings">The sensor's settings.</param>
    /// <param name="frameInFlight">The frame slot the parameters are written to.</param>
    /// <returns>The sensor's picture, at the source's size.</returns>
    public IRhiTextureView RecordPicture(
        IRhiComputePassEncoder pass,
        IRhiTextureView source,
        IRhiTextureView depth,
        in ThermalFrame frame,
        in ThermalDisplaySettings settings,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(depth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture heat = _heat.Ensure(frame.Width, frame.Height);
        IRhiTexture output = _output.Ensure(frame.Width, frame.Height);
        Span<float> block = stackalloc float[ParameterFloats];
        WriteParameters(block, frame, settings);

        _composite.Bind("uSource", source);
        _composite.Bind("uDepth", depth);
        _composite.Bind("uHeat", heat.View);
        _composite.Bind("uTarget", output.View);
        _composite.Bind("p", _parameters.Write<float>(frameInFlight, block));
        _composite.Dispatch(pass, frame.Width, frame.Height);
        return output.View;
    }

    /// <summary>Writes one body's push data for the heat mask.</summary>
    /// <param name="push">Room for <see cref="MaskPushFloats"/> floats.</param>
    /// <param name="draw">The body.</param>
    public static void WriteMask(Span<float> push, in ThermalBodyDraw draw)
    {
        if (push.Length != MaskPushFloats)
        {
            throw new ArgumentException($"The heat mask's push data is {MaskPushFloats} floats, not {push.Length}.", nameof(push));
        }

        push.Clear();
        draw.Body.Transform.CopyColumnMajorTo(push[..16]);
        push[16] = draw.DepthRow.X;
        push[17] = draw.DepthRow.Y;
        push[18] = draw.DepthRow.Z;
        push[19] = draw.DepthRow.W;
        push[20] = draw.Body.PoseBlend;
        push[21] = Math.Clamp(draw.Heat, 0f, 1f);
    }

    /// <summary>Writes the sensor's parameter block, in the order <c>Post/Thermal.slang</c> reads it.</summary>
    /// <param name="block">Room for <see cref="ParameterFloats"/> floats.</param>
    /// <param name="frame">What the picture is made against.</param>
    /// <param name="settings">The sensor's settings.</param>
    public static void WriteParameters(Span<float> block, in ThermalFrame frame, in ThermalDisplaySettings settings)
    {
        if (block.Length != ParameterFloats)
        {
            throw new ArgumentException($"The thermal block is {ParameterFloats} floats, not {block.Length}.", nameof(block));
        }

        int width = Math.Max(frame.Width, 1);
        int height = Math.Max(frame.Height, 1);
        block[0] = width;
        block[1] = height;
        block[2] = 1f / width;
        block[3] = 1f / height;

        block[4] = Math.Max(frame.DepthWidth, 1);
        block[5] = Math.Max(frame.DepthHeight, 1);
        block[6] = block[4] / width;
        block[7] = block[5] / height;

        block[8] = frame.Unprojection.XScale;
        block[9] = frame.Unprojection.YScale;
        block[10] = frame.Unprojection.Near;
        block[11] = frame.Unprojection.Far;

        block[12] = Math.Clamp(settings.Amount, 0f, 1f);
        block[13] = (int)settings.Palette;
        block[14] = settings.Seconds;
        block[15] = Math.Clamp(settings.Calibration, 0f, 1f);

        block[16] = MathF.Max(frame.Exposure, 1e-3f);
        block[17] = MathF.Max(settings.Noise, 0f);
        block[18] = SeenThroughReachMetres;
        block[19] = ((frame.Frame % GrainCycle) + GrainCycle) % GrainCycle;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _composite.Dispose();
        _parameters.Dispose();
        _maskPipeline.Dispose();
        _maskLayout.Dispose();
        _maskModule.Dispose();
        _heat.Dispose();
        _output.Dispose();
    }
}

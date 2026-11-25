using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>One of a race's ghost's bodies, as it was drawn.</summary>
/// <param name="Body">The body.</param>
/// <param name="DepthRow">Its model-view transform's third row: a point's distance ahead of the eye, a normal's turn towards it.</param>
internal readonly record struct GhostBodyDraw(PosedBody Body, System.Numerics.Vector4 DepthRow);

/// <summary>What the ghost is laid over the scene against beyond its look.</summary>
/// <param name="Width">The picture's width.</param>
/// <param name="Height">Its height.</param>
/// <param name="DepthWidth">The scene depth's width, smaller than the picture's after the scene is upscaled.</param>
/// <param name="DepthHeight">Its height.</param>
/// <param name="Unprojection">The projection's scales and its near and far planes, in metres.</param>
/// <param name="Exposure">The exposure the resolve will apply to the picture, which the ghost's own light is divided by.</param>
internal readonly record struct GhostFrame(int Width, int Height, int DepthWidth, int DepthHeight, DepthUnprojection Unprojection, float Exposure);

/// <summary>A race's ghost: its bodies drawn into a mask of their own, then laid over the lit scene with their glow and glitches.</summary>
/// <remarks>
/// Two passes, as the thermal sensor's: the bodies drawn, each in the pose it was drawn in, into the ghost mask
/// (<c>World/GhostMask.slang</c>) with no depth test; then one compute pass (<c>Post/Ghost.slang</c>) over the lit scene, its
/// depth and the mask, whose picture goes on to the bloom and the tone map, so the ghost's glow blooms. After the temporal
/// resolve, so the history never holds it and nothing of it smears.
/// </remarks>
internal sealed class GhostPass : IDisposable
{
    /// <summary>How many floats the composite's parameter block holds.</summary>
    public const int ParameterFloats = 20;

    /// <summary>How many floats of push data the mask's draw reads: its transform, its depth row and its control vector.</summary>
    public const int MaskPushFloats = 16 + 4 + 4;

    /// <summary>The ghost's colour, linear: a cold cyan the white of its brightest edges sits in.</summary>
    public static readonly (float R, float G, float B) Tint = (0.22f, 0.72f, 1f);

    /// <summary>How strong the ghost's light is, at the display's white before the exposure.</summary>
    public const float Strength = 1.4f;

    private const RhiTextureFormat MaskFormat = RhiTextureFormat.Rgba16Float;

    private readonly IRhiShaderModule _maskModule;
    private readonly IRhiPipelineLayout _maskLayout;
    private readonly IRhiRenderPipeline _maskPipeline;
    private readonly ComputePass _composite;
    private readonly ComputeParameters _parameters;
    private readonly ScreenImage _mask;
    private readonly ScreenImage _output;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GhostPass"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames the parameters are kept for at once.</param>
    /// <param name="retired">Where an image a resize replaces goes until the frames reading it finish.</param>
    public GhostPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _mask = new ScreenImage(device, retired, MaskFormat, RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled, "ghost.mask");
        _output = new ScreenImage(device, retired, MaskFormat, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, "ghost.picture");

        ShaderReflection mask = ShaderCatalog.GetReflection(ShaderModuleId.GhostMask);
        _maskModule = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.GhostMask, Label: "ghost.mask"));
        _maskLayout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            MaskPushFloats * sizeof(float),
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "ghost.mask"));
        RhiBlendComponent greater = new(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Max);
        _maskPipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _maskLayout,
            new RhiShaderStage(
                _maskModule,
                mask.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(mask, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_maskModule, mask.EntryPointFor(ShaderStage.Fragment).Name),
            OutlinePipelineCache.VertexLayouts(),
            [new RhiColorTargetState(MaskFormat, new RhiBlendState(greater, greater), RhiColorWrite.All)],
            null,
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            "ghost.mask"));

        _composite = new ComputePass(device, ShaderModuleId.Ghost, "ghost");
        _parameters = new ComputeParameters(device, ParameterFloats * sizeof(float), framesInFlight, "ghost");
    }

    /// <summary>Draws the ghost mask, ahead of the compute pass that reads it.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="draws">The ghost's bodies this frame.</param>
    /// <param name="seconds">How long the ghost has been shown, which moves its scan.</param>
    public void RecordMask(IRhiCommandEncoder encoder, int width, int height, IReadOnlyList<GhostBodyDraw> draws, float seconds)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(draws);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture mask = _mask.Ensure(width, height);
        Span<float> push = stackalloc float[MaskPushFloats];
        using IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(mask.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Label: "ghost.mask"));
        pass.SetPipeline(_maskPipeline);
        pass.SetViewport(0, 0, width, height, 0f, 1f);
        pass.SetScissor(0, 0, width, height);
        foreach (GhostBodyDraw draw in draws)
        {
            WriteMask(push, draw, seconds);
            pass.SetPushData(MemoryMarshal.AsBytes(push));
            draw.Body.Draw(pass);
        }

        pass.EndPass();
    }

    /// <summary>Lays the ghost drawn into the mask this frame over the lit scene.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="source">The lit scene, linear and unexposed.</param>
    /// <param name="depth">The scene's depth.</param>
    /// <param name="frame">What the ghost is laid against.</param>
    /// <param name="look">How it looks this frame.</param>
    /// <param name="frameInFlight">The frame slot the parameters are written to.</param>
    /// <returns>The picture, at the source's size.</returns>
    public IRhiTextureView RecordPicture(
        IRhiComputePassEncoder pass, IRhiTextureView source, IRhiTextureView depth, in GhostFrame frame, in GhostLook look, int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(depth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture mask = _mask.Ensure(frame.Width, frame.Height);
        IRhiTexture output = _output.Ensure(frame.Width, frame.Height);
        Span<float> block = stackalloc float[ParameterFloats];
        WriteParameters(block, frame, look);

        _composite.Bind("uSource", source);
        _composite.Bind("uDepth", depth);
        _composite.Bind("uGhost", mask.View);
        _composite.Bind("uTarget", output.View);
        _composite.Bind("p", _parameters.Write<float>(frameInFlight, block));
        _composite.Dispatch(pass, frame.Width, frame.Height);
        return output.View;
    }

    /// <summary>Writes one body's push data for the mask.</summary>
    /// <param name="push">Room for <see cref="MaskPushFloats"/> floats.</param>
    /// <param name="draw">The body.</param>
    /// <param name="seconds">How long the ghost has been shown.</param>
    public static void WriteMask(Span<float> push, in GhostBodyDraw draw, float seconds)
    {
        if (push.Length != MaskPushFloats)
        {
            throw new ArgumentException($"The ghost mask's push data is {MaskPushFloats} floats, not {push.Length}.", nameof(push));
        }

        push.Clear();
        draw.Body.Transform.CopyColumnMajorTo(push[..16]);
        push[16] = draw.DepthRow.X;
        push[17] = draw.DepthRow.Y;
        push[18] = draw.DepthRow.Z;
        push[19] = draw.DepthRow.W;
        push[20] = draw.Body.PoseBlend;
        push[21] = seconds;
    }

    /// <summary>Writes the composite's parameter block, in the order <c>Post/Ghost.slang</c> reads it.</summary>
    /// <param name="block">Room for <see cref="ParameterFloats"/> floats.</param>
    /// <param name="frame">What the ghost is laid against.</param>
    /// <param name="look">How it looks this frame.</param>
    public static void WriteParameters(Span<float> block, in GhostFrame frame, in GhostLook look)
    {
        if (block.Length != ParameterFloats)
        {
            throw new ArgumentException($"The ghost block is {ParameterFloats} floats, not {block.Length}.", nameof(block));
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

        // The seconds wrap an hour round, so a float still counts them in thirtieths.
        block[12] = look.Seconds % 3600f;
        block[13] = MathF.Max(frame.Exposure, 1e-3f);
        block[14] = Math.Clamp(look.Glitch, 0f, 1f);
        block[15] = look.GlitchSeed % 4096f;

        block[16] = Tint.R;
        block[17] = Tint.G;
        block[18] = Tint.B;
        block[19] = Strength;
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
        _mask.Dispose();
        _output.Dispose();
    }
}

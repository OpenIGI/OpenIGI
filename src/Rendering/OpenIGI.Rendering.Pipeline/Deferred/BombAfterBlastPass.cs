using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>What a bomb's after-blast is drawn against beyond its own settings.</summary>
/// <param name="Width">The picture's width.</param>
/// <param name="Height">Its height.</param>
/// <param name="DepthWidth">The scene depth's width, smaller than the picture's after the scene is upscaled.</param>
/// <param name="DepthHeight">Its height.</param>
/// <param name="Unprojection">The projection's scales and its near and far planes, in metres.</param>
/// <param name="Exposure">The exposure the resolve will apply to the picture.</param>
/// <param name="Frame">The frame's number, which moves the streak's jitter.</param>
internal readonly record struct BombAfterBlastFrame(
    int Width,
    int Height,
    int DepthWidth,
    int DepthHeight,
    DepthUnprojection Unprojection,
    float Exposure,
    int Frame);

/// <summary>A bomb's after-blast drawn over the lit scene: one compute pass (<c>Post/BombAfterBlast.slang</c>).</summary>
/// <remarks>
/// After the temporal resolve and before the bloom, so the history never holds the streak and the flaring parts of it bloom.
/// Its picture takes the scene's place for the rest of the frame.
/// </remarks>
internal sealed class BombAfterBlastPass : IDisposable
{
    /// <summary>How many floats the pass's parameter block holds.</summary>
    public const int ParameterFloats = 32;

    // How many frames the jitter counts before it wraps.
    private const int JitterCycle = 1024;

    private readonly ComputePass _pass;
    private readonly ComputeParameters _parameters;
    private readonly ScreenImage _output;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="BombAfterBlastPass"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames the parameters are kept for at once.</param>
    /// <param name="retired">Where an image a resize replaces goes until the frames reading it finish.</param>
    public BombAfterBlastPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);
        _output = new ScreenImage(device, retired, RhiTextureFormat.Rgba16Float, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, "afterblast.picture");
        _pass = new ComputePass(device, ShaderModuleId.BombAfterBlast, "afterblast");
        _parameters = new ComputeParameters(device, ParameterFloats * sizeof(float), framesInFlight, "afterblast");
    }

    /// <summary>Draws the after-blast over the lit scene.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="source">The lit scene, linear and unexposed.</param>
    /// <param name="depth">The scene's depth.</param>
    /// <param name="frame">What it is drawn against.</param>
    /// <param name="settings">The after-blast at this frame.</param>
    /// <param name="frameInFlight">The frame slot the parameters are written to.</param>
    /// <returns>The picture, at the source's size.</returns>
    public IRhiTextureView Record(
        IRhiComputePassEncoder pass,
        IRhiTextureView source,
        IRhiTextureView depth,
        in BombAfterBlastFrame frame,
        in BombAfterBlastSettings settings,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(depth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiTexture output = _output.Ensure(frame.Width, frame.Height);
        Span<float> block = stackalloc float[ParameterFloats];
        WriteParameters(block, frame, settings);
        _pass.Bind("uSource", source);
        _pass.Bind("uDepth", depth);
        _pass.Bind("uTarget", output.View);
        _pass.Bind("p", _parameters.Write<float>(frameInFlight, block));
        _pass.Dispatch(pass, frame.Width, frame.Height);
        return output.View;
    }

    /// <summary>Writes the pass's parameter block, in the order <c>Post/BombAfterBlast.slang</c> reads it.</summary>
    /// <param name="block">Room for <see cref="ParameterFloats"/> floats.</param>
    /// <param name="frame">What it is drawn against.</param>
    /// <param name="settings">The after-blast at this frame.</param>
    public static void WriteParameters(Span<float> block, in BombAfterBlastFrame frame, in BombAfterBlastSettings settings)
    {
        if (block.Length != ParameterFloats)
        {
            throw new ArgumentException($"The after-blast block is {ParameterFloats} floats, not {block.Length}.", nameof(block));
        }

        int width = Math.Max(frame.Width, 1);
        int height = Math.Max(frame.Height, 1);
        float exposure = MathF.Max(frame.Exposure, 1e-3f);
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

        block[12] = settings.BombView.X;
        block[13] = settings.BombView.Y;
        block[14] = settings.BombView.Z;
        block[15] = MathF.Max(settings.FrontMetres, 0f);

        block[16] = BombAfterBlastSettings.EdgeMetres;
        block[17] = BombAfterBlastSettings.WakeMetres;
        block[18] = BombAfterBlastSettings.StreakShare;
        block[19] = Math.Clamp(settings.Strength, 0f, 1f);

        block[20] = BombAfterBlastSettings.NearStartMetres;
        block[21] = BombAfterBlastSettings.NearFullMetres;
        block[22] = BombAfterBlastSettings.FarFullMetres;
        block[23] = BombAfterBlastSettings.FarGoneMetres;

        // The dust's colour as it is seen, back into the scene's linear light before the exposure.
        block[24] = Decode(BombAfterBlastSettings.HazeColour.X) / exposure;
        block[25] = Decode(BombAfterBlastSettings.HazeColour.Y) / exposure;
        block[26] = Decode(BombAfterBlastSettings.HazeColour.Z) / exposure;
        block[27] = Math.Clamp(settings.Haze, 0f, 1f);

        block[28] = exposure;
        block[29] = ((frame.Frame % JitterCycle) + JitterCycle) % JitterCycle;
        block[30] = Math.Clamp(settings.Taps, 1, 64);
        block[31] = 0f;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pass.Dispose();
        _parameters.Dispose();
        _output.Dispose();
    }

    private static float Decode(float encoded) =>
        encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}

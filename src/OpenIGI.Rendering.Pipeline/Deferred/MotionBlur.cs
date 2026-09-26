using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>The camera shutter: the frame smeared along each pixel's own motion.</summary>
/// <remarks>
/// <para>
/// A chain of four modules sharing one set of blocks (<see cref="MotionBlurParameters"/>): the colour halved,
/// each pixel's travel flattened and its tile's range recorded, each tile dilated by its neighbours, and the
/// gather along it. After the temporal resolve and before the bloom -- a lens sees the smeared picture, so a
/// bright moving highlight blooms along its own smear rather than from where it no longer is.
/// </para>
/// <para>
/// Five images, owned here and remade with the frame's size: the half colour, the flattened motion, the
/// tiles and their dilation, and the result the resolve reads. Half floats throughout.
/// </para>
/// </remarks>
public sealed class MotionBlur : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _half;
    private readonly ComputePass _flatten;
    private readonly ComputePass _dilate;
    private readonly ComputePass _apply;
    private readonly ComputeParameters _halfBlock;
    private readonly ComputeParameters _flattenBlock;
    private readonly ComputeParameters _dilateBlock;
    private readonly ComputeParameters _applyBlock;
    private readonly IRhiSampler _sampler;

    private IRhiTexture? _halfColour;
    private IRhiTexture? _flat;
    private IRhiTexture? _tiles;
    private IRhiTexture? _dilated;
    private IRhiTexture? _blurred;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="MotionBlur"/> class.</summary>
    /// <param name="device">The device the images live on.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where images a resize replaces go until the frames reading them finish.</param>
    public MotionBlur(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _half = new ComputePass(device, ShaderModuleId.BloomDownsample, "motionblur.half");
        _flatten = new ComputePass(device, ShaderModuleId.MotionBlurFlatten, "motionblur.flatten");
        _dilate = new ComputePass(device, ShaderModuleId.MotionBlurDilate, "motionblur.dilate");
        _apply = new ComputePass(device, ShaderModuleId.MotionBlurApply, "motionblur.apply");

        int small = MotionBlurParameters.SmallFloats * sizeof(float);
        _halfBlock = new ComputeParameters(device, small, framesInFlight, "motionblur.half");
        _flattenBlock = new ComputeParameters(
            device, MotionBlurParameters.FlattenFloats * sizeof(float), framesInFlight, "motionblur.flatten");
        _dilateBlock = new ComputeParameters(device, small, framesInFlight, "motionblur.dilate");
        _applyBlock = new ComputeParameters(device, small, framesInFlight, "motionblur.apply");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "motionblur"));
    }

    /// <summary>Gets the smeared colour, or null before a frame has been blurred.</summary>
    public IRhiTextureView? Result => _blurred?.View;

    /// <summary>Smears one frame's colour along its motion.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frameInFlight">Which set of per-frame resources this frame may write.</param>
    /// <param name="colour">The colour to smear: the temporal output, or the lit image where that did not run.</param>
    /// <param name="velocity">Each pixel's motion, as a UV delta.</param>
    /// <param name="depth">The scene depth.</param>
    /// <param name="width">The width the colour is at.</param>
    /// <param name="height">Its height.</param>
    /// <param name="post">The frame's post settings: the amount and the frame time.</param>
    /// <param name="unprojection">The projection's planes, for linearising depth.</param>
    /// <returns>The smeared colour.</returns>
    public IRhiTextureView Record(
        IRhiComputePassEncoder pass,
        int frameInFlight,
        IRhiTextureView colour,
        IRhiTextureView velocity,
        IRhiTextureView depth,
        int width,
        int height,
        in PostProcessSettings post,
        in DepthUnprojection unprojection)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(colour);
        ArgumentNullException.ThrowIfNull(velocity);
        ArgumentNullException.ThrowIfNull(depth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Ensure(width, height);

        int halfWidth = Math.Max(1, width / 2);
        int halfHeight = Math.Max(1, height / 2);
        int tilesX = MotionBlurParameters.TilesFor(width);
        int tilesY = MotionBlurParameters.TilesFor(height);

        Span<float> block = stackalloc float[MotionBlurParameters.SmallFloats];
        Span<float> flatten = stackalloc float[MotionBlurParameters.FlattenFloats];

        MotionBlurParameters.WriteHalf(block, halfWidth, halfHeight);
        _half.Bind("uSource", colour, _sampler);
        _half.Bind("uTarget", _halfColour!.View);
        _half.Bind("p", _halfBlock.Write<float>(frameInFlight, block));
        _half.Dispatch(pass, halfWidth, halfHeight);

        MotionBlurParameters.WriteFlatten(
            flatten,
            width,
            height,
            unprojection.Near,
            unprojection.Far,
            post.MotionBlur,
            MotionBlurParameters.MaxPercentOfScreenWidth,
            post.FrameDeltaSeconds);
        _flatten.Bind("uVelocity", velocity, _sampler);
        _flatten.Bind("uDepth", depth, _sampler);
        _flatten.Bind("uFlat", _flat!.View);
        _flatten.Bind("uTile", _tiles!.View);
        _flatten.Bind("p", _flattenBlock.Write<float>(frameInFlight, flatten));
        _flatten.Dispatch(pass, width, height);

        MotionBlurParameters.WriteDilate(block, tilesX, tilesY);
        _dilate.Bind("uTile", _tiles.View, _sampler);
        _dilate.Bind("uDilated", _dilated!.View);
        _dilate.Bind("p", _dilateBlock.Write<float>(frameInFlight, block));
        _dilate.Dispatch(pass, tilesX, tilesY);

        MotionBlurParameters.WriteApply(block, width, height, tilesX, tilesY);
        _apply.Bind("uColour", colour, _sampler);
        _apply.Bind("uHalf", _halfColour.View, _sampler);
        _apply.Bind("uFlat", _flat.View, _sampler);
        _apply.Bind("uDilated", _dilated.View, _sampler);
        _apply.Bind("uTarget", _blurred!.View);
        _apply.Bind("p", _applyBlock.Write<float>(frameInFlight, block));
        _apply.Dispatch(pass, width, height);

        return _blurred.View;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiTexture? image in (IRhiTexture?[])[_halfColour, _flat, _tiles, _dilated, _blurred])
        {
            image?.Dispose();
        }

        _sampler.Dispose();
        _applyBlock.Dispose();
        _dilateBlock.Dispose();
        _flattenBlock.Dispose();
        _halfBlock.Dispose();
        _apply.Dispose();
        _dilate.Dispose();
        _flatten.Dispose();
        _half.Dispose();
    }

    private void Ensure(int width, int height)
    {
        if (_blurred is not null && width == _width && height == _height)
        {
            return;
        }

        foreach (IRhiTexture? image in (IRhiTexture?[])[_halfColour, _flat, _tiles, _dilated, _blurred])
        {
            if (image is not null)
            {
                _retired.Retire(image);
            }
        }

        _width = width;
        _height = height;

        int tilesX = MotionBlurParameters.TilesFor(width);
        int tilesY = MotionBlurParameters.TilesFor(height);

        _halfColour = Create(Math.Max(1, width / 2), Math.Max(1, height / 2), "motionblur.half");
        _flat = Create(width, height, "motionblur.flat");
        _tiles = Create(tilesX, tilesY, "motionblur.tiles");
        _dilated = Create(tilesX, tilesY, "motionblur.dilated");
        _blurred = Create(width, height, "motionblur.result");
    }

    private IRhiTexture Create(int width, int height, string label) => _device.CreateTexture(new RhiTextureDescriptor(
        width,
        height,
        RhiTextureFormat.Rgba16Float,
        RhiTextureUsage.Storage | RhiTextureUsage.Sampled,
        Label: label));
}

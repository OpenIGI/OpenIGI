using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The lens scatter: the scene halved six times, each level blurred where it stands, and the six summed
/// back at half the frame's size for the resolve to add before its tone map.
/// </summary>
/// <remarks>
/// <para>
/// Each dispatch has a pass of its own rather than one pass rebound six times, because a pass keeps the group
/// it last built and builds another only when what it binds changes: rebinding one pass per level would build
/// nineteen groups a frame, every frame, where this builds them once.
/// </para>
/// <para>
/// Only the top level sees the threshold. Every level below it is a blur of the level above, which has
/// already been cut.
/// </para>
/// </remarks>
public sealed class BloomPass : IDisposable
{
    /// <summary>How many times the scene is halved.</summary>
    public const int Levels = 6;

    // How much each level adds to the sum, finest first. Broad levels add less, so the glow falls off
    // smoothly with distance from a bright source rather than ending in a hard ring; together they come to
    // a little under four fifths, the rest of the strength being the intensity's to give.
    private static readonly float[] LevelWeights = [0.3465f, 0.138f, 0.1176f, 0.066f, 0.066f, 0.061f];

    private const RhiTextureFormat Format = RhiTextureFormat.Rgba16Float;
    private const int BlockFloats = 8;
    private const int CombineFloats = 12;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass[] _downsample = new ComputePass[Levels];
    private readonly ComputePass[] _horizontal = new ComputePass[Levels];
    private readonly ComputePass[] _vertical = new ComputePass[Levels];
    private readonly ComputeParameters[] _downsampleBlocks = new ComputeParameters[Levels];
    private readonly ComputeParameters[] _horizontalBlocks = new ComputeParameters[Levels];
    private readonly ComputeParameters[] _verticalBlocks = new ComputeParameters[Levels];
    private readonly ComputePass _combine;
    private readonly ComputeParameters _combineBlock;
    private readonly IRhiSampler _sampler;

    private IRhiTexture[] _chain = [];
    private IRhiTexture[] _scratch = [];
    private IRhiTexture? _result;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="BloomPass"/> class.</summary>
    /// <param name="device">The device the passes run on.</param>
    /// <param name="framesInFlight">How many parameter buffers to keep of each.</param>
    /// <param name="retired">Where the pyramid goes when a resize replaces it, until no frame reads it.</param>
    public BloomPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;

        for (int level = 0; level < Levels; level++)
        {
            _downsample[level] = new ComputePass(device, ShaderModuleId.BloomDownsample, $"bloom.down{level}");
            _horizontal[level] = new ComputePass(device, ShaderModuleId.BloomBlur, $"bloom.blur{level}.h");
            _vertical[level] = new ComputePass(device, ShaderModuleId.BloomBlur, $"bloom.blur{level}.v");

            _downsampleBlocks[level] = Block(device, BlockFloats, framesInFlight, $"bloom.down{level}");
            _horizontalBlocks[level] = Block(device, BlockFloats, framesInFlight, $"bloom.blur{level}.h");
            _verticalBlocks[level] = Block(device, BlockFloats, framesInFlight, $"bloom.blur{level}.v");
        }

        _combine = new ComputePass(device, ShaderModuleId.BloomCombine, "bloom.combine");
        _combineBlock = Block(device, CombineFloats, framesInFlight, "bloom.combine");

        // Bilinear and clamped: the downsample's four taps and the blur's in-between offsets both land
        // between texels, and an edge has no neighbour to wrap to.
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "bloom"));
    }

    /// <summary>Gets the summed bloom, or null before the pyramid has been built.</summary>
    public IRhiTextureView? Result => _result?.View;

    /// <summary>Records the whole chain over a scene image: down the pyramid, blur each level, then sum.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="scene">The image the pyramid is built from, before it is tone mapped.</param>
    /// <param name="width">The scene's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="intensity">How strongly the sum is added.</param>
    /// <param name="threshold">The luminance a pixel must pass before it spills, or nought for all of it.</param>
    /// <param name="frameInFlight">Which parameter buffers this frame writes.</param>
    public void Record(
        IRhiComputePassEncoder pass,
        IRhiTextureView scene,
        int width,
        int height,
        float intensity,
        float threshold,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Resize(width, height);

        Span<float> block = stackalloc float[BlockFloats];

        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);

            Size(block, w, h);
            block[4] = level == 0 ? threshold : 0f;

            ComputePass down = _downsample[level];
            down.Bind("uSource", level == 0 ? scene : _chain[level - 1].View, _sampler);
            down.Bind("uTarget", _chain[level].View);
            down.Bind("p", _downsampleBlocks[level].Write<float>(frameInFlight, block));
            down.Dispatch(pass, w, h);
        }

        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);

            Size(block, w, h);
            block[4] = 1f;
            Blur(pass, _horizontal[level], _chain[level], _scratch[level], _horizontalBlocks[level].Write<float>(frameInFlight, block), w, h);

            Size(block, w, h);
            block[5] = 1f;
            Blur(pass, _vertical[level], _scratch[level], _chain[level], _verticalBlocks[level].Write<float>(frameInFlight, block), w, h);
        }

        (int cw, int ch) = LevelSize(0);
        Span<float> combine = stackalloc float[CombineFloats];
        Size(combine, cw, ch);
        LevelWeights.AsSpan(0, 4).CopyTo(combine[4..]);
        combine[8] = LevelWeights[4];
        combine[9] = LevelWeights[5];
        combine[10] = intensity;

        for (int level = 0; level < Levels; level++)
        {
            _combine.Bind($"uLevel{level}", _chain[level].View, _sampler);
        }

        _combine.Bind("uTarget", _result!.View);
        _combine.Bind("p", _combineBlock.Write<float>(frameInFlight, combine));
        _combine.Dispatch(pass, cw, ch);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleasePyramid(retire: false);

        for (int level = 0; level < Levels; level++)
        {
            _downsample[level].Dispose();
            _horizontal[level].Dispose();
            _vertical[level].Dispose();
            _downsampleBlocks[level].Dispose();
            _horizontalBlocks[level].Dispose();
            _verticalBlocks[level].Dispose();
        }

        _combine.Dispose();
        _combineBlock.Dispose();
        _sampler.Dispose();
    }

    private static ComputeParameters Block(IRhiDevice device, int floats, int framesInFlight, string label) =>
        new(device, floats * sizeof(float), framesInFlight, label);

    private static void Size(Span<float> block, int width, int height)
    {
        block.Clear();
        block[0] = width;
        block[1] = height;
        block[2] = 1f / width;
        block[3] = 1f / height;
    }

    private void Blur(
        IRhiComputePassEncoder pass,
        ComputePass blur,
        IRhiTexture source,
        IRhiTexture target,
        IRhiBuffer parameters,
        int width,
        int height)
    {
        blur.Bind("uSource", source.View, _sampler);
        blur.Bind("uTarget", target.View);
        blur.Bind("p", parameters);
        blur.Dispatch(pass, width, height);
    }

    /// <summary>Rebuilds the pyramid for a scene size, if it is not already that size.</summary>
    private void Resize(int width, int height)
    {
        if (_width == width && _height == height && _result is not null)
        {
            return;
        }

        ReleasePyramid(retire: true);
        _width = width;
        _height = height;

        _chain = new IRhiTexture[Levels];
        _scratch = new IRhiTexture[Levels];

        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);
            _chain[level] = Level(w, h, $"bloom.chain{level}");
            _scratch[level] = Level(w, h, $"bloom.scratch{level}");
        }

        (int rw, int rh) = LevelSize(0);
        _result = Level(rw, rh, "bloom.result");
    }

    private IRhiTexture Level(int width, int height, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            Format,
            RhiTextureUsage.Storage | RhiTextureUsage.Sampled,
            Label: label));

    /// <summary>Lets the pyramid go: retired against the frame when a resize replaces it, freed on dispose.</summary>
    private void ReleasePyramid(bool retire)
    {
        foreach (IRhiTexture? texture in (IRhiTexture?[])[.. _chain, .. _scratch, _result])
        {
            if (texture is null)
            {
                continue;
            }

            if (retire)
            {
                _retired.Retire(texture);
            }
            else
            {
                texture.Dispose();
            }
        }

        _chain = [];
        _scratch = [];
        _result = null;
    }

    /// <summary>A level's size: the top one is half the scene, and each one below it half again.</summary>
    private (int Width, int Height) LevelSize(int level)
    {
        int shift = level + 1;
        return (Math.Max(1, _width >> shift), Math.Max(1, _height >> shift));
    }
}

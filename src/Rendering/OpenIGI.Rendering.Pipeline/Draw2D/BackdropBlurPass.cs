using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>
/// Blurs a copy of the frame into a short pyramid the interface's frosted glass samples: each level half
/// the size of the one before and blurred by a gaussian, so each is twice as blurred.
/// </summary>
/// <remarks>
/// <para>
/// The bloom pyramid's two kernels, reused with the threshold off: the four-tap downsample halves the
/// resolution and the separable sigma-two gaussian softens each level. A level <c>L</c> is at
/// <c>1/2^(L+1)</c> of the frame, so its blur in full-resolution pixels is about <c>2^(L+2)</c> — 4, 8, 16,
/// 32, 64 — which covers the design's 6-unit backdrop and 14 to 22-unit glass at every resolution. The 2D
/// pass cross-fades the two levels either side of a requested blur.
/// </para>
/// <para>
/// The frame is sampled through an sRGB view, so the pyramid holds linear light: the same thing a 2D draw
/// gets from any sRGB texture, which is what lets the glass draw through the ordinary sprite pipeline.
/// </para>
/// </remarks>
public sealed class BackdropBlurPass : IDisposable
{
    /// <summary>How many levels the pyramid has.</summary>
    public const int Levels = 5;

    private const RhiTextureFormat Format = RhiTextureFormat.Rgba16Float;
    private const int BlockFloats = 8;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass[] _downsample = new ComputePass[Levels];
    private readonly ComputePass[] _horizontal = new ComputePass[Levels];
    private readonly ComputePass[] _vertical = new ComputePass[Levels];
    private readonly ComputeParameters[] _downsampleBlocks = new ComputeParameters[Levels];
    private readonly ComputeParameters[] _horizontalBlocks = new ComputeParameters[Levels];
    private readonly ComputeParameters[] _verticalBlocks = new ComputeParameters[Levels];
    private readonly IRhiSampler _sampler;

    private IRhiTexture[] _chain = [];
    private IRhiTexture[] _scratch = [];
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="BackdropBlurPass"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames can be in flight at once.</param>
    /// <param name="retired">Where textures go to die once the GPU is done with them.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public BackdropBlurPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;

        for (int level = 0; level < Levels; level++)
        {
            _downsample[level] = new ComputePass(device, ShaderModuleId.BloomDownsample, $"backdrop.down{level}");
            _horizontal[level] = new ComputePass(device, ShaderModuleId.BloomBlur, $"backdrop.blur{level}.h");
            _vertical[level] = new ComputePass(device, ShaderModuleId.BloomBlur, $"backdrop.blur{level}.v");

            _downsampleBlocks[level] = new ComputeParameters(device, BlockFloats * sizeof(float), framesInFlight, $"backdrop.down{level}");
            _horizontalBlocks[level] = new ComputeParameters(device, BlockFloats * sizeof(float), framesInFlight, $"backdrop.blur{level}.h");
            _verticalBlocks[level] = new ComputeParameters(device, BlockFloats * sizeof(float), framesInFlight, $"backdrop.blur{level}.v");
        }

        // Bilinear and clamped: the taps land between texels, and an edge has no neighbour to wrap to.
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "backdrop"));
    }

    /// <summary>Raised when the pyramid is remade at a new size, so handles to the old levels can be let go.</summary>
    public event Action? Resized;

    /// <summary>Gets the levels' textures, least blurred first; empty before the first record.</summary>
    /// <remarks>The caller adopts these into its texture table, which then owns them; this pass never disposes them.</remarks>
    public IReadOnlyList<IRhiTexture> Chain => _chain;

    /// <summary>Gets a level's blur in full-resolution pixels.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The approximate gaussian sigma.</returns>
    public static float SigmaOf(int level) => MathF.Pow(2f, level + 2);

    /// <summary>Records the downsample and blur of every level.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="frame">The copy of the frame, sampled through an sRGB view.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">The frame's height.</param>
    /// <param name="frameInFlight">The frame-in-flight slot whose parameter blocks to write.</param>
    public void Record(IRhiComputePassEncoder pass, IRhiTextureView frame, int width, int height, int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Resize(width, height);
        Span<float> block = stackalloc float[BlockFloats];

        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);
            Size(block, w, h);

            // A threshold of zero is the shader's "no threshold": every pixel goes down the pyramid.
            block[4] = 0f;

            ComputePass down = _downsample[level];
            down.Bind("uSource", level == 0 ? frame : _chain[level - 1].View, _sampler);
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

        _sampler.Dispose();
    }

    private static void Size(Span<float> block, int width, int height)
    {
        block.Clear();
        block[0] = width;
        block[1] = height;
        block[2] = 1f / width;
        block[3] = 1f / height;
    }

    private void Blur(IRhiComputePassEncoder pass, ComputePass blur, IRhiTexture source, IRhiTexture target, IRhiBuffer parameters, int width, int height)
    {
        blur.Bind("uSource", source.View, _sampler);
        blur.Bind("uTarget", target.View);
        blur.Bind("p", parameters);
        blur.Dispatch(pass, width, height);
    }

    private void Resize(int width, int height)
    {
        if (_width == width && _height == height && _chain.Length == Levels)
        {
            return;
        }

        // The chain textures belong to the texture table once the device has adopted them, and it lets
        // them go when told the pyramid has been remade; the scratch levels are this pass's own.
        Resized?.Invoke();
        ReleasePyramid(retire: true);
        _width = width;
        _height = height;

        _chain = new IRhiTexture[Levels];
        _scratch = new IRhiTexture[Levels];
        for (int level = 0; level < Levels; level++)
        {
            (int w, int h) = LevelSize(level);
            _chain[level] = Level(w, h, $"backdrop.chain{level}");
            _scratch[level] = Level(w, h, $"backdrop.scratch{level}");
        }
    }

    private IRhiTexture Level(int width, int height, string label) =>
        _device.CreateTexture(new RhiTextureDescriptor(width, height, Format, RhiTextureUsage.Storage | RhiTextureUsage.Sampled, Label: label));

    private void ReleasePyramid(bool retire)
    {
        foreach (IRhiTexture texture in _scratch)
        {
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
    }

    private (int Width, int Height) LevelSize(int level)
    {
        int shift = level + 1;
        return (Math.Max(1, _width >> shift), Math.Max(1, _height >> shift));
    }
}

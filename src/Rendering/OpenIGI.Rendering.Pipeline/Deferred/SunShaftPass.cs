using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>Light shafts from the sun, built in screen space and laid onto the bloom.</summary>
/// <remarks>
/// <para>
/// The sky around the sun's disc is masked out at the bloom's size, blurred twice along lines towards the
/// sun's place on screen, and added to the bloom, which the resolve exposes with the scene. Whatever stands
/// between the eye and the sun cuts its shape into the glow as rays. See Post/SunShafts.slang.
/// </para>
/// <para>
/// Each dispatch has a pass of its own, as the bloom's do, so each keeps the group it built rather than
/// building another every frame.
/// </para>
/// </remarks>
public sealed class SunShaftPass : IDisposable
{
    private const RhiTextureFormat Format = RhiTextureFormat.Rgba16Float;
    private const int BlockFloats = 16;

    // The two blurs' steps, each as a share of the way to the sun per tap: the first reaches all the way in
    // its sixteen taps, the second fills the gap between two of them with its own sixteen. Half the way left
    // a ray no longer than the glow it came from.
    private const float FirstStep = 1f / 16f;
    private const float SecondStep = 1f / 256f;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _mask;
    private readonly ComputePass _first;
    private readonly ComputePass _second;
    private readonly ComputePass _composite;
    private readonly ComputeParameters _maskBlock;
    private readonly ComputeParameters _firstBlock;
    private readonly ComputeParameters _secondBlock;
    private readonly ComputeParameters _compositeBlock;
    private readonly IRhiSampler _sampler;

    private IRhiTexture? _a;
    private IRhiTexture? _b;
    private IRhiTexture? _result;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SunShaftPass"/> class.</summary>
    /// <param name="device">The device the passes run on.</param>
    /// <param name="framesInFlight">How many parameter buffers to keep of each.</param>
    /// <param name="retired">Where the images go when a resize replaces them, until no frame reads them.</param>
    public SunShaftPass(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
        _mask = new ComputePass(device, ShaderModuleId.SunShafts, "shafts.mask");
        _first = new ComputePass(device, ShaderModuleId.SunShafts, "shafts.blur1");
        _second = new ComputePass(device, ShaderModuleId.SunShafts, "shafts.blur2");
        _composite = new ComputePass(device, ShaderModuleId.SunShafts, "shafts.composite");
        _maskBlock = Block(framesInFlight, "shafts.mask");
        _firstBlock = Block(framesInFlight, "shafts.blur1");
        _secondBlock = Block(framesInFlight, "shafts.blur2");
        _compositeBlock = Block(framesInFlight, "shafts.composite");

        // Bilinear and clamped: the blur's taps land between texels, and the sun may be off the screen.
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "shafts"));
    }

    /// <summary>Gets the bloom with the shafts on it, or null before one was built.</summary>
    public IRhiTextureView? Result => _result?.View;

    /// <summary>Builds the shafts and lays them onto the bloom.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="scene">The picture the bloom was built from, before the tone map.</param>
    /// <param name="sceneWidth">Its width.</param>
    /// <param name="sceneHeight">Its height.</param>
    /// <param name="depth">The scene's depth, nought where it is sky.</param>
    /// <param name="depthWidth">The depth's width.</param>
    /// <param name="depthHeight">Its height.</param>
    /// <param name="bloom">The bloom, at its own size.</param>
    /// <param name="width">The bloom's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="sunUv">Where the sun stands on screen.</param>
    /// <param name="strength">How strongly the shafts are added.</param>
    /// <param name="visible">How much of the sun's effect its place on screen leaves, 0 to 1.</param>
    /// <param name="frameInFlight">Which parameter buffers this frame writes.</param>
    public void Record(
        IRhiComputePassEncoder pass,
        IRhiTextureView scene,
        int sceneWidth,
        int sceneHeight,
        IRhiTextureView depth,
        int depthWidth,
        int depthHeight,
        IRhiTextureView bloom,
        int width,
        int height,
        (float X, float Y) sunUv,
        float strength,
        float visible,
        int frameInFlight)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(bloom);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Resize(width, height);

        Span<float> block = stackalloc float[BlockFloats];

        Fill(block, width, height, sunUv, 0f, 0f, strength, visible, sceneWidth, sceneHeight, depthWidth, depthHeight);
        Run(pass, _mask, _maskBlock, frameInFlight, block, scene, depth, bloom, _a!, width, height);

        Fill(block, width, height, sunUv, 1f, FirstStep, strength, visible, sceneWidth, sceneHeight, depthWidth, depthHeight);
        Run(pass, _first, _firstBlock, frameInFlight, block, _a!.View, depth, bloom, _b!, width, height);

        Fill(block, width, height, sunUv, 2f, SecondStep, strength, visible, sceneWidth, sceneHeight, depthWidth, depthHeight);
        Run(pass, _second, _secondBlock, frameInFlight, block, _b!.View, depth, bloom, _a!, width, height);

        Fill(block, width, height, sunUv, 3f, 0f, strength, visible, sceneWidth, sceneHeight, depthWidth, depthHeight);
        _composite.Bind("uShafts", _a!.View, _sampler);
        Run(pass, _composite, _compositeBlock, frameInFlight, block, bloom, depth, null, _result!, width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _a?.Dispose();
        _b?.Dispose();
        _result?.Dispose();
        _mask.Dispose();
        _first.Dispose();
        _second.Dispose();
        _composite.Dispose();
        _maskBlock.Dispose();
        _firstBlock.Dispose();
        _secondBlock.Dispose();
        _compositeBlock.Dispose();
        _sampler.Dispose();
    }

    private static void Fill(
        Span<float> block,
        int width,
        int height,
        (float X, float Y) sunUv,
        float mode,
        float step,
        float strength,
        float visible,
        int sceneWidth,
        int sceneHeight,
        int depthWidth,
        int depthHeight)
    {
        block.Clear();
        block[0] = width;
        block[1] = height;
        block[2] = 1f / width;
        block[3] = 1f / height;
        block[4] = sunUv.X;
        block[5] = sunUv.Y;
        block[6] = mode;
        block[7] = step;

        // Strength; the sky brightness the mask ignores below (none: the sky is masked by distance from the
        // sun, which leaves an overcast sky's general brightness alone); how tightly it hugs the sun; and
        // how much the sun's place on screen leaves.
        block[8] = strength;
        block[9] = 0f;
        block[10] = 4f;
        block[11] = visible;

        block[12] = sceneWidth;
        block[13] = sceneHeight;
        block[14] = depthWidth;
        block[15] = depthHeight;
    }

    private ComputeParameters Block(int framesInFlight, string label) =>
        new(_device, BlockFloats * sizeof(float), framesInFlight, label);

    private void Run(
        IRhiComputePassEncoder pass,
        ComputePass stage,
        ComputeParameters parameters,
        int frameInFlight,
        ReadOnlySpan<float> block,
        IRhiTextureView source,
        IRhiTextureView depth,
        IRhiTextureView? shaftsStandIn,
        IRhiTexture target,
        int width,
        int height)
    {
        stage.Bind("uSource", source, _sampler);
        stage.Bind("uDepth", depth);
        if (shaftsStandIn is not null)
        {
            // Read only by the composite; every stage's layout declares it, so it is bound to something.
            stage.Bind("uShafts", shaftsStandIn, _sampler);
        }

        stage.Bind("uTarget", target.View);
        stage.Bind("p", parameters.Write<float>(frameInFlight, block));
        stage.Dispatch(pass, width, height);
    }

    private void Resize(int width, int height)
    {
        if (_result is not null && width == _width && height == _height)
        {
            return;
        }

        foreach (IRhiTexture? image in (IRhiTexture?[])[_a, _b, _result])
        {
            if (image is not null)
            {
                _retired.Retire(image);
            }
        }

        _width = width;
        _height = height;
        _a = Create("shafts.a");
        _b = Create("shafts.b");
        _result = Create("shafts.result");
    }

    private IRhiTexture Create(string label) => _device.CreateTexture(new RhiTextureDescriptor(
        Math.Max(_width, 1),
        Math.Max(_height, 1),
        Format,
        RhiTextureUsage.Storage | RhiTextureUsage.Sampled,
        Label: label));
}

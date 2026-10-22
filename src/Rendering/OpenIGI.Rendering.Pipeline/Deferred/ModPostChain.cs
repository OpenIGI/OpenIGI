using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>The passes mods run over the picture at one stage, one after another, each into the other's image.</summary>
/// <remarks>
/// Each pass is a <see cref="ComputePass"/> over a module that keeps the contract in <c>Shaders/Source/Mod/PostPass.slang</c>:
/// it binds <c>uSource</c>, <c>uDepth</c>, <c>uTarget</c> and <c>p</c>, and nothing else. A module that does not is refused when
/// it is set, with the reason in the log, and the rest run without it. The device keeps one chain for the passes over the lit
/// scene and one for those over the finished picture; the depth is the scene's in both, at the scene's resolution.
/// </remarks>
internal sealed class ModPostChain : IDisposable
{
    private const RhiTextureFormat Format = RhiTextureFormat.Rgba16Float;
    private const int BlockFloats = 28;
    private const int ValueCount = 16;
    private const string LogCategory = "Rendering";

    private static readonly string[] Contract = ["uSource", "uSourceSampler", "uDepth", "uTarget", "p"];

    private readonly IRhiDevice _device;
    private readonly int _framesInFlight;
    private readonly RetiredResources _retired;
    private readonly IOpenIgiLogger _logger;
    private readonly IRhiSampler _sampler;
    private readonly string _label;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Pass> _passes = [];

    private IRhiTexture? _a;
    private IRhiTexture? _b;
    private int _width;
    private int _height;
    private int _frame;

    /// <summary>Initializes a new instance of the <see cref="ModPostChain"/> class.</summary>
    /// <param name="device">The device the passes run on.</param>
    /// <param name="framesInFlight">How many frames the parameters are kept for at once.</param>
    /// <param name="retired">Where an image outgrown by a resize goes until the frames reading it are done.</param>
    /// <param name="logger">Where a refused pass is reported.</param>
    /// <param name="label">What its images and passes are labelled.</param>
    public ModPostChain(IRhiDevice device, int framesInFlight, RetiredResources retired, IOpenIgiLogger logger, string label = "mods")
    {
        _label = label;
        _device = device;
        _framesInFlight = framesInFlight;
        _retired = retired;
        _logger = logger;
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: label));
    }

    /// <summary>Gets whether any pass is set.</summary>
    public bool IsEmpty => _passes.Count == 0;

    /// <summary>Gets the names of the passes that run, in order.</summary>
    public IEnumerable<string> Names => _passes.Select(static pass => pass.Name);

    /// <summary>Sets the passes, in order; one whose module does not keep the contract is left out.</summary>
    /// <param name="passes">The passes.</param>
    public void Set(IReadOnlyList<PostPassModule> passes)
    {
        Clear();
        foreach (PostPassModule module in passes)
        {
            ComputePass? stage = null;
            try
            {
                stage = new ComputePass(_device, module.Module, $"mod.{module.Name}");
                if (Contract.FirstOrDefault(name => !stage.Declares(name)) is { } missing)
                {
                    _logger.Warning(LogCategory, $"The mod pass '{module.Name}' does not bind '{missing}', which the contract names; it is left out.");
                    stage.Dispose();
                    continue;
                }

                _passes.Add(new Pass(module.Name, stage, new ComputeParameters(_device, BlockFloats * sizeof(float), _framesInFlight, $"mod.{module.Name}")));
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                stage?.Dispose();
                _logger.Warning(LogCategory, $"The mod pass '{module.Name}' cannot be built and is left out: {error.Message}");
            }
        }
    }

    /// <summary>Sets the values a pass reads, up to sixteen.</summary>
    /// <param name="name">The pass's name.</param>
    /// <param name="values">The values.</param>
    public void SetValues(string name, ReadOnlySpan<float> values)
    {
        foreach (Pass pass in _passes)
        {
            if (pass.Name == name)
            {
                Array.Clear(pass.Values);
                values[..Math.Min(values.Length, ValueCount)].CopyTo(pass.Values);
            }
        }
    }

    /// <summary>Runs every pass over the picture.</summary>
    /// <param name="encoder">The compute pass they are recorded into.</param>
    /// <param name="source">The picture.</param>
    /// <param name="depth">The scene's depth.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <param name="depthRange">The near and far planes, in metres.</param>
    /// <param name="depthSize">The depth's width and height, which differ from the picture's after the scene is upscaled.</param>
    /// <param name="frameInFlight">The frame slot the parameters are written to.</param>
    /// <returns>The colour the last pass wrote, or <see langword="null"/> when none ran.</returns>
    public IRhiTextureView? Record(
        IRhiComputePassEncoder encoder,
        IRhiTextureView source,
        IRhiTextureView depth,
        int width,
        int height,
        (float Near, float Far) depthRange,
        (int Width, int Height) depthSize,
        int frameInFlight)
    {
        if (_passes.Count == 0)
        {
            return null;
        }

        Resize(width, height);
        _frame++;
        Span<float> block = stackalloc float[BlockFloats];
        IRhiTextureView input = source;
        IRhiTexture output = _a!;
        foreach (Pass pass in _passes)
        {
            block.Clear();
            block[0] = width;
            block[1] = height;
            block[2] = 1f / width;
            block[3] = 1f / height;
            block[4] = (float)_clock.Elapsed.TotalSeconds;
            block[5] = _frame;
            block[6] = depthRange.Near;
            block[7] = depthRange.Far;
            pass.Values.CopyTo(block[8..]);
            block[24] = depthSize.Width;
            block[25] = depthSize.Height;
            block[26] = depthSize.Width / (float)width;
            block[27] = depthSize.Height / (float)height;

            pass.Stage.Bind("uSource", input, _sampler);
            pass.Stage.Bind("uDepth", depth);
            pass.Stage.Bind("uTarget", output.View);
            pass.Stage.Bind("p", pass.Parameters.Write<float>(frameInFlight, block));
            pass.Stage.Dispatch(encoder, width, height);

            input = output.View;
            output = ReferenceEquals(output, _a) ? _b! : _a!;
        }

        return input;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Clear();
        _a?.Dispose();
        _b?.Dispose();
        _sampler.Dispose();
    }

    private void Clear()
    {
        foreach (Pass pass in _passes)
        {
            pass.Stage.Dispose();
            pass.Parameters.Dispose();
        }

        _passes.Clear();
    }

    private void Resize(int width, int height)
    {
        if (_a is not null && width == _width && height == _height)
        {
            return;
        }

        foreach (IRhiTexture? image in (IRhiTexture?[])[_a, _b])
        {
            if (image is not null)
            {
                _retired.Retire(image);
            }
        }

        _width = width;
        _height = height;
        _a = Create($"{_label}.a");
        _b = Create($"{_label}.b");
    }

    private IRhiTexture Create(string label) => _device.CreateTexture(new RhiTextureDescriptor(
        Math.Max(_width, 1),
        Math.Max(_height, 1),
        Format,
        RhiTextureUsage.Storage | RhiTextureUsage.Sampled,
        Label: label));

    // One pass: its name, its program, its parameters and the values its settings give it.
    private sealed record Pass(string Name, ComputePass Stage, ComputeParameters Parameters)
    {
        public float[] Values { get; } = new float[ValueCount];
    }
}

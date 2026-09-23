using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// The eye adaptation: the lit scene metered into a histogram, read back, and eased into the exposure the
/// resolve applies.
/// </summary>
/// <remarks>
/// <para>
/// The Vulkan renderer's metering on the RHI: the same ExposureHistogram module and parameter block, and the
/// same host half (<see cref="ExposureAdaptation"/>). The histogram is zeroed from a buffer of zeroes, built,
/// and copied into the frame slot's own readback buffer; the slot's previous histogram is read as the slot
/// comes round again, when the frame that wrote it has finished -- two frames of lag, as the Vulkan
/// renderer has, far below what an eye easing over a second can show.
/// </para>
/// <para>
/// SPIR-V only: the count is an image atomic, and WGSL has none on textures. A device without the form
/// stands this down (<see cref="RunsOn"/>) and the resolve keeps the setting's own exposure.
/// </para>
/// </remarks>
public sealed class AutoExposure : IDisposable
{
    private const int HistogramBytes = ExposureAdaptation.Bins * sizeof(uint);

    private readonly IRhiDevice _device;
    private readonly ComputePass _histogram;
    private readonly ComputeParameters _parameters;
    private readonly IRhiSampler _sampler;
    private readonly IRhiTexture _bins;
    private readonly IRhiBuffer _zeroes;
    private readonly IRhiBuffer[] _readback;
    private readonly IRhiReadback?[] _pending;
    private readonly bool[] _written;
    private readonly ExposureAdaptation _adaptation = new();
    private float _adapted = 1f;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="AutoExposure"/> class.</summary>
    /// <param name="device">The device, which must compile the histogram module (see <see cref="RunsOn"/>).</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public AutoExposure(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;
        _histogram = new ComputePass(device, ShaderModuleId.ExposureHistogram, "exposure.histogram");
        _parameters = new ComputeParameters(
            device, ExposureAdaptation.ParameterFloats * sizeof(float), framesInFlight, "exposure");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "exposure"));

        _bins = device.CreateTexture(new RhiTextureDescriptor(
            ExposureAdaptation.Bins,
            1,
            RhiTextureFormat.R32Uint,
            RhiTextureUsage.Storage | RhiTextureUsage.CopyDestination | RhiTextureUsage.CopySource,
            Label: "exposure.bins"));

        // A zeroed row to copy over the bins, because a storage image has no clear of its own on WebGPU's
        // terms and this keeps the pass to what both backends offer. A buffer starts zeroed on both.
        _zeroes = device.CreateBuffer(new RhiBufferDescriptor(
            HistogramBytes, RhiBufferUsage.CopySource | RhiBufferUsage.CopyDestination, "exposure.zeroes"));
        device.WriteBuffer<byte>(_zeroes, 0, new byte[HistogramBytes]);

        // One per slot, so a readback waits only on the frame that wrote it.
        _readback = new IRhiBuffer[framesInFlight];
        _pending = new IRhiReadback?[framesInFlight];
        _written = new bool[framesInFlight];

        for (int i = 0; i < framesInFlight; i++)
        {
            _readback[i] = device.CreateBuffer(new RhiBufferDescriptor(
                HistogramBytes, RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, $"exposure.readback.{i}"));
        }
    }

    /// <summary>Gets the exposure scale the metering last settled on; one before any histogram arrives.</summary>
    public float Adapted => _adapted;

    /// <summary>Gets whether a device can meter at all.</summary>
    /// <param name="device">The device.</param>
    /// <returns><see langword="true"/> where the histogram module has the device's form.</returns>
    public static bool RunsOn(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return ShaderCatalog.Compiles(ShaderModuleId.ExposureHistogram, ShaderVariant.Default, device.ShaderTarget);
    }

    /// <summary>Folds the histogram this frame slot last carried into the exposure, if it has arrived.</summary>
    /// <param name="frameInFlight">The slot about to be written again, whose previous frame has finished.</param>
    /// <param name="deltaSeconds">How much time this frame advances the easing by.</param>
    /// <returns>The exposure scale to apply this frame.</returns>
    public float Adapt(int frameInFlight, float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int slot = frameInFlight % _readback.Length;

        if (!_written[slot])
        {
            return _adapted;
        }

        // Asked for once and kept until it arrives: a browser cannot block for it, and neither should this.
        IRhiReadback data = _pending[slot] ??= _device.ReadBuffer(_readback[slot], 0, HistogramBytes);

        if (!data.Wait(TimeSpan.Zero) && !data.IsComplete)
        {
            return _adapted;
        }

        _adapted = _adaptation.Adapt(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.GetData()), deltaSeconds);

        data.Dispose();
        _pending[slot] = null;
        _written[slot] = false;
        return _adapted;
    }

    /// <summary>Meters this frame's lit scene into the slot's readback.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="frameInFlight">The slot this frame writes.</param>
    /// <param name="lit">The lit scene colour.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="timestamps">Where the stage's timing goes, if it is timed.</param>
    public void Record(
        IRhiCommandEncoder encoder,
        int frameInFlight,
        IRhiTextureView lit,
        int width,
        int height,
        RhiTimestampWrites? timestamps)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(lit);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int slot = frameInFlight % _readback.Length;

        // A readback still waiting on this slot is for a frame about to be overwritten, and a buffer still
        // mapped cannot be copied into.
        _pending[slot]?.Dispose();
        _pending[slot] = null;

        RhiExtent3D row = new(ExposureAdaptation.Bins, 1);
        encoder.CopyBufferToTexture(new RhiBufferTextureCopy(_zeroes, 0, HistogramBytes), new RhiTextureRegion(_bins), row);

        Span<float> parameters = stackalloc float[ExposureAdaptation.ParameterFloats];
        ExposureAdaptation.WriteHistogramParameters(parameters, width, height);

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(timestamps, "exposure")))
        {
            _histogram.Bind("uScene", lit, _sampler);
            _histogram.Bind("uHistogram", _bins.View);
            _histogram.Bind("p", _parameters.Write<float>(frameInFlight, parameters));
            _histogram.Dispatch(pass, width, height);
        }

        encoder.CopyTextureToBuffer(new RhiTextureRegion(_bins), new RhiBufferTextureCopy(_readback[slot], 0, HistogramBytes), row);
        _written[slot] = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiReadback? pending in _pending)
        {
            pending?.Dispose();
        }

        foreach (IRhiBuffer buffer in _readback)
        {
            buffer.Dispose();
        }

        _zeroes.Dispose();
        _bins.Dispose();
        _sampler.Dispose();
        _parameters.Dispose();
        _histogram.Dispose();
    }
}

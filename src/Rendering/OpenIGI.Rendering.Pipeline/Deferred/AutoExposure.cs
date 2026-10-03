using OpenIGI.Core.Diagnostics;
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
/// The ExposureHistogram module and its parameter block, with the host half in
/// <see cref="ExposureAdaptation"/>. The histogram is cleared, built, and copied into the frame slot's own
/// readback buffer; the slot's previous histogram is read as the slot comes round again, when the frame that
/// wrote it has finished -- two frames of lag, far below what an eye easing over a second can show.
/// </para>
/// <para>
/// The module's buffer form, counting into a storage buffer rather than an image: WGSL has atomics on storage
/// buffers and none on textures, so this runs on WebGPU too. A device without the form stands this down
/// (<see cref="RunsOn"/>) and the resolve keeps the setting's own exposure.
/// </para>
/// </remarks>
public sealed class AutoExposure : IDisposable
{
    private const int HistogramBytes = ExposureAdaptation.Bins * sizeof(uint);

    private readonly IRhiDevice _device;
    private readonly ComputePass _histogram;
    private readonly ComputeParameters _parameters;
    private readonly IRhiSampler _sampler;
    private readonly IRhiBuffer _bins;
    private readonly IRhiBuffer[] _readback;
    private readonly IRhiReadback?[] _pending;
    private readonly bool[] _written;
    private readonly ExposureAdaptation _adaptation;
    private float _adapted = 1f;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="AutoExposure"/> class.</summary>
    /// <param name="device">The device, which must compile the histogram module (see <see cref="RunsOn"/>).</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="logger">Where the metering is reported at Debug; nowhere when omitted.</param>
    public AutoExposure(IRhiDevice device, int framesInFlight, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;
        _adaptation = new ExposureAdaptation(logger);
        _histogram = new ComputePass(device, ShaderModuleId.ExposureHistogram, "exposure.histogram");
        _parameters = new ComputeParameters(
            device, ExposureAdaptation.ParameterFloats * sizeof(float), framesInFlight, "exposure");
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "exposure"));

        // The counters, a storage buffer rather than an image: its atomics are the only kind WGSL has.
        _bins = device.CreateBuffer(new RhiBufferDescriptor(
            HistogramBytes,
            RhiBufferUsage.Storage | RhiBufferUsage.CopySource | RhiBufferUsage.CopyDestination,
            "exposure.bins"));

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

    /// <summary>Starts mapping a slot whose histogram has just been submitted.</summary>
    /// <param name="frameInFlight">The slot the submission just sent copied into.</param>
    /// <remarks>
    /// As the frame is submitted, not when the slot comes round again: a map asked for then has only that
    /// moment to complete in, and on WebGPU it seldom has -- the eye then never moved past its first
    /// histogram. Started a frame ahead, it has had the whole frame.
    /// </remarks>
    public void RequestReadback(int frameInFlight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int slot = frameInFlight % _readback.Length;

        if (_written[slot])
        {
            _pending[slot] ??= _device.ReadBuffer(_readback[slot], 0, HistogramBytes);
        }
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

        // Kept until it arrives -- a browser cannot block for it, and neither should this -- and asked
        // for here only if the submission did not already ask.
        IRhiReadback data = _pending[slot] ??= _device.ReadBuffer(_readback[slot], 0, HistogramBytes);

        if (!data.Wait(TimeSpan.Zero) && !data.IsComplete)
        {
            return _adapted;
        }

        // How far into night the sky is, so the eye can open further under it; see ExposureAdaptation.
        _adapted = _adaptation.Adapt(
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.GetData()),
            deltaSeconds,
            RenderCVars.Nightfall.Value);

        data.Dispose();
        _pending[slot] = null;
        _written[slot] = false;
        return _adapted;
    }

    /// <summary>Meters this frame's lit scene into the slot's readback, unless that slot's map is still in flight.</summary>
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

        // A map still in flight is a histogram not yet read, and WebGPU refuses a copy into a buffer with a
        // map outstanding: this frame goes unmetered and the eye holds, which at a frame's cost is nothing.
        // Throwing the map away instead, as this did, threw away every histogram whose map was slower than
        // the frame -- on WebGPU, nearly all of them.
        if (_pending[slot] is { IsComplete: false })
        {
            return;
        }

        _pending[slot]?.Dispose();
        _pending[slot] = null;

        encoder.ClearBuffer(_bins, 0, HistogramBytes);

        Span<float> parameters = stackalloc float[ExposureAdaptation.ParameterFloats];
        ExposureAdaptation.WriteHistogramParameters(parameters, width, height);

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(timestamps, "exposure")))
        {
            _histogram.Bind("uScene", lit, _sampler);
            _histogram.Bind("uBins", _bins);
            _histogram.Bind("p", _parameters.Write<float>(frameInFlight, parameters));
            _histogram.Dispatch(pass, width, height);
        }

        encoder.CopyBufferToBuffer(_bins, 0, _readback[slot], 0, HistogramBytes);
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

        _bins.Dispose();
        _sampler.Dispose();
        _parameters.Dispose();
        _histogram.Dispose();
    }
}
